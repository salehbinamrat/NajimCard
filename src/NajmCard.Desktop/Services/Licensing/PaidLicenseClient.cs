using System.IO;
using System.Net.Http;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace NajmCard.Desktop.Services.Licensing;

/// <summary>Verifies the ACTUAL production Hadramout license feed and signature;
/// same product ID, public key and device hash; nothing in the service checks
/// the trial state. Public key is pinned, not downloaded as a trust root.</summary>
public static class PaidLicenseFeedVerifier
{
    private const string Product="NIDI-MIKROTIK-CARDS";
    private const int MaxFeedBytes=1024*1024;
    private static readonly byte[] PublicKey=Convert.FromBase64String(
        "yifdpnKvdmxoEGUOoQGqeytEaume9Ldp7sKPbpQuGBE=");
    private static readonly JsonSerializerOptions CanonicalOptions=new()
    {
        Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented=false
    };

    public static string NormalizeCode(string? code)
    {
        var normalized=new string((code??"")
            .Where(x=>!char.IsWhiteSpace(x)&&x!='-').ToArray()).ToUpperInvariant();
        return Regex.IsMatch(normalized,@"^[A-F0-9]{32}$",
            RegexOptions.CultureInvariant)?normalized:"";
    }
    public static LicenseCheck Verify(byte[] raw,string code,string device,
        DateTimeOffset nowUtc,byte[]? publicKeyOverride=null)
    {
        try
        {
            var normalized=NormalizeCode(code);
            if(normalized.Length!=32 || !Regex.IsMatch(device,@"^[A-F0-9]{64}$"))
                return Denied("input_invalid","رمز الترخيص أو بصمة Windows غير صالحة.");
            if(raw.Length is <1 or >MaxFeedBytes)
                return Denied("feed_invalid","حجم ملف التراخيص غير موثوق.");
            using var doc=JsonDocument.Parse(raw,new JsonDocumentOptions
            {
                MaxDepth=24,AllowTrailingCommas=false,CommentHandling=JsonCommentHandling.Disallow
            });
            var root=doc.RootElement;
            if(root.ValueKind!=JsonValueKind.Object || !UniqueFields(root))
                return Denied("feed_invalid","بيانات ملف التراخيص غير صحيحة.");
            if(!root.TryGetProperty("signature",out var sig) ||
                sig.ValueKind!=JsonValueKind.String)
                return Denied("feed_invalid","توقيع التراخيص مفقود.");
            var signature=Convert.FromBase64String(sig.GetString()!);
            var key=publicKeyOverride??PublicKey;
            if(key.Length!=32||signature.Length!=64)return Denied(
                "feed_invalid","توقيع غير صالح.");
            byte[] canonical=Encoding.UTF8.GetBytes(Canonical(root,excludeSignature:true));
            var verifier=new Ed25519Signer();
            verifier.Init(false,new Ed25519PublicKeyParameters(key,0));
            verifier.BlockUpdate(canonical,0,canonical.Length);
            if(!verifier.VerifySignature(signature))
                return Denied("feed_invalid","تعذر التحقق من توقيع التراخيص.");
            if(!root.TryGetProperty("schema",out var schema)||schema.GetInt32()!=1 ||
                !root.TryGetProperty("product",out var product)||product.GetString()!=Product ||
                !root.TryGetProperty("revision",out var rev)||rev.GetInt64()<1 ||
                !root.TryGetProperty("generated_at",out var generated) ||
                !DateTimeOffset.TryParse(generated.GetString(),CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,out var issued) ||
                issued>nowUtc.AddMinutes(5) ||
                !root.TryGetProperty("licenses",out var licenses) ||
                licenses.ValueKind!=JsonValueKind.Object)
                return Denied("feed_invalid","ملف الترخيص لا يطابق منصة حضرموت.");
            string index=Convert.ToHexString(SHA256.HashData(
                Encoding.ASCII.GetBytes(normalized)));
            if(!licenses.TryGetProperty(index,out var entry) ||
                entry.ValueKind!=JsonValueKind.Object)
                return Denied("not_found","رمز الترخيص لم يظهر في السجل المنشور بعد.");
            var status=entry.GetProperty("status").GetString();
            string? bound=entry.TryGetProperty("device",out var d) &&
                d.ValueKind==JsonValueKind.String?d.GetString():null;
            var expires=entry.TryGetProperty("expires",out var exp) &&
                exp.ValueKind==JsonValueKind.String?exp.GetString():null;
            if(expires is not null)
            {
                if(!DateOnly.TryParseExact(expires,"yyyy-MM-dd",
                    CultureInfo.InvariantCulture,DateTimeStyles.None,out var day))
                    return Denied("feed_invalid","تاريخ انتهاء الترخيص غير صالح.");
                if(DateOnly.FromDateTime(nowUtc.UtcDateTime)>day)
                    return Denied("expired",
                        "انتهت صلاحية الترخيص المدفوع. يرجى تجديده من مركز التراخيص.");
            }
            if(status=="pending")
                return Denied("pending_activation",
                    "الرمز صالح للإصدار وينتظر ربطه بأول جهاز.");
            if(!string.Equals(bound,device,StringComparison.Ordinal))
                return Denied("wrong_device",
                    "هذا الرمز مرتبط بجهاز Windows آخر. اطلب نقله من جهة التراخيص.");
            if(status!="active")
                return Denied("revoked",
                    "الترخيص موقوف أو ملغى. راجع مركز التراخيص.");
            DateTimeOffset? expiry=expires is null?null:new DateTimeOffset(
                DateTime.ParseExact(expires,"yyyy-MM-dd",CultureInfo.InvariantCulture),
                TimeSpan.Zero).AddDays(1);
            return new LicenseCheck("active",expires is null?
                "تم تفعيل الترخيص بنجاح — ترخيص دائم لنجم كارد وحضرموت على هذا الجهاز.":
                "ترخيصك فعال حتى "+expires+
                " — يمكنك استخدام نجم كارد وحضرموت على الجهاز المرتبط.",-1,expiry);
        }
        catch(Exception ex) when(ex is JsonException or FormatException
            or ArgumentException or InvalidOperationException or KeyNotFoundException
            or OverflowException or CryptographicException)
        {
            return Denied("feed_invalid",
                "فشل التحقق من توقيع أو بنية ملف التراخيص. لم يُسمح بالتفعيل.");
        }
    }
    private static LicenseCheck Denied(string mode,string message)
        => new(mode,message);
    private static bool UniqueFields(JsonElement element)
    {
        if(element.ValueKind==JsonValueKind.Object)
        {
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var property in element.EnumerateObject())
                if(!names.Add(property.Name)||!UniqueFields(property.Value))return false;
        }
        else if(element.ValueKind==JsonValueKind.Array)
        {
            foreach(var item in element.EnumerateArray())
                if(!UniqueFields(item))return false;
        }
        return true;
    }
    private static string Canonical(JsonElement element,bool excludeSignature=false)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object=>"{"+string.Join(",",
                element.EnumerateObject()
                    .Where(p=>!excludeSignature||p.Name!="signature")
                    .OrderBy(p=>p.Name,StringComparer.Ordinal)
                    .Select(p=>JsonSerializer.Serialize(p.Name,CanonicalOptions)+":"+
                        Canonical(p.Value)))+"}",
            JsonValueKind.Array=>"["+string.Join(",",
                element.EnumerateArray().Select(x=>Canonical(x)))+"]",
            JsonValueKind.String=>JsonSerializer.Serialize(element.GetString(),CanonicalOptions),
            JsonValueKind.Number=>element.GetRawText(),
            JsonValueKind.True=>"true",
            JsonValueKind.False=>"false",
            JsonValueKind.Null=>"null",
            _=>throw new FormatException("Unknown JSON value")
        };
    }
}

