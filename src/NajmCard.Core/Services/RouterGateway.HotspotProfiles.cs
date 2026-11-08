using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>RouterOS 6 Hotspot user profile CRUD. All writes require a fresh,
/// exact identity check, guarded fields and explicit live verification. Never
/// replay a write after a lost reply. User Manager v6 is a separate contract.</summary>
public sealed partial class RouterGateway
{
    private const string HotspotProfilePath="/ip/hotspot/user/profile";
    private const string HotspotProfileFields=".id,name,rate-limit,session-timeout,shared-users";
    private static bool ProfileIdValid(string? id)
        => id is {Length:>=2 and <=40} && id[0]=='*'
            && id.Skip(1).All(char.IsAsciiHexDigit);
    private static string ValidateDraftName(string name)
    {
        name=(name??"").Trim();
        if(!Regex.IsMatch(name,@"^[\p{L}\p{N}_ .-]{1,64}$",
            RegexOptions.CultureInvariant) || name.Contains("..",StringComparison.Ordinal))
            throw new ArgumentException("اسم الباقة يجب أن يكون 1-64 حرفا أو رقما، دون أوامر.",nameof(name));
        return name;
    }
    private static HotspotProfileDraft ValidateDraft(HotspotProfileDraft input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var name=ValidateDraftName(input.Name);
        var rate=(input.RateLimit??"").Trim();
        if(rate.Length>0 && !Regex.IsMatch(rate,
            @"^[1-9][0-9]{0,8}[kKmMgG]?/[1-9][0-9]{0,8}[kKmMgG]?$",
            RegexOptions.CultureInvariant))
            throw new ArgumentException("حد السرعة غير صالح؛ مثال 2M/512k.",nameof(input));
        var timeout=(input.SessionTimeout??"").Trim();
        if(!Regex.IsMatch(timeout,@"^(0s|(?:[0-9]{1,5}[wdhms])+)$",
            RegexOptions.CultureInvariant|RegexOptions.IgnoreCase))
            throw new ArgumentException("مدة الجلسة غير صالحة؛ مثال 1h30m أو 0s.",nameof(input));
        var shared=(input.SharedUsers??"").Trim();
        if(!int.TryParse(shared,NumberStyles.None,CultureInfo.InvariantCulture,out int users)
            || users is <1 or >100000)
            throw new ArgumentException("عدد المستخدمين المشتركين يجب أن يكون بين 1 و100000.",nameof(input));
        return new HotspotProfileDraft(name,rate,timeout,users.ToString(CultureInfo.InvariantCulture));
    }
    private static IReadOnlyDictionary<string,string> ProfileArgs(HotspotProfileDraft input)
        =>new Dictionary<string,string>
        {
            ["name"]=input.Name,
            ["rate-limit"]=input.RateLimit,
            ["session-timeout"]=input.SessionTimeout,
            ["shared-users"]=input.SharedUsers
        };

    private async Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> ReadHotspotProfileByIdAsync(
        string id,CancellationToken ct)
    {
        if(client is not IRouterQueryClient q)
            throw new NotSupportedException("التحقق يتطلب RouterOS API بمعرف باقة واحد.");
        return await q.QueryByIdAsync(HotspotProfilePath+"/print",id,
            new Dictionary<string,string>{{".proplist",HotspotProfileFields}},ct);
    }
    private static bool MatchesProfile(
        IReadOnlyDictionary<string,string> row,RouterCardProfile selected)
        => Get(row,".id")==selected.Id
            && row.ContainsKey("name")
            && Get(row,"name")==selected.Name
            && row.ContainsKey("rate-limit")
            && Get(row,"rate-limit")==selected.RateLimit
            && row.ContainsKey("session-timeout")
            && Get(row,"session-timeout")==selected.SessionTimeout
            && row.ContainsKey("shared-users")
            && Get(row,"shared-users")==selected.SharedUsers;

