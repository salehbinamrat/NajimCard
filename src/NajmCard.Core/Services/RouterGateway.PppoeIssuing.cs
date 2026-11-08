using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// RouterOS 6 PPPoE voucher creation matching Hadramout core/network.py
/// add_user(ppp): one /ppp/secret/add with service=pppoe.
/// </summary>
public sealed partial class RouterGateway
{
    public async Task<PppoeVoucherDeliveryResult> CreatePppoeVoucherAsync(
        Voucher voucher, CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(voucher);
        ct.ThrowIfCancellationRequested();
        if(voucher.Service!="pppoe")
            throw new ArgumentException("خدمة الكرت يجب أن تكون PPPoE.",nameof(voucher));
        if(voucher.Username.Length is < 1 or > 64 ||
            voucher.Username.Any(c=>char.IsControl(c)||char.IsWhiteSpace(c)))
            throw new ArgumentException("اسم حساب PPPoE غير صالح.",nameof(voucher));
        if(voucher.Password.Length is < 1 or > 128)
            throw new ArgumentException("كلمة مرور PPPoE غير صالحة.",nameof(voucher));
        if(string.IsNullOrWhiteSpace(voucher.Profile) || voucher.Profile.Length>100)
            throw new ArgumentException("اختر باقة PPPoE صالحة.",nameof(voucher));
        if(client is not IRouterReceiptClient receiptClient)
            throw new NotSupportedException(
                "يجب الحصول على معرف حساب PPPoE من رد RouterOS 6 لمنع إعادة الإصدار الخاطئة.");

        RouterCommandReply reply;
        try
        {
            reply=await receiptClient.CommandWithReceiptAsync(
                "/ppp/secret/add",
                new Dictionary<string,string>
                {
                    ["name"]=voucher.Username,
                    ["password"]=voucher.Password,
                    ["profile"]=voucher.Profile,
                    ["service"]="pppoe"
                },ct);
        }
        catch(RouterOsTrapException ex)
        {
            return new PppoeVoucherDeliveryResult(false,false,null,
                "رفض الراوتر إنشاء حساب PPPoE: "+ex.Message);
        }
        catch(Exception ex) when (ex is IOException or OperationCanceledException)
        {
            return new PppoeVoucherDeliveryResult(false,true,null,
                "قد يكون حساب PPPoE أُنشئ قبل انقطاع الاتصال. افحص الراوتر قبل إعادة الإصدار.");
        }

        reply.Done.TryGetValue("ret",out string? id);
        if(string.IsNullOrEmpty(id)||id.Length is < 2 or > 40 ||
            id[0]!='*'||id.Skip(1).Any(c=>!char.IsAsciiHexDigit(c)))
            return new PppoeVoucherDeliveryResult(false,true,null,
                "لا يوجد معرف جديد صالح وحيد من الراوتر؛ نتيجة الإنشاء غير مؤكدة.");

        return new PppoeVoucherDeliveryResult(true,false,id,null);
    }
}
