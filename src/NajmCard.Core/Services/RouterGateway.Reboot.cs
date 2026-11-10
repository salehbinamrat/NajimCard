using System.IO;

namespace NajmCard.Core.Services;

/// <summary>
/// Reboot is explicitly requested by the operator and is never automatically
/// replayed or treated as verified solely because the API socket disappeared.
/// </summary>
public sealed record RouterRebootRequestResult(bool Accepted,bool Uncertain,string Message);

public sealed partial class RouterGateway
{
    public async Task<RouterRebootRequestResult> RequestRebootAsync(
        CancellationToken ct=default)
    {
        try
        {
            await client.CommandAsync("/system/reboot",cancellationToken:ct);
            return new RouterRebootRequestResult(true,false,
                "أرسل طلب إعادة التشغيل. قد ينقطع الاتصال؛ راقب عودة الراوتر.");
        }
        catch(RouterOsTrapException ex)
        {
            return new RouterRebootRequestResult(false,false,
                "رفض الراوتر إعادة التشغيل: "+ex.Message);
        }
        catch(Exception ex) when(ex is IOException or ObjectDisposedException
            or OperationCanceledException)
        {
            return new RouterRebootRequestResult(false,true,
                "انقطع الاتصال أثناء إرسال إعادة التشغيل. النتيجة غير مؤكدة؛ "+
                "لا تكرر الأمر قبل التحقق من حالة الراوتر.");
        }
    }
}
