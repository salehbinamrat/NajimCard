using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NajmCard.Desktop.Services.Licensing;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Xunit;

namespace NajmCard.Desktop.Tests;

/// <summary>Trial never calls the Internet and only starts after an explicitly
/// successful MikroTik session. Paid licensing uses the signed Hadramout feed.</summary>
public sealed class SharedLicenseAndTrialTests
{
    private sealed class Store : ITrialStateStore
    {
        public TrialState? State;
        public TrialState? Read()=>State;
        public void Write(TrialState state)=>State=state;
    }
    [Fact]
    public void Trial_Does_Not_Start_At_Launch_Or_Failed_Router_Login()
    {
        var store=new Store();
        var today=new DateTimeOffset(2026,11,8,10,0,0,TimeSpan.Zero);
        var trial=new LocalTrialService(store,"LOCAL-COMPUTER",()=>today);
        Assert.Equal("ready",trial.Check().Mode);
        Assert.Null(store.State);
        today=today.AddHours(8);
        Assert.Equal("ready",trial.Check().Mode);
        Assert.Null(store.State);
        var started=trial.BeginAfterSuccessfulRouterLogin();
        Assert.Equal("trial",started.Mode);
        Assert.Equal(20*24*60*60,started.RemainingSeconds);
        Assert.NotNull(store.State);
    }
    [Fact]
    public void Trial_Is_Exactly_480_Hours_Cannot_Reset_And_Blocks_Clock_Rollback()
    {
        var store=new Store();
        var today=new DateTimeOffset(2026,11,8,10,0,0,TimeSpan.Zero);
        var trial=new LocalTrialService(store,"LOCAL-COMPUTER",()=>today);
        trial.BeginAfterSuccessfulRouterLogin();
        today=today.AddDays(19);
        Assert.Equal("trial",trial.Check().Mode);
        Assert.Equal(86400,trial.Check().RemainingSeconds);
        trial.BeginAfterSuccessfulRouterLogin();
        Assert.Equal(new DateTimeOffset(2026,11,8,10,0,0,TimeSpan.Zero),
            store.State!.StartedAtUtc);
        today=today.AddDays(1);
        Assert.Equal("expired",trial.Check().Mode);
        Assert.Equal("expired",trial.BeginAfterSuccessfulRouterLogin().Mode);
        today=today.AddDays(-2);
        Assert.Equal("clock_error",trial.Check().Mode);
    }
    [Fact]
    public void Expired_Trial_Records_Observed_Date_So_Clock_Rollback_Cannot_Revive_It()
    {
        var store=new Store();
        var t=new DateTimeOffset(2026,11,8,10,0,0,TimeSpan.Zero);
        var trial=new LocalTrialService(store,"LOCAL",()=>t);
        trial.BeginAfterSuccessfulRouterLogin();
        t=t.AddDays(21);
        Assert.Equal("expired",trial.Check().Mode);
        Assert.Equal(t,store.State!.LastSeenUtc);
        t=t.AddDays(-20);
        Assert.Equal("clock_error",trial.Check().Mode);
    }
    [Fact]
    public void Windows_DPAPI_Trial_State_Persists_Across_Process_Restarts()
    {
        string directory=Path.Combine(Path.GetTempPath(),"najm-trial-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var now=new DateTimeOffset(2026,11,8,10,0,0,TimeSpan.Zero);
            var first=new LocalTrialService(new ProtectedLocalTrialStore(directory,false),
                "MACHINE-X",()=>now);
            Assert.Equal("trial",first.BeginAfterSuccessfulRouterLogin().Mode);
            now=now.AddDays(8);
            var reopened=new LocalTrialService(new ProtectedLocalTrialStore(directory,false),
                "MACHINE-X",()=>now);
            Assert.Equal("trial",reopened.Check().Mode);
            Assert.Equal(12*86400,reopened.Check().RemainingSeconds);
        }
        finally{Directory.Delete(directory,true);}
    }
    [Fact]
    public void Trial_Fingerprint_Must_Match_And_Cannot_Be_Substituted()
    {
        var store=new Store();
        var now=new DateTimeOffset(2026,11,8,10,0,0,TimeSpan.Zero);
        new LocalTrialService(store,"COMPUTER-A",()=>now).BeginAfterSuccessfulRouterLogin();
        Assert.Equal("locked",new LocalTrialService(store,"COMPUTER-B",()=>now).Check().Mode);
    }

