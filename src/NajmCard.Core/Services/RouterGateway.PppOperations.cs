using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// One-record PPP secret delete and one-record session disconnect with RouterOS6
/// server-side .id filters, preflight identity comparison, and readback.
/// There is deliberately no bulk delete/retry or unfiltered read fallback.
/// </summary>
public sealed partial class RouterGateway
{
    public Task<PppAccountMutationResult> DeletePppAccountAsync(
        PppAccount selected, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (string.IsNullOrWhiteSpace(selected.Name) ||
            string.IsNullOrWhiteSpace(selected.Profile) ||
            string.IsNullOrWhiteSpace(selected.Service))
            throw new ArgumentException("بيانات حساب PPP غير مكتملة.");
        return RemovePppSingleAsync("/ppp/secret",selected.Id,
            new Dictionary<string,string>(StringComparer.Ordinal)
            {
                ["name"]=selected.Name,["profile"]=selected.Profile,
                ["service"]=selected.Service,["disabled"]=selected.Disabled?"yes":"no"
            },"حساب PPP",ct);
    }

    public Task<PppAccountMutationResult> DisconnectPppSessionAsync(
        PppActiveSession selected, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (string.IsNullOrWhiteSpace(selected.Name) ||
            string.IsNullOrWhiteSpace(selected.Service) ||
            string.IsNullOrWhiteSpace(selected.Address))
            throw new ArgumentException("بيانات جلسة PPP غير مكتملة.");
        return RemovePppSingleAsync("/ppp/active",selected.Id,
            new Dictionary<string,string>(StringComparer.Ordinal)
            {
                ["name"]=selected.Name,["service"]=selected.Service,
                ["address"]=selected.Address,["caller-id"]=selected.CallerId
            },"جلسة PPP",ct);
    }

    private async Task<PppAccountMutationResult> RemovePppSingleAsync(
        string menu, string id, IReadOnlyDictionary<string,string> expected,
        string itemLabel, CancellationToken ct)
    {
        if (menu is not ("/ppp/active" or "/ppp/secret"))
            throw new ArgumentException("قائمة PPP غير معتمدة.");
        ValidateInternalId(id);
        if (client is not IRouterQueryClient targeted)
            throw new NotSupportedException("لا يُسمح بهذا الإجراء دون استعلام RouterOS موجّه بالمعرف.");
        var projection=".id,"+string.Join(",",expected.Keys);
        var attrs=new Dictionary<string,string> { [".proplist"]=projection };

        async Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> ReadTarget() =>
            await targeted.QueryByIdAsync(menu+"/print",id,attrs,ct);

        var fresh=await ReadTarget();
        if (fresh.Count!=1 || Get(fresh[0],".id")!=id ||
            expected.Any(field => !fresh[0].TryGetValue(field.Key,out var value) ||
                (field.Key=="disabled"
                    ? IsDisabledValue(value)!=IsDisabledValue(field.Value)
                    : !string.Equals(value,field.Value,StringComparison.Ordinal))))
            throw new InvalidOperationException(
                "تغيّرت هوية "+itemLabel+" أو بياناته؛ لم يُنفذ الحذف أو الفصل.");

        try
        {
            await client.CommandAsync(menu+"/remove",
                new Dictionary<string,string> { [".id"]=id },ct);
        }
        catch (RouterOsTrapException ex)
        {
            return new PppAccountMutationResult(false,false,
                "رفض الراوتر إزالة "+itemLabel+": "+ex.Message);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            return new PppAccountMutationResult(false,true,
                "قد يكون الراوتر أزال "+itemLabel+" رغم انقطاع الاتصال؛ لا تعاود التنفيذ.");
        }

        try
        {
            var after=await ReadTarget();
            return after.Count==0
                ? new PppAccountMutationResult(true,false,
                    "تمت إزالة "+itemLabel+" المحدد والتحقق من غيابه.")
                : new PppAccountMutationResult(false,true,
                    "رد الراوتر بالنجاح لكن "+itemLabel+" لم يختف؛ يلزم التحقق يدوياً.");
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException
            or RouterOsTrapException or InvalidDataException)
        {
            return new PppAccountMutationResult(false,true,
                "أُرسل الأمر لكن تعذرت إعادة التحقق من "+itemLabel+": "+ex.Message);
        }
    }

    private static bool IsDisabledValue(string value) =>
        value.Equals("yes",StringComparison.OrdinalIgnoreCase) ||
        value.Equals("true",StringComparison.OrdinalIgnoreCase) || value=="1";
}
