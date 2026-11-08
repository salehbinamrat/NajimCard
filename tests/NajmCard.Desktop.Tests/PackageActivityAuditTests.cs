using System.IO;
using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Desktop.Tests;

/// <summary>Hadramout activity page: network-package writes must be visible in
/// the local SQLite audit and must never persist voucher credentials.</summary>
public sealed class PackageActivityAuditTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"najm-profile-audit-"+Guid.NewGuid().ToString("N"));
    public PackageActivityAuditTests()=>Directory.CreateDirectory(root);
    public void Dispose()=>Directory.Delete(root,true);
    [Fact]
    public void ProfileCreateEditDelete_And_Uncertain_Are_Separate_Durable_Local_Events()
    {
        using(var db=new VoucherStore(root))
        {
            db.RecordPackageOperation("hotspot","create","200RY","SUCCESS");
            db.RecordPackageOperation("hotspot","edit","200RY","UNKNOWN");
            db.RecordPackageOperation("hotspot","remove","200RY","SUCCESS");
            db.RecordPackageOperation("pppoe","rate","300RY","ERROR");
        }
        using var reopened=new VoucherStore(root);
        var log=reopened.ReadActivityPage("NETWORK",null,0,50);
        Assert.Equal(4,log.Total);
        Assert.Equal(4,log.Items.Count);
        Assert.Contains(log.Items,x=>x.Status=="UNKNOWN");
        Assert.All(log.Items,x=>Assert.DoesNotContain("password=",x.Detail));
        Assert.All(log.Items,x=>Assert.DoesNotContain("secret=",x.Detail));
        Assert.Contains(log.Items,x=>x.Detail.Contains("200RY"));
    }
    [Theory]
    [InlineData("invalid","create","gold","SUCCESS")]
    [InlineData("hotspot","reboot","gold","SUCCESS")]
    [InlineData("hotspot","remove","gold","DONE")]
    [InlineData("hotspot","edit","bad\npassword=abc","SUCCESS")]
    public void Rejects_Unrecognized_Or_Suspicious_Audit_Values(
        string service,string action,string profile,string status)
    {
        using var db=new VoucherStore(root);
        Assert.ThrowsAny<ArgumentException>(()=>
            db.RecordPackageOperation(service,action,profile,status));
        Assert.Empty(db.ReadRecentActivities());
    }
}
