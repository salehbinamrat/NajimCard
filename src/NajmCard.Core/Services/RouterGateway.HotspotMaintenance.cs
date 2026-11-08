using System.IO;
using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>Single selected record actions with exact-ID identity verification,
/// explicit readback, and no retries after ambiguous RouterOS write outcomes.</summary>
public sealed partial class RouterGateway
{
    private static void CheckHotspotInternalId(string id)
    {
        if(string.IsNullOrEmpty(id)||id.Length is <2 or >40||id[0]!='*'
            || id.Skip(1).Any(c=>!char.IsAsciiHexDigit(c)))
            throw new ArgumentException("معرف Hotspot المحدد غير صالح.");
    }
    private async Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> HotspotById(
        string path,string id,string fields,CancellationToken ct)
    {
        if(client is not IRouterQueryClient targeted)
            throw new InvalidOperationException("التحقق الآمن يتطلب استعلام RouterOS بمعرف محدد.");
        return await targeted.QueryByIdAsync(path,id,
            new Dictionary<string,string>{[".proplist"]=fields},ct);
    }

    public async Task<RouterHotspotActionResult> ChangeHotspotServerStateAsync(
        RouterHotspotServerStatus selected,bool disabled,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        CheckHotspotInternalId(selected.Id);
        const string path="/ip/hotspot/print";
        const string fields=".id,name,interface,profile,disabled,invalid,running";
        var before=await HotspotById(path,selected.Id,fields,ct);
        if(before.Count!=1 || Get(before[0],".id")!=selected.Id
            || Get(before[0],"name")!=selected.Name
            || Get(before[0],"interface")!=selected.Interface
            || Get(before[0],"profile")!=selected.Profile
            || NetworkFlag(before[0],"disabled") is not bool original
            || selected.Disabled is null || original!=selected.Disabled)
            throw new InvalidOperationException(
                "تغيرت هوية السيرفر أو حالته؛ حدّث القائمة قبل تنفيذ العملية.");
        if(original==disabled)
            return new RouterHotspotActionResult(true,false,"السيرفر بالفعل بالحالة المطلوبة.");
        try
        {
            await client.CommandAsync("/ip/hotspot/set",
                new Dictionary<string,string>
                {
                    [".id"]=selected.Id,["disabled"]=disabled?"yes":"no"
                },ct);
        }
        catch(RouterOsTrapException ex)
        {
            return new RouterHotspotActionResult(false,false,"رفض الراوتر التغيير: "+ex.Message);
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            return new RouterHotspotActionResult(false,true,
                "انقطع الرد أثناء تغيير السيرفر. تحقق من الراوتر ولا تكرر الأمر.");
        }
        try
        {
            var after=await HotspotById(path,selected.Id,fields,ct);
            if(after.Count==1 && Get(after[0],".id")==selected.Id
                && Get(after[0],"name")==selected.Name
                && Get(after[0],"interface")==selected.Interface
                && NetworkFlag(after[0],"disabled")==disabled)
                return new RouterHotspotActionResult(true,false,"تم التغيير وتأكيده من الراوتر.");
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            return new RouterHotspotActionResult(false,true,
                "تم إرسال التعديل لكن تعذرت قراءة التأكيد؛ تحقق يدويا.");
        }
        return new RouterHotspotActionResult(false,true,
            "لم يثبت التعديل عند إعادة القراءة؛ لا تعاود الإرسال دون تحقق.");
    }

    public async Task<RouterHotspotActionResult> DisconnectHotspotSessionAsync(
        HotspotSession selected,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        CheckHotspotInternalId(selected.Id);
        const string path="/ip/hotspot/active/print";
        const string fields=".id,user,address,mac-address,uptime";
        var before=await HotspotById(path,selected.Id,fields,ct);
        if(before.Count!=1 || Get(before[0],".id")!=selected.Id
            || Get(before[0],"user")!=selected.User
            || Get(before[0],"address")!=selected.Address
            || Get(before[0],"mac-address")!=selected.MacAddress)
            throw new InvalidOperationException(
                "تغيرت الجلسة المحددة؛ أعد قراءة الجلسات قبل الفصل.");
        try
        {
            await client.CommandAsync("/ip/hotspot/active/remove",
                new Dictionary<string,string>{[".id"]=selected.Id},ct);
        }
        catch(RouterOsTrapException ex)
        {
            return new RouterHotspotActionResult(false,false,"رفض الراوتر فصل الجلسة: "+ex.Message);
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            return new RouterHotspotActionResult(false,true,
                "تعذر تأكيد الفصل بسبب انقطاع الرد؛ لا تكرر تلقائيا.");
        }
        try
        {
            var after=await HotspotById(path,selected.Id,fields,ct);
            if(after.Count==0)
                return new RouterHotspotActionResult(true,false,"فُصلت الجلسة المحددة وتأكد غيابها.");
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            return new RouterHotspotActionResult(false,true,
                "وصل أمر الفصل لكن تعذر تأكيد غياب الجلسة.");
        }
        return new RouterHotspotActionResult(false,true,
            "لم يتأكد غياب الجلسة؛ راجع الراوتر قبل تكرار الأمر.");
    }
}
