using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>
/// Legacy User Manager v6 limit definitions, aliases and link mapping in
/// Hadramout core/network.py::_userman6_profile_limit_map.
/// </summary>
public sealed class UserManagerV6ProfileLimitSummaryTests
{
    private sealed class Spy : IRouterCommandClient
    {
        public readonly List<string> Calls=[];
        public string? Denied;
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Calls.Add(command);
            if (command == Denied) throw new RouterOsTrapException("not enough permissions");
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows=command switch
            {
                "/tool/user-manager/profile/print" =>
                [
                    new Dictionary<string,string>{{".id","*A"},{"name","gold"}},
                    new Dictionary<string,string>{{".id","*B"},{"name","silver"}}
                ],
                "/tool/user-manager/profile/limitation/print" =>
                [
                    new Dictionary<string,string>{{".id","*11"},{"name","limit-2M"},{"transfer-limit","2MiB"}},
                    new Dictionary<string,string>{{".id","*12"},{"name","limit-1M"},{"transfer-limit","1MiB"}},
                    new Dictionary<string,string>{{".id","*13"},{"name","unlimited"},{"transfer-limit","0"}}
                ],
                "/tool/user-manager/profile/profile-limitation/print" =>
                [
                    new Dictionary<string,string>{{"profile","*A"},{"limitation","*11"}},
                    new Dictionary<string,string>{{"profile","gold"},{"limitation","limit-1M"}},
                    new Dictionary<string,string>{{"profile","*B"},{"limitation","*13"}}
                ],
                _=>throw new InvalidOperationException("Not RouterOS 6 profile limitation path: "+command)
            };
            return Task.FromResult(rows);
        }
    }

    [Fact]
    public async Task Reads_RouterOs6_Profile_And_Limitation_Definitions_Once_Each()
    {
        var spy=new Spy();
        var rows=await new RouterGateway(spy).ReadUserManagerV6ProfileLimitSummaryAsync();
        Assert.Equal(2,rows.Count);
        var gold=Assert.Single(rows.Where(x=>x.ProfileName=="gold"));
        Assert.Equal(1024L*1024,gold.EffectiveTransferLimitBytes);
        Assert.Equal(2,gold.LinkedLimitationCount);
        Assert.Contains("limit-2M",gold.LimitationNames);
        Assert.Contains("limit-1M",gold.LimitationNames);
        var silver=Assert.Single(rows.Where(x=>x.ProfileName=="silver"));
        Assert.Equal(0,silver.EffectiveTransferLimitBytes);
        Assert.Equal(1,silver.LinkedLimitationCount);
        Assert.Equal([
            "/tool/user-manager/profile/print",
            "/tool/user-manager/profile/limitation/print",
            "/tool/user-manager/profile/profile-limitation/print"
        ],spy.Calls);
    }

    [Theory]
    [InlineData("/tool/user-manager/profile/print")]
    [InlineData("/tool/user-manager/profile/limitation/print")]
    [InlineData("/tool/user-manager/profile/profile-limitation/print")]
    public async Task Denied_One_Table_Stops_Instead_Of_Reporting_Zero_Limits(string denied)
    {
        var spy=new Spy{Denied=denied};
        await Assert.ThrowsAsync<RouterOsTrapException>(() =>
            new RouterGateway(spy).ReadUserManagerV6ProfileLimitSummaryAsync());
    }
}