/// <summary>Paid-only network path; the local trial never instantiates or calls
/// this service. No service-role token or user entitlement is stored in the EXE.</summary>
public sealed class PaidLicenseClient(HttpClient http,string deviceId,byte[]? trustedKey=null,
    ISignedPaidLicenseCache? cache=null)
{
    public const string BaseUrl=
        "https://zlirqppwemfmzxkholcr.supabase.co/functions/v1/hadramout-license";
    private readonly HttpClient _http=http;
    private readonly string _device=deviceId;
    private readonly byte[]? _testKey=trustedKey;

    private async Task<byte[]> FetchFeedAsync(CancellationToken ct)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromSeconds(8));
        using var req=new HttpRequestMessage(HttpMethod.Get,BaseUrl+"/licenses.json");
        req.Headers.CacheControl=new CacheControlHeaderValue{NoCache=true,NoStore=true};
        using var resp=await _http.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,
            linked.Token);
        resp.EnsureSuccessStatusCode();
        await using var stream=await resp.Content.ReadAsStreamAsync(linked.Token);
        using var ms=new MemoryStream();
        byte[] block=new byte[8192];
        int read;
        while((read=await stream.ReadAsync(block,linked.Token))!=0)
        {
            if(ms.Length+read>1024*1024)
                throw new InvalidDataException("Oversized licensing feed.");
            ms.Write(block,0,read);
        }
        return ms.ToArray();
    }
    public async Task<LicenseCheck> VerifyAsync(string code,CancellationToken ct=default)
    {
        var normalized=PaidLicenseFeedVerifier.NormalizeCode(code);
        if(normalized.Length!=32)return new LicenseCheck("input_invalid",
            "رمز الترخيص يجب أن يكون 32 خانة من الأرقام والحروف الإنجليزية A-F.");
        try
        {
            var feed=await FetchFeedAsync(ct);
            var result=PaidLicenseFeedVerifier.Verify(feed,normalized,_device,
                DateTimeOffset.UtcNow,_testKey);
            // Cache signed results, including a signed revocation; never revive an older grant.
            if(result.Mode is not ("feed_invalid" or "input_invalid"))
            {
                try {cache?.Write(feed);}
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or CryptographicException)
                {return new LicenseCheck("storage_error","تعذر حفظ إثبات الترخيص؛ تحقق من صلاحيات Windows.");}
            }
            return result;
        }
        catch(Exception ex) when(ex is HttpRequestException or IOException
            or TaskCanceledException or InvalidDataException)
        {
            // Only transport failure permits offline use. Every cached signature,
            // device binding and expiry is validated again at the current time.
            if(ex is not InvalidDataException && cache?.Read() is byte[] saved)
                return PaidLicenseFeedVerifier.Verify(saved,normalized,_device,
                    DateTimeOffset.UtcNow,_testKey);
            return new LicenseCheck("network_error",
                "تعذر الاتصال بمنصة الترخيص. الرمز محفوظ؛ أعد المحاولة عند توفر الإنترنت.");
        }
    }
    public async Task<LicenseCheck> ActivateAsync(string code,CancellationToken ct=default)
    {
        string normalized=PaidLicenseFeedVerifier.NormalizeCode(code);
        var current=await VerifyAsync(normalized,ct);
        if(current.Mode!="pending_activation")return current;
        try
        {
            // Only paid activation: exactly the code and Hadramout-compatible
            // hashed Windows device code are submitted via HTTPS.
            string json=JsonSerializer.Serialize(new
            {
                license_id=normalized,device_code=_device
            });
            using var req=new HttpRequestMessage(HttpMethod.Post,BaseUrl+"/v1/claim")
            {
                Content=new StringContent(json,Encoding.UTF8,"application/json")
            };
            using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(8));
            using var res=await _http.SendAsync(req,linked.Token);
            if(!res.IsSuccessStatusCode)
                return new LicenseCheck("claim_unavailable",
                    "لم تقبل منصة الترخيص الربط. راجع حالة الرمز أو ارتباطه بجهاز آخر.");
            var confirmed=await VerifyAsync(normalized,ct);
            return confirmed.Mode=="active"?confirmed:new LicenseCheck("claim_pending",
                "وصل طلب ربط الجهاز، لكن لم تؤكد القائمة الموقعة التفعيل بعد. أعد التحقق دون إعادة إنشاء الرمز.");
        }
        catch(Exception ex) when(ex is HttpRequestException or IOException
            or TaskCanceledException)
        {
            return new LicenseCheck("claim_pending",
                "قد يكون طلب الربط وصل للمنصة. النتيجة غير مؤكدة؛ استخدم تحقق قبل إعادة التفعيل.");
        }
    }
}