    public async Task<HotspotProfileMutationResult> SaveHotspotProfileAsync(
        HotspotProfileDraft draft,RouterCardProfile? selected=null,CancellationToken ct=default)
    {
        var clean=ValidateDraft(draft); // Reject malformed drafts before any network I/O.
        if(client is not IRouterQueryClient)
            throw new NotSupportedException("مطلوب دعم قراءة الباقة بمعرفها قبل الحفظ.");
        if(selected is not null)
        {
            if(!ProfileIdValid(selected.Id))throw new ArgumentException("معرف الباقة غير صالح.");
            var before=await ReadHotspotProfileByIdAsync(selected.Id,ct);
            if(before.Count!=1 || !MatchesProfile(before[0],selected))
                throw new InvalidOperationException("تغيرت الباقة منذ تحميلها؛ تحديث الباقات مطلوب قبل الحفظ.");
            if(clean.Name==selected.Name && clean.RateLimit==selected.RateLimit
                && clean.SessionTimeout==selected.SessionTimeout && clean.SharedUsers==selected.SharedUsers)
                return new HotspotProfileMutationResult(true,false,"لم تتغير بيانات الباقة.",selected.Id);
            var attrs=new Dictionary<string,string>(ProfileArgs(clean)){{".id",selected.Id}};
            try
            {
                await client.CommandAsync(HotspotProfilePath+"/set",attrs,ct);
            }
            catch(RouterOsTrapException ex)
            {
                return new HotspotProfileMutationResult(false,false,"رفض الراوتر تعديل الباقة: "+ex.Message);
            }
            catch(Exception ex) when(ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
                return new HotspotProfileMutationResult(false,true,"رد تعديل الباقة غير مؤكد؛ لا تكرر الحفظ.");
            }
            return await VerifyProfileWriteAsync(selected.Id,clean,ct);
        }

        if(client is not IRouterReceiptClient receipts)
            throw new NotSupportedException("إضافة الباقات تتطلب استلام المعرف الحقيقي من RouterOS.");
        var existing=await client.CommandAsync(HotspotProfilePath+"/print",
            new Dictionary<string,string>{{".proplist",".id,name"}},ct);
        if(existing.Count>10000 || existing.Any(row=>!row.ContainsKey("name")))
            throw new InvalidDataException("قائمة الباقات غير مكتملة؛ أُوقفت الإضافة.");
        if(existing.Any(x=>Get(x,"name").Equals(clean.Name,StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("توجد باقة بالاسم نفسه على الراوتر.");
        RouterCommandReply receipt;
        try
        {
            receipt=await receipts.CommandWithReceiptAsync(
                HotspotProfilePath+"/add",ProfileArgs(clean),ct);
        }
        catch(RouterOsTrapException ex)
        {
            return new HotspotProfileMutationResult(false,false,"رفض الراوتر إضافة الباقة: "+ex.Message);
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            return new HotspotProfileMutationResult(false,true,"نتيجة إنشاء الباقة غير مؤكدة؛ افحص الراوتر قبل الإعادة.");
        }
        if(!receipt.Done.TryGetValue("ret",out var id) || !ProfileIdValid(id))
            return new HotspotProfileMutationResult(false,true,
                "الراوتر لم يؤكد المعرف الجديد؛ لا تعاود الإضافة تلقائيا.");
        return await VerifyProfileWriteAsync(id,clean,ct);
    }

    private async Task<HotspotProfileMutationResult> VerifyProfileWriteAsync(
        string id,HotspotProfileDraft requested,CancellationToken ct)
    {
        try
        {
            var after=await ReadHotspotProfileByIdAsync(id,ct);
            if(after.Count==1 && Get(after[0],".id")==id
                && Get(after[0],"name")==requested.Name
                && Get(after[0],"rate-limit")==requested.RateLimit
                && Get(after[0],"session-timeout")==requested.SessionTimeout
                && Get(after[0],"shared-users")==requested.SharedUsers)
                return new HotspotProfileMutationResult(true,false,
                    "تم حفظ باقة Hotspot والتحقق من البيانات على الراوتر.",id);
            return new HotspotProfileMutationResult(false,true,
                "لم تتطابق قراءة التحقق مع الحفظ؛ لا تكرر العملية.",id);
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or ObjectDisposedException
            or RouterOsTrapException or InvalidDataException)
        {
            return new HotspotProfileMutationResult(false,true,
                "تعذر التحقق من الحفظ بعد إرساله؛ راجع الراوتر قبل إعادة الأمر.",id);
        }
    }

    public async Task<HotspotProfileMutationResult> DeleteHotspotProfileAsync(
        RouterCardProfile selected,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if(!ProfileIdValid(selected.Id))throw new ArgumentException("معرف الباقة غير صالح.");
        var before=await ReadHotspotProfileByIdAsync(selected.Id,ct);
        if(before.Count!=1 || !MatchesProfile(before[0],selected))
            throw new InvalidOperationException("تغيرت هوية أو خصائص الباقة؛ حدث البيانات قبل الحذف.");
        try
        {
            await client.CommandAsync(HotspotProfilePath+"/remove",
                new Dictionary<string,string>{{".id",selected.Id}},ct);
        }
        catch(RouterOsTrapException ex)
        {
            return new HotspotProfileMutationResult(false,false,
                "رفض الراوتر حذف الباقة (قد تكون مرتبطة بمستخدمين): "+ex.Message);
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            return new HotspotProfileMutationResult(false,true,
                "نتيجة الحذف غير مؤكدة؛ لا تعاود العملية تلقائيا.");
        }
        try
        {
            var after=await ReadHotspotProfileByIdAsync(selected.Id,ct);
            return after.Count==0
                ?new HotspotProfileMutationResult(true,false,"تأكد حذف الباقة من الراوتر.",selected.Id)
                :new HotspotProfileMutationResult(false,true,"لم يتأكد حذف الباقة من الراوتر.");
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or ObjectDisposedException
            or RouterOsTrapException or InvalidDataException)
        {
            return new HotspotProfileMutationResult(false,true,
                "وصل أمر الحذف لكن فشلت قراءة التحقق؛ تحقق قبل إعادة الإجراء.");
        }
    }
}
