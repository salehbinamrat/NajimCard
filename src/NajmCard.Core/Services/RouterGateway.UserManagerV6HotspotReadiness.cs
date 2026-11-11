using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// Read-only RouterOS 6 preflight for a USER MANAGER voucher used by a local
/// Hotspot. Hadramout's user/add + create-and-activate-profile sequence remains
/// unchanged. A real account cannot authenticate unless the NAS Hotspot uses
/// RADIUS and the User Manager recognizes the NAS router.
/// This makes misconfiguration a *pre-send* error, not a printed dead card.
/// </summary>
public sealed partial class RouterGateway
{
    private static bool IsYes(string value) =>
        value is "yes" or "true" or "1";
    private static bool IsNo(string value) =>
        value is "no" or "false" or "0";

    /// <summary>
    /// Checks only RouterOS read menus; never creates users or changes RADIUS,
    /// Hotspot profiles, routers, firewall, or server secrets.
    /// A successful result verifies local configuration, not a live subscriber
    /// login or the shared secret. User Manager on a separate NAS needs an
    /// explicit live NAS-side check before vouchers can be guaranteed.
    /// </summary>
    public async Task VerifyUserManagerV6HotspotReadinessAsync(
        string profile,string customer,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(profile) || string.IsNullOrWhiteSpace(customer))
            throw new ArgumentException("اختر باقة وحساب مدير User Manager v6.");

        // Reading a single selected package by owner before sending avoids
        // assuming that a customer can activate a different owner's package.
        var packageRows=await client.CommandAsync("/tool/user-manager/profile/print",
            new Dictionary<string,string>
            {
                [".proplist"]="name,owner,customer"
            },ct);
        var matches=packageRows.Where(x=>string.Equals(Get(x,"name"),
            profile,StringComparison.Ordinal)).ToArray();
        if(matches.Length==0)
            throw new InvalidOperationException(
                "الباقة المختارة غير موجودة في User Manager v6؛ حدّث الباقات قبل الإصدار.");
        if(matches.Any(x=>
            (Get(x,"owner") is { Length:>0 } owner && 
                !string.Equals(owner,customer,StringComparison.OrdinalIgnoreCase)) ||
            (Get(x,"owner").Length==0 && Get(x,"customer") is { Length:>0 } cust &&
                !string.Equals(cust,customer,StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException(
                "حساب المدير لا يطابق مالك الباقة في User Manager v6؛ "+
                "اختر حساب المدير الصحيح قبل إنشاء أي كرت.");

        var servers=await client.CommandAsync("/ip/hotspot/print",
            new Dictionary<string,string>
            {
                [".proplist"]="name,profile,disabled"
            },ct);
        var running=servers.Where(row=>!IsYes(Get(row,"disabled"))).ToArray();
        if(running.Length==0)
            throw new InvalidOperationException(
                "لم يُعثر على Hotspot مفعّل على الراوتر المتصل. "+
                "لا يمكن تأكيد اتصال الكروت بالإنترنت؛ "+
                "إذا كان Hotspot على راوتر آخر، افحص إعدادات RADIUS عليه أولا.");

        var definitions=await client.CommandAsync("/ip/hotspot/profile/print",
            new Dictionary<string,string>
            {
                [".proplist"]="name,use-radius,radius-accounting"
            },ct);
        var profileMap=definitions.Where(row=>!string.IsNullOrWhiteSpace(Get(row,"name")))
            .ToDictionary(row=>Get(row,"name"),StringComparer.OrdinalIgnoreCase);
        foreach(var server in running)
        {
            var serverProfile=Get(server,"profile");
            if(serverProfile.Length==0 || !profileMap.TryGetValue(serverProfile,out var cfg))
                throw new InvalidOperationException(
                    "تعذر قراءة ملف تعريف Hotspot للسيرفر النشط؛ "+
                    "لم تُنشأ كروت قد لا تعمل.");
            var radiusEnabled=Get(cfg,"use-radius").ToLowerInvariant();
            if(!IsYes(radiusEnabled))
                throw new InvalidOperationException(
                    "ملف Hotspot «"+serverProfile+
                    "» لا يستخدم RADIUS (use-radius=no). "+
                    "الكروت المنشأة في User Manager لن تُقبل عند تسجيل الدخول.");
        }

        var radiusRows=await client.CommandAsync("/radius/print",
            new Dictionary<string,string>
            {
                [".proplist"]="service,address,disabled,authentication-port"
            },ct);
        if(!radiusRows.Any(row=>
            !IsYes(Get(row,"disabled").ToLowerInvariant()) &&
            Get(row,"service").Split(',',StringSplitOptions.TrimEntries |
                StringSplitOptions.RemoveEmptyEntries)
                .Any(x=>string.Equals(x,"hotspot",StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(Get(row,"address"))))
            throw new InvalidOperationException(
                "لا يوجد عميل RADIUS مفعّل لخدمة hotspot في /radius. "+
                "أنشئ وصحح اتصال RADIUS بمدير المستخدمين قبل إصدار الكروت.");

        var registered=await client.CommandAsync("/tool/user-manager/router/print",
            new Dictionary<string,string> 
            {
                [".proplist"]="name,ip-address,disabled"
            },ct);
        if(!registered.Any(row=>!IsYes(Get(row,"disabled").ToLowerInvariant())))
            throw new InvalidOperationException(
                "لا يوجد راوتر NAS مسجل ومفعّل في /tool/user-manager/router. "+
                "يجب أن يتطابق عنوان الراوتر والسر المشترك بين RADIUS وUser Manager.");

        // We cannot safely verify shared-secret equality or Internet routing
        // by reading configuration; never claim a real client login succeeded.
    }
}
