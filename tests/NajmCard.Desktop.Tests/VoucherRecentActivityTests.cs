using System.IO;
using NajmCard.Core.Models;
using NajmCard.Desktop.Services;
using Xunit;
namespace NajmCard.Desktop.Tests;
public sealed class VoucherRecentActivityTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"najm-activity-"+Guid.NewGuid().ToString("N"));
    public VoucherRecentActivityTests()=>Directory.CreateDirectory(root);
    public void Dispose()=>Directory.Delete(root,true);

    [Fact]
    public void Recording_Card_Batches_And_Confirmed_Delivery_Creates_Real_Activity()
    {
        var card=new Voucher("001234","secret","profile","batch-a",DateTimeOffset.UtcNow);
        using(var store=new VoucherStore(root))
        {
            Assert.Empty(store.ReadRecentActivities());
            store.SaveBatch([card]);
            var rows=store.ReadRecentActivities();
            Assert.Single(rows);
            Assert.Equal("INFO",rows[0].Status);
            Assert.DoesNotContain("secret",rows[0].Detail);
            store.BeginSend(card);
            store.FinishSend(card,"SENT");
        }
        using var reopened=new VoucherStore(root);
        var events=reopened.ReadRecentActivities();
        Assert.True(events.Count>=2);
        Assert.Equal("SUCCESS",events[0].Status);
        Assert.DoesNotContain("secret",string.Concat(events.Select(x=>x.Detail+x.Action)));
    }

    [Fact]
    public void Report_Can_Read_Activity_Without_Unprotecting_Any_Password()
    {
        using var store=new VoucherStore(root);
        store.SaveBatch([new Voucher("00001","pw","gold","a",DateTimeOffset.UtcNow)]);
        Assert.Single(store.ReadRecentActivities(1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>store.ReadRecentActivities(0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>store.ReadRecentActivities(1001));
    }
}
