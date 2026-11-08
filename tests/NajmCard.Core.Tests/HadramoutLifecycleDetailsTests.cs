using NajmCard.Core.Services;
using Xunit;
namespace NajmCard.Core.Tests;
public sealed class HadramoutLifecycleDetailsTests
{
    [Fact]
    public void RouterOs6_Detailed_Usage_Preserves_Bytes_Sessions_And_Last_Seen_Without_Time_Based_Expiry()
    {
        var v=UserManagerV6Usage.Classify(new Dictionary<string,string>
        {
            [".id"]="*F",["username"]="123456",["actual-profile"]="200RY",
            ["total-download-used"]="100",["total-upload-used"]="30",
            ["transfer-limit"]="500",["uptime-used"]="4h5m",
            ["last-seen"]="nov/07/2026 10:00:00",["active-sessions"]="1",
            ["total-time-left"]="0s"
        },0);
        Assert.Equal("active",v.Status);
        Assert.Equal(370,v.RemainingBytes);
        Assert.Equal("4h5m",v.UptimeUsed);
        Assert.Equal("nov/07/2026 10:00:00",v.LastSeen);
        Assert.Equal(1,v.ActiveSessions);
        Assert.Equal(100,v.DownloadBytes);
        Assert.Equal(30,v.UploadBytes);
    }
}
