using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>Real RouterGateway -> RouterOS command path: one full authoritative scan,
/// no redundant post-delete inventory query, never remove a renewed/changed card.</summary>
public sealed class FastSafeExpiredCleanupTests
{
    private static Dictionary<string,string> Row(string id,string user,long used,long limit=100)
        =>new()
        {
            [".id"]=id,["username"]=user,["actual-profile"]="gold",
            ["disabled"]="no",["incomplete"]="no",["total-download-used"]=used.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["total-upload-used"]="0",["transfer-limit"]=limit.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["total-time-left"]="0s"
        };
    private sealed class Router : IRouterCommandClient
    {
        public List<IReadOnlyDictionary<string,string>> Users=[];
        public int UserReads;
        public int Removes;
        public bool Disconnected;
        public readonly List<string> Removed=[];
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch(command)
            {
                case "/tool/user-manager/user/print":
                    UserReads++;
                    return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(
                        Users.ToArray());
                case "/tool/user-manager/profile/print":
                    return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(
                        [new Dictionary<string,string>{{".id","*B"},{"name","gold"}}]);
                case "/tool/user-manager/profile/limitation/print":
                case "/tool/user-manager/profile/profile-limitation/print":
                    return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>([]);
                case "/tool/user-manager/user/remove":
                    Removes++;
                    if(Disconnected) throw new IOException("lost acknowledgement");
                    Removed.Add(attributes![".id"]);
                    return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>([]);
                default:throw new InvalidOperationException(command);
            }
        }
    }
    [Fact]
    public async Task One_Scan_And_Result_Contains_Authoritative_Remaining_Users_Without_Second_Read()
    {
        var fake=new Router
        {
            Users=[Row("*A","exhausted",140),Row("*B","renewed",15),
                   Row("*C","unlimited",500,0)]
        };
        var outdated=new[]
        {
            UserManagerV6Usage.Classify(Row("*A","exhausted",140),0),
            UserManagerV6Usage.Classify(Row("*B","renewed",130),0)
        };
        var observed=new List<ExpiredTransferDeletionProgress>();
        var progress=new ProgressSpy(observed);
        var result=await new RouterGateway(fake).DeleteExpiredUserManagerV6Async(
            outdated,progress:progress);
        Assert.Equal(2,result.Requested);
        Assert.Equal(1,result.Deleted);
        Assert.Equal(1,result.Skipped);
        Assert.Equal(1,fake.UserReads);
        Assert.Equal(["*A"],fake.Removed);
        Assert.NotNull(result.RemainingUserManager);
        Assert.Equal(["renewed","unlimited"],
            result.RemainingUserManager!.Select(x=>x.Username).ToArray());
        Assert.NotEmpty(observed);
        Assert.Equal(1,observed.Last().Deleted);
    }
    [Fact]
    public async Task Ambiguous_Remove_Does_Not_Offer_Misleading_Refreshed_List_Or_Retry()
    {
        var fake=new Router{Users=[Row("*A","one",110)],Disconnected=true};
        var result=await new RouterGateway(fake).DeleteExpiredUserManagerV6Async(
            [UserManagerV6Usage.Classify(Row("*A","one",110),0)]);
        Assert.True(result.UncertainOutcome);
        Assert.Null(result.RemainingUserManager);
        Assert.Equal(1,fake.UserReads);
        Assert.Equal(1,fake.Removes);
    }
    private sealed class ProgressSpy(List<ExpiredTransferDeletionProgress> items)
        : IProgress<ExpiredTransferDeletionProgress>
    {
        public void Report(ExpiredTransferDeletionProgress value)=>items.Add(value);
    }
}
