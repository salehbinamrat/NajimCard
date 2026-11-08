using System.Globalization;
using System.Text.RegularExpressions;
using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// Restrict PPP rate edits to explicit single selected RouterOS6 profiles.
/// All RouterOS writes are exact-ID and subject to global command allowlist.
/// </summary>
public sealed partial class RouterGateway
{
    public async Task<PppAccountMutationResult> SetPppProfileRateLimitAsync(
        PppProfile selected, string proposedRate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ValidateInternalId(selected.Id);
        var rate=(proposedRate??"").Trim();
        if (!Regex.IsMatch(rate,@"^[1-9][0-9]{0,8}[kKmMgG]?/[1-9][0-9]{0,8}[kKmMgG]?$",
            RegexOptions.CultureInvariant))
            throw new ArgumentException(
                "اكتب سرعة رفع/تنزيل بصيغة مثل 4M/4M؛ لا تُقبل الأوامر أو النصوص الأخرى.",
                nameof(proposedRate));
        if (string.IsNullOrWhiteSpace(selected.Name))
            throw new ArgumentException("اختر باقة PPP صحيحة أولاً.");
        if (client is not IRouterQueryClient query)
            throw new NotSupportedException(
                "تغيير سرعة PPP يحتاج قراءة موجهة إلى معرّف الباقة من RouterOS.");

        const string menu="/ppp/profile/print";
        var columns=new Dictionary<string,string>
        {
            [".proplist"]=".id,name,rate-limit,local-address,remote-address"
        };
        async Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> Read() =>
            await query.QueryByIdAsync(menu,selected.Id,columns,ct);

        var before=await Read();
        if (before.Count!=1 || Get(before[0],".id")!=selected.Id ||
            !before[0].ContainsKey("name") ||
            Get(before[0],"name")!=selected.Name ||
            !before[0].ContainsKey("rate-limit") ||
            Get(before[0],"rate-limit")!=selected.RateLimit ||
            !before[0].ContainsKey("local-address") ||
            Get(before[0],"local-address")!=selected.LocalAddress ||
            !before[0].ContainsKey("remote-address") ||
            Get(before[0],"remote-address")!=selected.RemoteAddress)
            throw new InvalidOperationException(
                "تغيرت الباقة المحددة أو عناوينها أو سرعتها؛ لم يُجر أي تعديل.");
        if (selected.RateLimit==rate)
            return new PppAccountMutationResult(true,false,
                "حد السرعة المطلوب مطبق بالفعل؛ لم يُرسل أمر تعديل.");

        try
        {
            await client.CommandAsync("/ppp/profile/set",
                new Dictionary<string,string>
                {
                    [".id"]=selected.Id,["rate-limit"]=rate
                },ct);
        }
        catch (RouterOsTrapException ex)
        {
            return new PppAccountMutationResult(false,false,
                "رفض الراوتر تعديل سرعة PPP: "+ex.Message);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            return new PppAccountMutationResult(false,true,
                "انقطع الاتصال بعد تعديل سرعة PPP؛ تحقق من الباقة قبل إعادة المحاولة.");
        }

        try
        {
            var after=await Read();
            if (after.Count==1 && Get(after[0],".id")==selected.Id &&
                Get(after[0],"name")==selected.Name &&
                after[0].ContainsKey("rate-limit") &&
                Get(after[0],"rate-limit")==rate &&
                Get(after[0],"local-address")==selected.LocalAddress &&
                Get(after[0],"remote-address")==selected.RemoteAddress)
                return new PppAccountMutationResult(true,false,
                    "تُغيّر حد سرعة باقة PPP المحددة وتم التحقق من تطبيقه.");
            return new PppAccountMutationResult(false,true,
                "رد الراوتر بالنجاح لكن لم تتأكد السرعة الجديدة؛ تحقق يدوياً.");
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException
            or RouterOsTrapException or InvalidDataException)
        {
            return new PppAccountMutationResult(false,true,
                "أُرسل تعديل سرعة PPP لكن تعذرت إعادة قراءة الحالة: "+ex.Message);
        }
    }
}
