using System.IO;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Desktop.Tests;

public sealed class ExpiredCleanupAuditTests : IDisposable
{
    private readonly string directory=Path.Combine(Path.GetTempPath(),
        "najm-cleanup-audit-"+Guid.NewGuid().ToString("N"));
    public ExpiredCleanupAuditTests()=>Directory.CreateDirectory(directory);
    public void Dispose()=>Directory.Delete(directory,true);

    [Fact]
    public void Confirmed_And_Uncertain_Cleanup_Summaries_Are_Durable_And_Private()
    {
        using(var store=new VoucherStore(directory))
        {
            store.RecordExpiredCleanup("userman",
                new ExpiredTransferDeletionResult(155,150,3,["sensitive-account-xyz"],false));
            store.RecordExpiredCleanup("hotspot",
                new ExpiredTransferDeletionResult(3,1,0,[],true));
        }
        using var reread=new VoucherStore(directory);
        var entries=reread.ReadActivityPage("NETWORK",null,0,10);
        Assert.Equal(2,entries.Total);
        Assert.Contains(entries.Items,x=>x.Status=="UNKNOWN");
        Assert.Contains(entries.Items,x=>x.Detail.Contains("150"));
        Assert.All(entries.Items,x=>
        {
            Assert.DoesNotContain("sensitive-account-xyz",x.Detail);
            Assert.DoesNotContain("password",x.Detail,StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Unsupported_Service_Is_Rejected_Before_Audit_Write()
    {
        using var store=new VoucherStore(directory);
        Assert.Throws<ArgumentException>(()=>store.RecordExpiredCleanup("other",
            new ExpiredTransferDeletionResult(1,1,0,[],false)));
        Assert.Empty(store.ReadRecentActivities());
    }
}
