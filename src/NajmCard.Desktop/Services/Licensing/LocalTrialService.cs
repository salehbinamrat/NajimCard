using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace NajmCard.Desktop.Services.Licensing;

public sealed record LicenseCheck(string Mode,string Message,int RemainingSeconds=-1,
    DateTimeOffset? ExpiresAtUtc=null)
{
    public bool Permitted=>Mode is "active" or "trial";
}
public sealed record TrialState(DateTimeOffset StartedAtUtc,DateTimeOffset LastSeenUtc,
    string MachineHash);

public interface ITrialStateStore
{
    TrialState? Read();
    void Write(TrialState state);
}

/// <summary>Twenty-day offline trial. This class does no network I/O and
/// cannot ever submit a machine fingerprint to a licensing provider.
/// Trial starts ONLY after a real successful RouterOS API login.
/// A client-side trial cannot resist an administrator deleting all local state.</summary>
public sealed class LocalTrialService(
    ITrialStateStore storage,string fingerprint,Func<DateTimeOffset>? clock=null)
{
    private static readonly TimeSpan Duration=TimeSpan.FromDays(20);
    private static readonly TimeSpan RollbackGrace=TimeSpan.FromMinutes(5);
    private readonly Func<DateTimeOffset> _now=clock??(()=>DateTimeOffset.UtcNow);
    private readonly ITrialStateStore _storage=storage;
    private readonly string _fingerprint=fingerprint;

    public LicenseCheck Check()
    {
        try
        {
            var state=_storage.Read();
            if(state is null)
                return new LicenseCheck("ready",
                    "تجربة مجانية لمدة 20 يوما (480 ساعة). تبدأ فقط بعد أول اتصال ناجح بالراوتر.");
            return Evaluate(state,_now().ToUniversalTime(),update:true);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException
            or CryptographicException or JsonException or InvalidDataException or FormatException)
        {
            return new LicenseCheck("locked",
                "تعذر قراءة سجل التجربة المحمي. تحقق من صلاحيات Windows، دون حذف السجل.");
        }
    }

    public LicenseCheck BeginAfterSuccessfulRouterLogin()
    {
        try
        {
            var now=_now().ToUniversalTime();
            var prior=_storage.Read();
            if(prior is not null) return Evaluate(prior,now,update:true);
            var start=new TrialState(now,now,_fingerprint);
            _storage.Write(start);
            return Evaluate(start,now,update:false);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException
            or CryptographicException or JsonException or InvalidDataException or FormatException)
        {
            return new LicenseCheck("locked",
                "فشل تسجيل بداية التجربة محليا؛ لم يُسمح بالدخول دون حفظ محمي.");
        }
    }

    private LicenseCheck Evaluate(TrialState state,DateTimeOffset now,bool update)
    {
        if(string.IsNullOrEmpty(_fingerprint) ||
            !string.Equals(state.MachineHash,_fingerprint,StringComparison.Ordinal) ||
            state.StartedAtUtc>state.LastSeenUtc ||
            state.StartedAtUtc>now.Add(RollbackGrace))
            return new LicenseCheck("locked",
                "هذا السجل لا يطابق Windows الحالي أو بياناته غير سليمة.");
        if(now<state.LastSeenUtc.Subtract(RollbackGrace))
            return new LicenseCheck("clock_error",
                "تاريخ أو ساعة Windows رجعا للخلف. صحح الوقت، ولن تتغير بداية التجربة.");
        var expires=state.StartedAtUtc.Add(Duration);
        if(now>=expires)
        {
            // Preserve the highest observed UTC time after expiry.
            if(update && now>state.LastSeenUtc)
                _storage.Write(state with {LastSeenUtc=now});
            return new LicenseCheck("expired",
                "انتهت التجربة المجانية بعد 20 يوما. لإكمال استخدام نجم كارد، يرجى شراء ترخيص أو إدخال رمز صالح.",0,expires);
        }
        if(update && now>state.LastSeenUtc.AddMinutes(1))
        {
            _storage.Write(state with {LastSeenUtc=now});
        }
        var seconds=(int)Math.Ceiling((expires-now).TotalSeconds);
        return new LicenseCheck("trial",
            "تجربتك المجانية فعالة — المتبقي "+FormatRemaining(seconds)+
            " | بيانات التجربة محلية ولا تُرسل للإنترنت.",seconds,expires);
    }

    public static string FormatRemaining(int seconds)
    {
        if(seconds<=0)return "0 ساعة";
        int days=seconds/86400;
        int hours=(seconds%86400)/3600;
        int minutes=(seconds%3600)/60;
        return days+" يوم و"+hours+" ساعة و"+minutes+" دقيقة";
    }
}