public sealed class ProtectedPaidCodeStore(string? directory=null)
{
    private static readonly byte[] Entropy=Encoding.UTF8.GetBytes("NajmCard-Paid-Code-V1");
    private readonly string _path=Path.Combine(directory??
        Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
            "NajmCard","Licensing"),"paid-code-v1.bin");
    public string Load()
    {
        try
        {
            if(!File.Exists(_path))return "";
            byte[] encrypted=File.ReadAllBytes(_path);
            if(encrypted.Length>4096)return "";
            var content=ProtectedData.Unprotect(encrypted,Entropy,
                DataProtectionScope.CurrentUser);
            return PaidLicenseFeedVerifier.NormalizeCode(Encoding.ASCII.GetString(content));
        }
        catch(Exception ex) when(ex is IOException or CryptographicException
            or UnauthorizedAccessException){return "";}
    }
    public void Save(string code)
    {
        var normalized=PaidLicenseFeedVerifier.NormalizeCode(code);
        if(normalized.Length!=32)throw new ArgumentException("رمز ترخيص غير صالح.");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var protectedCode=ProtectedData.Protect(Encoding.ASCII.GetBytes(normalized),
            Entropy,DataProtectionScope.CurrentUser);
        var temp=_path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllBytes(temp,protectedCode);File.Move(temp,_path,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}

public interface ISignedPaidLicenseCache
{
    byte[]? Read();
    void Write(byte[] signedFeed);
}

/// <summary>Device/user-protected copy of the signed grant, never a cached boolean.</summary>
public sealed class SignedPaidLicenseCache(string? directory=null):ISignedPaidLicenseCache
{
    private static readonly byte[] Entropy=Encoding.UTF8.GetBytes("NajmCard-Signed-Paid-Feed-V1");
    private readonly string _path=Path.Combine(directory??Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NajmCard","Licensing"),"paid-feed-v1.bin");
    public byte[]? Read()
    {
        try
        {
            if(!File.Exists(_path)||new FileInfo(_path).Length>2*1024*1024)return null;
            return ProtectedData.Unprotect(File.ReadAllBytes(_path),Entropy,DataProtectionScope.CurrentUser);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or CryptographicException)
        {return null;}
    }
    public void Write(byte[] signedFeed)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp=_path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            File.WriteAllBytes(temp,ProtectedData.Protect(signedFeed,Entropy,DataProtectionScope.CurrentUser));
            File.Move(temp,_path,true);
        }
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
}
