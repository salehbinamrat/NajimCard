using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>Safe, one-profile RouterOS 6 PPP rate-limit edit from a real selected row.</summary>
public sealed class PppProfileRateLimitTests
{
    private static PppProfile Profile() =>
        new("*D","gold","8M/8M","192.0.2.1","ppp-pool");
    private static IReadOnlyDictionary<string,string> Row(string name="gold",
        string rate="8M/8M",string address="192.0.2.1",string remote="ppp-pool") =>
        new Dictionary<string,string>
        {
            [".id"]="*D",["name"]=name,["rate-limit"]=rate,
            ["local-address"]=address,["remote-address"]=remote
        };

    private sealed class Spy : IRouterCommandClient, IRouterQueryClient
    {
        public readonly List<string> Calls=[];
        public IReadOnlyList<IReadOnlyDictionary<string,string>> Rows = [Row()];
        public bool Drop,Trap,Ignore;
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByIdAsync(
            string cmd,string id,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Calls.Add(cmd+" ?.id="+id);
            Assert.Equal("/ppp/profile/print",cmd);
            Assert.Equal("*D",id);
            Assert.Equal(".id,name,rate-limit,local-address,remote-address",
                attributes![".proplist"]);
            return Task.FromResult(Rows);
        }
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string cmd,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Calls.Add(cmd);
            Assert.Equal("/ppp/profile/set",cmd);
            Assert.Equal("*D",attributes![".id"]);
            Assert.Equal("4M/4M",attributes["rate-limit"]);
            Assert.Equal(2,attributes.Count);
            if(Trap)throw new RouterOsTrapException("not allowed");
            if(Drop)throw new IOException("lost reply");
            if(!Ignore)Rows=[Row(rate:"4M/4M")];
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>([]);
        }
    }

    [Fact]
    public async Task Updates_Exactly_One_Selected_Profile_And_Reads_Back_Rate()
    {
        var spy=new Spy();
        var result=await new RouterGateway(spy).SetPppProfileRateLimitAsync(Profile(),"4M/4M");
        Assert.True(result.Success);
        Assert.Equal([
            "/ppp/profile/print ?.id=*D",
            "/ppp/profile/set",
            "/ppp/profile/print ?.id=*D"
        ],spy.Calls);
    }

    [Theory]
    [InlineData("other","8M/8M","192.0.2.1","ppp-pool")]
    [InlineData("gold","2M/2M","192.0.2.1","ppp-pool")]
    [InlineData("gold","8M/8M","192.0.2.8","ppp-pool")]
    [InlineData("gold","8M/8M","192.0.2.1","other-pool")]
    public async Task Changed_Target_Profile_Fails_Closed_Before_Any_Set(
        string name,string rate,string local,string remote)
    {
        var spy=new Spy {Rows=[Row(name,rate,local,remote)]};
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RouterGateway(spy).SetPppProfileRateLimitAsync(Profile(),"4M/4M"));
        Assert.Single(spy.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("4M/4M; /system reboot")]
    [InlineData("bad")]
    [InlineData("-1M/4M")]
    [InlineData("0M/0M")]
    public async Task Invalid_New_Rates_Are_Rejected_Without_Router_IO(string rate)
    {
        var spy=new Spy();
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            new RouterGateway(spy).SetPppProfileRateLimitAsync(Profile(),rate));
        Assert.Empty(spy.Calls);
    }

    [Fact]
    public async Task Lost_Write_Reply_Or_Unverified_Apply_Does_Not_Claim_Success()
    {
        var dropped=new Spy {Drop=true};
        var lost=await new RouterGateway(dropped).SetPppProfileRateLimitAsync(Profile(),"4M/4M");
        Assert.False(lost.Success);
        Assert.True(lost.UncertainOutcome);
        Assert.Equal(2,dropped.Calls.Count);
        var ignored=new Spy {Ignore=true};
        var unknown=await new RouterGateway(ignored).SetPppProfileRateLimitAsync(Profile(),"4M/4M");
        Assert.False(unknown.Success);
        Assert.True(unknown.UncertainOutcome);
        Assert.Equal(3,ignored.Calls.Count);
    }

    [Fact]
    public async Task Explicit_Trap_Is_Reported_Without_Retry()
    {
        var spy=new Spy {Trap=true};
        var result=await new RouterGateway(spy).SetPppProfileRateLimitAsync(Profile(),"4M/4M");
        Assert.False(result.Success);
        Assert.False(result.UncertainOutcome);
        Assert.Equal(2,spy.Calls.Count);
    }
}
