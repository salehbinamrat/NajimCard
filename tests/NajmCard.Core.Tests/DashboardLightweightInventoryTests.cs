using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>
/// Test real RouterGateway query boundary for the performance-sensitive home screen.
/// A projection must omit password and comment fields, never fake absent counters.
/// </summary>
public sealed class DashboardLightweightInventoryTests
{
    private sealed class Fake : IRouterCommandClient
    {
        public readonly List<(string Path,IReadOnlyDictionary<string,string>? Args)> Calls=[];
        public bool MissingLimit,Denied;
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Calls.Add((command,attributes));
            Assert.Equal("/ip/hotspot/user/print",command);
            if(Denied) throw new RouterOsTrapException("not enough permissions");
            var row=new Dictionary<string,string>
            {
                [".id"]="*A",["name"]="001234",["profile"]="medium",
                ["disabled"]="yes",["bytes-in"]="1024",["bytes-out"]="2048",
                ["limit-bytes-total"]="3072"
            };
            if(MissingLimit && attributes is not null)
                row.Remove("limit-bytes-total");
            if(attributes is null)
                row["password"]="must never leak into dashboard view";
            IReadOnlyList<IReadOnlyDictionary<string,string>> result=[row];
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task Projected_Inventory_Sends_Only_Necessary_Fields_And_Maps_Yes_Disabled()
    {
        var spy=new Fake();
        var rows=await new RouterGateway(spy).ReadDashboardUsersAsync();
        Assert.Single(spy.Calls);
        var attrs=spy.Calls[0].Args!;
        Assert.Equal(".id,name,profile,disabled,bytes-in,bytes-out,limit-bytes-total",
            attrs[".proplist"]);
        Assert.DoesNotContain("password",attrs[".proplist"]);
        Assert.DoesNotContain("comment",attrs[".proplist"]);
        var user=Assert.Single(rows);
        Assert.True(user.Disabled);
        Assert.True(user.IsExpiredByTransferLimit);
        Assert.Equal("",user.Password);
    }

    [Fact]
    public async Task Missing_Essential_Projected_Data_Falls_Back_Only_Once_Per_Connection()
    {
        var spy=new Fake{MissingLimit=true};
        var gateway=new RouterGateway(spy);
        var first=await gateway.ReadDashboardUsersAsync();
        Assert.Equal(2,spy.Calls.Count);
        Assert.NotNull(spy.Calls[0].Args);
        Assert.Null(spy.Calls[1].Args);
        Assert.True(Assert.Single(first).IsExpiredByTransferLimit);
        Assert.True(Assert.Single(first).Disabled);
        spy.Calls.Clear();
        var next=await gateway.ReadDashboardUsersAsync();
        Assert.Single(spy.Calls);
        Assert.Null(spy.Calls[0].Args);
        Assert.Single(next);
    }

    [Fact]
    public async Task Router_Permission_Failure_Cannot_Be_Misreported_As_Empty_Inventory()
    {
        var spy=new Fake{Denied=true};
        await Assert.ThrowsAsync<RouterOsTrapException>(
            ()=>new RouterGateway(spy).ReadDashboardUsersAsync());
        Assert.Single(spy.Calls);
    }
}
