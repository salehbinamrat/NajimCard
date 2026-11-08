using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>Real RouterGateway -> RouterOS command boundary; only positive exhausted
/// Transfer Limit allows an exact-ID remove after one authoritative refresh.</summary>
public sealed class UserManagerV6ExpiredCleanupTests
{
    private static IReadOnlyDictionary<string,string> Row(
        string id, string name, int consumed, int limit=100, string profile="gold",
        bool disabled=false, string timeLeft="0s") =>
        new Dictionary<string,string>
        {
            [".id"]=id, ["username"]=name, ["actual-profile"]=profile,
            ["disabled"]=disabled?"yes":"no", ["incomplete"]="no",
            ["total-download-used"]=consumed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["total-upload-used"]="0",
            ["transfer-limit"]=limit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["total-time-left"]=timeLeft
        };

    private static UserManagerV6Voucher Snapshot(
        string id, string name, int used, int limit=100, string profile="gold") =>
        UserManagerV6Usage.Classify(Row(id,name,used,limit,profile),0);

    private sealed class Fake : IRouterCommandClient
    {
        public IReadOnlyList<IReadOnlyDictionary<string,string>> Users { get; set; }=[];
        public readonly List<string> Removed=[];
        public int ScanCount { get; private set; }
        public int LimitationScans { get; private set; }
        public string? RejectId { get; set; }
        public string? DropId { get; set; }
        public bool NoLifecycle { get; set; }

        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command, IReadOnlyDictionary<string,string>? attributes = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows;
            switch(command)
            {
                case "/tool/user-manager/user/print":
                    ScanCount++;
                    rows=NoLifecycle
                        ? [new Dictionary<string,string>{[".id"]="*1",["username"]="stale"}]
                        : Users;
                    break;
                case "/tool/user-manager/profile/print":
                    rows=[new Dictionary<string,string>{[".id"]="*F",["name"]="gold"}];break;
                case "/tool/user-manager/profile/limitation/print":
                    LimitationScans++;
                    rows=[new Dictionary<string,string>{[".id"]="*E",["name"]="limit100",["transfer-limit"]="100"}];break;
                case "/tool/user-manager/profile/profile-limitation/print":
                    rows=[new Dictionary<string,string>{["profile"]="*F",["limitation"]="*E"}];break;
                case "/tool/user-manager/user/remove":
                    var id=attributes![".id"];
                    if(id==RejectId)throw new RouterOsTrapException("permission denied");
                    if(id==DropId)throw new IOException("connection dropped after write");
                    Removed.Add(id);
                    return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>([]);
                default:throw new InvalidOperationException("Unexpected command: "+command);
            }
            return Task.FromResult(rows);
        }
    }

    [Fact]
    public async Task Uses_Single_Fresh_Full_Scan_And_Skips_Renewed_Profile_Changed_And_Unlimited()
    {
        var fake=new Fake
        {
            Users=[
                Row("*1","expired",110),
                Row("*2","renewed",10),
                Row("*3","new-profile",160,200,"platinum"),
                Row("*4","time-only",0,100,timeLeft:"0s"),
                Row("*5","unlimited",500,0)
            ]
        };
        var snapshot=new[]
        {
            Snapshot("*1","expired",110),
            Snapshot("*2","renewed",140),
            Snapshot("*3","new-profile",150),
            Snapshot("*4","time-only",0),
            Snapshot("*5","unlimited",500,0)
        };
        var result=await new RouterGateway(fake).DeleteExpiredUserManagerV6Async(snapshot);
        Assert.Equal(3,result.Requested);
        Assert.Equal(1,result.Deleted);
        Assert.Equal(2,result.Skipped);
        Assert.Empty(result.Failed);
        Assert.False(result.UncertainOutcome);
        Assert.Equal(["*1"],fake.Removed);
        Assert.Equal(1,fake.ScanCount);
        Assert.Equal(1,fake.LimitationScans);
    }

    [Fact]
    public async Task A_Definite_Trap_Records_Failure_But_Continues_With_Another_Confirmed_Id()
    {
        var fake=new Fake { Users=[Row("*1","one",110),Row("*2","two",130)],RejectId="*1" };
        var result=await new RouterGateway(fake).DeleteExpiredUserManagerV6Async(
            [Snapshot("*1","one",110),Snapshot("*2","two",130)]);
        Assert.Equal(2,result.Requested);
        Assert.Equal(1,result.Deleted);
        Assert.Equal(["one"],result.Failed);
        Assert.Equal(["*2"],fake.Removed);
        Assert.False(result.UncertainOutcome);
    }

    [Fact]
    public async Task Uncertain_Remove_Stops_Batch_Without_Replaying_Any_Id()
    {
        var fake=new Fake { Users=[Row("*1","one",110),Row("*2","two",130)],DropId="*1" };
        var result=await new RouterGateway(fake).DeleteExpiredUserManagerV6Async(
            [Snapshot("*1","one",110),Snapshot("*2","two",130)]);
        Assert.True(result.UncertainOutcome);
        Assert.Equal(0,result.Deleted);
        Assert.Empty(fake.Removed);
        Assert.Equal(1,fake.ScanCount);
    }

    [Fact]
    public async Task Incomplete_Telemetry_Never_Allows_Any_Delete()
    {
        var fake=new Fake { NoLifecycle=true };
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            new RouterGateway(fake).DeleteExpiredUserManagerV6Async(
                [Snapshot("*1","stale",110)]));
        Assert.Empty(fake.Removed);
    }

    [Fact]
    public async Task Different_Name_At_An_Existing_Id_Is_Not_Deleted()
    {
        var fake=new Fake { Users=[Row("*1","different",110)] };
        var result=await new RouterGateway(fake).DeleteExpiredUserManagerV6Async(
            [Snapshot("*1","original",110)]);
        Assert.Equal(0,result.Deleted);
        Assert.Equal(1,result.Skipped);
        Assert.Empty(fake.Removed);
    }

    [Fact]
    public async Task No_Transfer_Expired_Selected_Users_Means_Zero_Router_Commands()
    {
        var fake=new Fake();
        var result=await new RouterGateway(fake).DeleteExpiredUserManagerV6Async(
            [Snapshot("*1","time-only",0),Snapshot("*2","unlimited",999,0)]);
        Assert.Equal(0,result.Requested);
        Assert.Equal(0,fake.ScanCount);
        Assert.Empty(fake.Removed);
    }
}
