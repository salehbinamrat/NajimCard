using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class DashboardMetricsTests
{
    private static HotspotUser U(string name, string profile, bool disabled, long bytes, long limit) =>
        new("*1", name, "", profile, disabled, bytes, 0, limit, "");

    [Fact]
    public void Counters_And_Geometric_Chart_Counts_Reflect_Same_Real_Users()
    {
        HotspotUser[] users = [U("a","silver",false,40,100), U("b","gold",false,100,100),
            U("c","silver",true,0,100), U("d","gold",false,0,0)];
        var status = DashboardMetrics.Calculate(users, []);
        Assert.Equal(4,status.Total);
        Assert.Equal(3,status.Enabled);
        Assert.Equal(2,status.Available);
        Assert.Equal(1,status.ExpiredByTransfer);
        Assert.Equal(1,status.DisabledNotExpired);
        Assert.Equal(4,status.Available + status.ExpiredByTransfer + status.DisabledNotExpired);
        Assert.Equal(2,status.Profiles.Single(p=>p.Name=="gold").Count);
        Assert.Equal(2,status.Profiles.Single(p=>p.Name=="silver").Count);
    }

    [Fact]
    public void TotalTime_Left_Never_Decides_Expiration()
    {
        // Time does not exist in the HotspotUser expiration model; only the transfer limit is consulted.
        var state=DashboardMetrics.Calculate([U("never-expired","p",false,10,0),
            U("expired-by-bytes","p",false,512,512)],[]);
        Assert.Equal(1,state.ExpiredByTransfer);
        Assert.Equal(1,state.Available);
    }

    [Fact]
    public void Disabled_Expired_Is_Counted_Exactly_Once_In_Chart()
    {
        var state=DashboardMetrics.Calculate([U("disabled-expired","basic",true,1000,500)],[]);
        Assert.Equal(1,state.ExpiredByTransfer);
        Assert.Equal(0,state.DisabledNotExpired);
        Assert.Equal(0,state.Available);
    }
}