/// <summary>Two local, current-user DPAPI-protected mirrors. No HTTP, no
/// Supabase integration and no shared Hadramout folders or registry keys.</summary>
public sealed class ProtectedLocalTrialStore
    : ITrialStateStore
{
    private const string RegistryPath=@"Software\NajmCard\Licensing";
    private const string ValueName="OfflineTrialV1";
    private static readonly byte[] Entropy=Encoding.UTF8.GetBytes("NajmCard-Offline-Trial-V1");
    private readonly string _file;
    private readonly bool _mirrorRegistry;

    public ProtectedLocalTrialStore(string? directory=null,bool mirrorRegistry=true)
    {
        _mirrorRegistry=mirrorRegistry;
        var folder=directory??Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NajmCard","Licensing");
        _file=Path.Combine(folder,"trial-v1.dat");
    }

    private static TrialState Unprotect(byte[] payload)
    {
        var bytes=ProtectedData.Unprotect(payload,Entropy,DataProtectionScope.CurrentUser);
        var state=JsonSerializer.Deserialize<TrialState>(bytes);
        if(state is null)throw new InvalidDataException("Trial data missing");
        return state;
    }
    public TrialState? Read()
    {
        TrialState? onDisk=File.Exists(_file)?Unprotect(File.ReadAllBytes(_file)):null;
        TrialState? inRegistry=null;
        if(_mirrorRegistry)
        {
            using var key=Registry.CurrentUser.OpenSubKey(RegistryPath);
            var encoded=key?.GetValue(ValueName) as string;
            if(!string.IsNullOrWhiteSpace(encoded))
                inRegistry=Unprotect(Convert.FromBase64String(encoded));
        }
        if(onDisk is null)return inRegistry;
        if(inRegistry is null)return onDisk;
        if(onDisk.MachineHash!=inRegistry.MachineHash)
            throw new InvalidDataException("Conflicting trial fingerprints");
        // Oldest start and newest observed clock win; changing a file cannot
        // extend the period when the other protected copy still exists.
        return new TrialState(
            onDisk.StartedAtUtc<inRegistry.StartedAtUtc
                ?onDisk.StartedAtUtc:inRegistry.StartedAtUtc,
            onDisk.LastSeenUtc>inRegistry.LastSeenUtc
                ?onDisk.LastSeenUtc:inRegistry.LastSeenUtc,
            onDisk.MachineHash);
    }
    public void Write(TrialState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        byte[] blob=ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(state),
            Entropy,DataProtectionScope.CurrentUser);
        string temp=_file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            File.WriteAllBytes(temp,blob);
            File.Move(temp,_file,true);
            if(_mirrorRegistry)
            {
                using var key=Registry.CurrentUser.CreateSubKey(RegistryPath,writable:true);
                if(key is null)throw new IOException("Cannot write trial registry mirror.");
                key.SetValue(ValueName,Convert.ToBase64String(blob),RegistryValueKind.String);
            }
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}

/// <summary>Explicit choice: paid entitlement takes precedence, trial never
/// starts without consent and a successful RouterOS connection.</summary>
public sealed class LicenseGate(LocalTrialService trial)
{
    public LicenseCheck? Paid {get;private set;}
    public bool TrialSelected {get;private set;}
    public void SelectTrial()=>TrialSelected=true;
    public void SetPaid(LicenseCheck paid)=>Paid=paid;
    public LicenseCheck Preview()
        => Paid?.Permitted==true?Paid:
           TrialSelected?trial.Check():
           new LicenseCheck("locked",
               "اختر التجربة المجانية أو أدخل رمز الترخيص أولا.");
    public LicenseCheck OnSuccessfulRouterLogin()
        => Paid?.Permitted==true?Paid:
           TrialSelected?trial.BeginAfterSuccessfulRouterLogin():
           new LicenseCheck("locked","لم يتم اختيار طريقة الترخيص.");
}
