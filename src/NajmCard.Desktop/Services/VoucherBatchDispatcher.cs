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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Count == 0 || batch.Count > 10000)
            throw new ArgumentException("عدد كروت الدفعة غير صالح.", nameof(batch));
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
        var existingNames = await gateway.ReadExistingVoucherNamesAsync(cancellationToken);
        var collisions = batch.Where(v => existingNames.Contains(v.Username))
            .Select(v => v.Username).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (collisions.Length > 0)
            throw new ArgumentException(
                "رقم الكرت موجود مسبقاً في Hotspot أو PPPoE أو User Manager. أُوقف إرسال الدفعة.");

        var failed = new List<string>();
        int succeeded = 0;
        foreach (var voucher in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // COMMIT to WAL+FULL synchronously BEFORE sending the first byte.
            store.BeginSend(voucher);
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
            store.FinishSend(voucher, state, error);
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
        }
        return new BatchDeliveryResult(succeeded, failed, false);
    }
}
