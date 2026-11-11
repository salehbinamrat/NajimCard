using System.IO;
using NajmCard.Core.Models;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Services;

/// <summary>
/// Durable, single-attempt RouterOS delivery. Mirrors Hadramout
/// core/delivery.py's SENDING -> SENT/FAILED/UNKNOWN transaction protocol.
/// </summary>
public sealed class VoucherBatchDispatcher(RouterGateway gateway, VoucherStore store)
{
    public async Task<BatchDeliveryResult> SendAsync(IReadOnlyList<Voucher> batch,
        CancellationToken cancellationToken = default,IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Count == 0 || batch.Count > 10000)
            throw new ArgumentException("عدد كروت الدفعة غير صالح.", nameof(batch));
        // Freeze the caller-owned collection before yielding. IReadOnlyList
        // does not prevent its owner from replacing cards during preflight.
        var snapshot=batch.ToArray();
        // SQLite FULL commits and DPAPI are synchronous. Keep the complete
        // sequential delivery transaction off the WPF dispatcher, including
        // its initial validation and continuations after each router reply.
        return await Task.Run(()=>SendSnapshotAsync(snapshot,cancellationToken,progress),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<BatchDeliveryResult> SendSnapshotAsync(
        IReadOnlyList<Voucher> batch,CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var first = batch[0] ?? throw new ArgumentException("الكرت الأول غير صالح.");
        var service = first.Service;
        if (service is not ("hotspot" or "userman" or "pppoe"))
            throw new ArgumentException("خدمة الدفعة غير معروفة.");
        if (string.IsNullOrWhiteSpace(first.BatchId))
            throw new ArgumentException("معرف دفعة الكروت مطلوب.");
        var customer = first.Customer;
        if (service == "userman" && string.IsNullOrWhiteSpace(customer))
            throw new ArgumentException("حساب مدير User Manager مطلوب.");
        if (batch.Any(x => x is null || x.Service != service ||
            x.BatchId != first.BatchId ||
            (service == "userman" && x.Customer != customer)))
            throw new ArgumentException("لا ترسل كروت خدمات أو حسابات مدير مختلفة معاً.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (batch.Any(x => !seen.Add(x.Username)))
            throw new ArgumentException("الدفعة تحتوي اسم كرت مكرراً.");

        var persisted = store.GetBatchVouchers(service, first.BatchId).ToLookup(
            x => x.Service + "\u0000" + x.Username, StringComparer.OrdinalIgnoreCase);
        foreach (var voucher in batch)
        {
            var key = voucher.Service + "\u0000" + voucher.Username;
            if (!persisted[key].Any(x => x.BatchId == voucher.BatchId
                && x.Profile == voucher.Profile && x.Customer == voucher.Customer
                && x.Password == voucher.Password && !x.SentToRouter
                && x.DeliveryStatus is ("CREATED" or "FAILED")))
                throw new InvalidOperationException(
                    "الكرت مُرسل أو قيد التحقق أو غير محفوظ؛ راجع الراوتر أولاً.");
        }

        // A complete cross-service read is mandatory; do not lease a card or send
        // any RouterOS writes if any service is unavailable/forbidden or names collide.
        progress?.Report("التحقق من الأرقام الموجودة على الراوتر...");
        var existingNames = await gateway.ReadExistingVoucherNamesAsync(cancellationToken);
        var collisions = batch.Where(v => existingNames.Contains(v.Username))
            .Select(v => v.Username).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (collisions.Length > 0)
            throw new ArgumentException(
                "رقم الكرت موجود مسبقاً في Hotspot أو PPPoE أو User Manager. أُوقف إرسال الدفعة.");

        // An active User Manager account is not a working Hotspot voucher
        // unless the local NAS is configured for RADIUS. Do this *before*
        // the SQLite SENDING lease and any RouterOS mutation.
        if(service=="userman")
        {
            if(batch.Any(v=>v.Profile!=first.Profile))
                throw new ArgumentException(
                    "لا يمكن إصدار باقات مختلفة داخل دفعة User Manager واحدة.");
            progress?.Report("فحص جاهزية User Manager وربط Hotspot مع RADIUS...");
            await gateway.VerifyUserManagerV6HotspotReadinessAsync(
                first.Profile,customer,cancellationToken);
        }

        var failed = new List<string>();
        int succeeded = 0;
        int processed = 0;
        // For thousands of vouchers, avoid flooding the WPF dispatcher with two
        // notifications per card. Still report the first, last, ~80 checkpoints,
        // and every non-success outcome, without hiding the actual results.
        int progressStride = Math.Max(1, batch.Count / 80);
        // One open WAL connection per batch; every card still has TWO separate
        // durable FULL commits (lease before network, result after receipt).
        using var sendSession = store.OpenSendSession();
        foreach (var voucher in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int number = processed + 1;
            bool reportCard = batch.Count <= 24 || number == 1 ||
                number == batch.Count || number % progressStride == 0;
            if (reportCard)
                progress?.Report($"إرسال الكرت {number} من {batch.Count} إلى الراوتر...");
            // COMMIT to WAL+FULL synchronously BEFORE sending the first byte.
            sendSession.BeginSend(voucher);
            string state;
            string error = "";
            if (service == "userman")
            {
                try
                {
                    var result = await gateway.CreateAndActivateUserManagerV6Async(
                        voucher, customer, cancellationToken);
                    state = result.Success ? "SENT" :
                        result.UncertainOutcome ? "UNKNOWN" : "FAILED";
                    error = result.Error ?? "";
                }
                catch (Exception ex)
                {
                    // A partially processed router write may have reached the NAS.
                    state = "UNKNOWN";
                    error = "نتيجة إصدار User Manager غير مؤكدة: " + ex.Message;
                }
            }
            else if (service == "pppoe")
            {
                try
                {
                    var result = await gateway.CreatePppoeVoucherAsync(voucher, cancellationToken);
                    state = result.Success ? "SENT" :
                        result.UncertainOutcome ? "UNKNOWN" : "FAILED";
                    error = result.Error ?? "";
                }
                catch (Exception ex)
                {
                    state = "UNKNOWN";
                    error = "نتيجة إصدار PPPoE غير مؤكدة: " + ex.Message;
                }
            }
            else
            {
                try
                {
                    await gateway.AddHotspotUserAsync(voucher, cancellationToken);
                    state = "SENT";
                }
                catch (RouterOsTrapException ex)
                {
                    state = "FAILED"; // definitive rejection, not a lost reply
                    error = ex.Message;
                }
                catch (Exception ex)
                {
                    state = "UNKNOWN";
                    error = "نتيجة إصدار Hotspot غير مؤكدة: " + ex.Message;
                }
            }

            // If the DB write fails, the SENDING lease is retained. Stop and
            // investigate; never assume a potentially created card is absent.
            sendSession.FinishSend(voucher, state, error);
            if (reportCard || state != "SENT")
                progress?.Report($"تسجيل نتيجة الكرت {number} من {batch.Count}: "+
                    (state=="SENT"?"تم الإرسال":state=="FAILED"?"رفضه الراوتر":"النتيجة غير مؤكدة"));
            switch (state)
            {
                case "SENT":
                    succeeded++;
                    break;
                case "FAILED":
                    failed.Add(voucher.Username);
                    break;
                default:
                    return new BatchDeliveryResult(succeeded, failed, true);
            }
            processed++;
        }
        return new BatchDeliveryResult(succeeded, failed, false);
    }
}