    private static readonly byte[] Seed=Enumerable.Range(0,32).Select(x=>(byte)(x+1)).ToArray();
    private static readonly Ed25519PrivateKeyParameters Secret=new(Seed,0);
    private static byte[] SignedFeed(string code,string device,string status="active",
        string? expires=null,DateTimeOffset? issuedAtUtc=null)
    {
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(code)));
        var issued=(issuedAtUtc??DateTimeOffset.UtcNow.AddMinutes(-2))
            .ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ",
                System.Globalization.CultureInfo.InvariantCulture);
        var doc=new SortedDictionary<string,object?>
        {
            ["schema"]=1,["product"]="NIDI-MIKROTIK-CARDS",["revision"]=2,
            ["generated_at"]=issued,
            ["licenses"]=new SortedDictionary<string,object?>
            {
                [hash]=new SortedDictionary<string,object?>
                {
                    ["status"]=status,["device"]=device,["expires"]=expires
                }
            }
        };
        // ASCII deterministic canonical JSON matching Hadramout's Python
        // json.dumps(sort_keys=True,separators=(',',':'),ensure_ascii=False).
        var data=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(doc));
        var signer=new Ed25519Signer();
        signer.Init(true,Secret);
        signer.BlockUpdate(data,0,data.Length);
        var signed=Convert.ToBase64String(signer.GenerateSignature());
        var final=JsonSerializer.Serialize(new {
            schema=1,product="NIDI-MIKROTIK-CARDS",revision=2,
            generated_at=issued,
            licenses=doc["licenses"],signature=signed
        });
        return Encoding.UTF8.GetBytes(final);
    }
    [Theory]
    [InlineData("active","AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","active")]
    [InlineData("pending","AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","pending_activation")]
    [InlineData("revoked","AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","revoked")]
    [InlineData("active","BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB","wrong_device")]
    public void Signed_Hadramout_Feed_Enforces_State_And_Machine_Binding(
        string state,string registeredDevice,string expected)
    {
        const string code="0123456789ABCDEF0123456789ABCDEF";
        const string currentDevice="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        // Both sides of this signature contract use the same deterministic
        // UTC fixture. A live clock here made the generated-at date appear
        // to be in the future relative to Verify's fixed 2026-11-08 clock.
        var now=new DateTimeOffset(2026,11,8,12,0,0,TimeSpan.Zero);
        var bytes=SignedFeed(code,registeredDevice,state,
            issuedAtUtc:now.AddMinutes(-2));
        var result=PaidLicenseFeedVerifier.Verify(bytes,code,currentDevice,
            now,Secret.GeneratePublicKey().GetEncoded());
        Assert.Equal(expected,result.Mode);
    }
    [Fact]
    public void Future_Dated_Signed_Feed_Remains_Denied()
    {
        const string code="0123456789ABCDEF0123456789ABCDEF";
        const string device="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var now=new DateTimeOffset(2026,11,8,12,0,0,TimeSpan.Zero);
        var future=SignedFeed(code,device,issuedAtUtc:now.AddHours(1));
        var outcome=PaidLicenseFeedVerifier.Verify(future,code,device,now,
            Secret.GeneratePublicKey().GetEncoded());
        Assert.Equal("feed_invalid",outcome.Mode);
        Assert.False(outcome.Permitted);
    }
    [Fact]
    public void Tampered_Signature_Cannot_Grant_Access()
    {
        const string code="0123456789ABCDEF0123456789ABCDEF";
        const string device="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var bytes=SignedFeed(code,device);
        var doc=Encoding.UTF8.GetString(bytes).Replace("active","revoked");
        var result=PaidLicenseFeedVerifier.Verify(Encoding.UTF8.GetBytes(doc),code,device,
            new DateTimeOffset(2026,11,8,12,0,0,TimeSpan.Zero),Secret.GeneratePublicKey().GetEncoded());
        Assert.NotEqual("active",result.Mode);
    }
    [Fact]
    public void PaidFingerprint_Is_Byte_Compatible_With_Hadramout_MachineGuid_Algorithm()
    {
        var id="10E1AD33-EF19-4774-A460-21E2D9C591E0".ToLowerInvariant();
        var expected=Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes("HADRAMOUT-GITHUB-LICENSE-V1|"+id)));
        Assert.Equal(expected,WindowsLicenseFingerprint.PaidFromWindowsGuid(id));
        Assert.NotEqual(expected,WindowsLicenseFingerprint.TrialFromWindowsGuid(id));
    }
    [Fact]
    public async Task First_Paid_Claim_Only_Sends_Hadramout_Code_And_Hashed_Device()
    {
        const string code="0123456789ABCDEF0123456789ABCDEF";
        const string device="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var handler=new CaptureHandler(SignedFeed(code,device,"pending"));
        using var http=new HttpClient(handler);
        var client=new PaidLicenseClient(http,device,Secret.GeneratePublicKey().GetEncoded());
        var result=await client.ActivateAsync(code);
        Assert.Equal("active",result.Mode);
        Assert.Equal(1,handler.Claims);
        Assert.Contains("\"license_id\":\""+code+"\"",handler.RequestBody);
        Assert.Contains("\"device_code\":\""+device+"\"",handler.RequestBody);
        Assert.DoesNotContain("trial",handler.RequestBody,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("machine_guid",handler.RequestBody,StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ToggleFeedHandler(byte[] feed):HttpMessageHandler
    {
        public bool Offline;
        public byte[] Feed=feed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if(Offline)throw new HttpRequestException("offline");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(Feed)});
        }
    }
    [Fact]
    public async Task Paid_Code_And_Signed_Permanent_Grant_Survive_Restart_Offline()
    {
        const string code="0123456789ABCDEF0123456789ABCDEF";
        const string device="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var directory=Path.Combine(Path.GetTempPath(),"najm-paid-"+Guid.NewGuid());
        try
        {
            var handler=new ToggleFeedHandler(SignedFeed(code,device));
            using var http=new HttpClient(handler);
            var client=new PaidLicenseClient(http,device,Secret.GeneratePublicKey().GetEncoded(),
                new SignedPaidLicenseCache(directory));
            Assert.True((await client.VerifyAsync(code)).Permitted);
            new ProtectedPaidCodeStore(directory).Save(code);
            handler.Offline=true;
            var reopened=new PaidLicenseClient(http,device,Secret.GeneratePublicKey().GetEncoded(),
                new SignedPaidLicenseCache(directory));
            var check=await reopened.VerifyAsync(new ProtectedPaidCodeStore(directory).Load());
            Assert.True(check.Permitted);Assert.Null(check.ExpiresAtUtc);
            var trial=new LocalTrialService(new Store(),"LOCAL");
            var gate=new LicenseGate(trial);gate.SelectTrial();gate.SetPaid(check);
            Assert.Equal("active",gate.OnSuccessfulRouterLogin().Mode);
            Assert.Equal("ready",trial.Check().Mode);
            // A real online revocation must replace the previously cached permanent grant.
            handler.Offline=false;handler.Feed=SignedFeed(code,device,"revoked");
            Assert.Equal("revoked",(await reopened.VerifyAsync(code)).Mode);
            handler.Offline=true;
            Assert.Equal("revoked",(await client.VerifyAsync(code)).Mode);
        }
        finally {if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    [Theory]
    [InlineData("expired")]
    [InlineData("wrong_device")]
    [InlineData("tampered")]
    public async Task Offline_Cache_Does_Not_Authorize_Expired_Other_Device_Or_Tampered_Feed(string invalid)
    {
        const string code="0123456789ABCDEF0123456789ABCDEF";
        const string device="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var directory=Path.Combine(Path.GetTempPath(),"najm-paid-"+Guid.NewGuid());
        try
        {
            var bytes=SignedFeed(code,invalid=="wrong_device"?new string('B',64):device,
                expires:invalid=="expired"?DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd"):null);
            if(invalid=="tampered")bytes[bytes.Length-12]^=1;
            new SignedPaidLicenseCache(directory).Write(bytes);
            using var http=new HttpClient(new ToggleFeedHandler(bytes){Offline=true});
            var result=await new PaidLicenseClient(http,device,Secret.GeneratePublicKey().GetEncoded(),
                new SignedPaidLicenseCache(directory)).VerifyAsync(code);
            Assert.False(result.Permitted);
        }
        finally {if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    [Fact]
    public void Paid_Date_Includes_Whole_Expiry_Day_And_Permanent_Has_No_Deadline()
    {
        const string code="0123456789ABCDEF0123456789ABCDEF";
        var device=new string('A',64);
        var now=new DateTimeOffset(2026,11,10,23,59,59,TimeSpan.Zero);
        var bytes=SignedFeed(code,device,expires:"2026-11-10",issuedAtUtc:now.AddMinutes(-2));
        var result=PaidLicenseFeedVerifier.Verify(bytes,code,device,now,Secret.GeneratePublicKey().GetEncoded());
        Assert.True(result.Permitted);Assert.Equal(now.AddSeconds(1),result.ExpiresAtUtc);
        Assert.Equal("expired",PaidLicenseFeedVerifier.Verify(bytes,code,device,now.AddSeconds(1),
            Secret.GeneratePublicKey().GetEncoded()).Mode);
    }
    private sealed class CaptureHandler(byte[] pendingFeed):HttpMessageHandler
    {
        public int Claims;
        public string RequestBody="";
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,CancellationToken ct)
        {
            if(request.Method==HttpMethod.Post)
            {
                Claims++;
                RequestBody=await request.Content!.ReadAsStringAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {Content=new StringContent("{\"ok\":true,\"status\":\"active\"}")};
            }
            if(request.Method==HttpMethod.Get)
            {
                byte[] feed=Claims>0?SignedFeed("0123456789ABCDEF0123456789ABCDEF",
                    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"):pendingFeed;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {Content=new ByteArrayContent(feed)};
            }
            return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
        }
    }
}
