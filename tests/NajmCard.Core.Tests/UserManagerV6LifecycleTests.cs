using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>
/// Parity with core/network.py _userman_lifecycle_v6:
/// expiry requires real usage and exhausted positive Transfer Limit, not time left.
/// </summary>
public sealed class UserManagerV6LifecycleTests
{
    private sealed class FakeRouter : IRouterCommandClient
    {
        public readonly List<string> Calls = [];
        public bool BrokenProjection { get; set; }
        public bool NoLifecycle { get; set; }
        public bool InvalidCounters { get; set; }

        private static IReadOnlyDictionary<string,string> Row(
            string id, string name, string profile, string down, string up,
            string disabled="false", string incomplete="false", string limit="",
            string timeLeft="") =>
            new Dictionary<string,string>
            {
                [".id"]=id,["username"]=name,["actual-profile"]=profile,
                ["total-download-used"]=down,["total-upload-used"]=up,
                ["disabled"]=disabled,["incomplete"]=incomplete,
                ["transfer-limit"]=limit,["total-time-left"]=timeLeft
            };

        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command, IReadOnlyDictionary<string,string>? attributes = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(command + (attributes is null?" (full)":" (projected)"));
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows = command switch
            {
                "/tool/user-manager/user/print" when NoLifecycle =>
                    [new Dictionary<string,string> { [".id"]="*A",["username"]="no-metrics" }],
                "/tool/user-manager/user/print" when BrokenProjection && attributes is not null =>
                    [new Dictionary<string,string> { [".id"]="*A",["username"]="unused" }],
                "/tool/user-manager/user/print" =>
                    [
                        Row("*A","unused","gold","0","0"),
                        Row("*B","active","gold","40","30"),
                        Row("*C","expired","gold","70","35"),
                        Row("*D","disabled","gold","100","100","true"),
                        Row("*E","incomplete","gold","100","100","false","true"),
                        Row("*F","time-zero","gold","0","0",timeLeft:"0s"),
                        Row("*1A","orphan","","0","0"),
                        Row("*1B","no-profile-expired","","2","0",limit:"1")
                    ],
                "/tool/user-manager/profile/print" =>
                    [new Dictionary<string,string> { [".id"]="*P",["name"]="gold" }],
                "/tool/user-manager/profile/limitation/print" =>
                    [new Dictionary<string,string> { [".id"]="*L",["name"]="data100",["transfer-limit"]="100" }],
                "/tool/user-manager/profile/profile-limitation/print" =>
                    [new Dictionary<string,string> { ["profile"]="*P",["limitation"]="*L" }],
                _ => throw new InvalidOperationException("Unexpected RouterOS menu: "+command)
            };
            return Task.FromResult(rows);
        }
    }

    [Fact]
    public async Task Classifies_Eight_RouterOs6_Users_Using_Usage_And_Only_Transfer_Limit()
    {
        var fake = new FakeRouter();
        var report = await new RouterGateway(fake).ReadUserManagerV6InventoryAsync();
        Assert.True(report.IsComplete);
        Assert.Equal(8, report.Total);
        Assert.Equal(2, report.Valid);
        Assert.Equal(1, report.Active);
        Assert.Equal(2, report.Expired);
        Assert.Equal(1, report.Disabled);
        Assert.Equal(1, report.Incomplete);
        Assert.Equal(1, report.Unclassified);
        var expired = Assert.Single(report.Users.Where(x=>x.Username=="expired"));
        Assert.Equal("expired", expired.Status);
        Assert.Equal("transfer-limit",expired.ExpirySource);
        Assert.Equal(105,expired.ConsumedBytes);
        Assert.Equal(100,expired.TransferLimit);
        Assert.Equal("valid",Assert.Single(report.Users.Where(x=>x.Username=="time-zero")).Status);
        Assert.Equal("expired",
            Assert.Single(report.Users.Where(x=>x.Username=="no-profile-expired")).Status);
        Assert.Equal(1, fake.Calls.Count(x=>x.StartsWith("/tool/user-manager/user/print")));
    }

    [Fact]
    public async Task Silently_Omitted_Projection_Triggers_A_Full_Compatibility_Read()
    {
        var fake=new FakeRouter{BrokenProjection=true};
        var report=await new RouterGateway(fake).ReadUserManagerV6InventoryAsync();
        Assert.True(report.IsComplete);
        Assert.Contains("/tool/user-manager/user/print (projected)",fake.Calls);
        Assert.Contains("/tool/user-manager/user/print (full)",fake.Calls);
    }

    [Fact]
    public async Task Missing_All_Lifecycle_Fields_Does_Not_Return_Trusted_Zero_Counts()
    {
        var fake=new FakeRouter{NoLifecycle=true};
        var report=await new RouterGateway(fake).ReadUserManagerV6InventoryAsync();
        Assert.False(report.IsComplete);
        Assert.Equal(1,report.Total);
        Assert.Contains("غير مكتملة", report.Diagnostic);
        Assert.DoesNotContain(report.Users,x=>x.Status=="expired");
    }

    [Fact]
    public void Malformed_Transfer_Values_Or_Time_Alone_Must_Never_Prove_Expiry()
    {
        Assert.Equal(0,UserManagerV6Usage.ParseBytes("unlimited"));
        Assert.Equal(0,UserManagerV6Usage.ParseBytes("invalid"));
        Assert.Equal(1024L*1024,UserManagerV6Usage.ParseBytes("1MiB"));
        var row = new Dictionary<string,string>
        {
            ["username"]="bad", ["actual-profile"]="gold",
            ["total-download-used"]="xyz",["total-upload-used"]="0",
            ["transfer-limit"]="100",["total-time-left"]="0s"
        };
        var status=UserManagerV6Usage.Classify(row,100);
        Assert.NotEqual("expired",status.Status);
        Assert.Equal(0,status.ConsumedBytes);
    }
}
