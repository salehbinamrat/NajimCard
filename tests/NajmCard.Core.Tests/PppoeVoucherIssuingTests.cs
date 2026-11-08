using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>RouterOS 6 PPPoE voucher creation uses /ppp/secret/add and !done =ret=.</summary>
public sealed class PppoeVoucherIssuingTests
{
    private static Voucher Card(string service="pppoe") =>
        new("001234","pass123","gold","batch",DateTimeOffset.UtcNow,Service:service);

    private sealed class Spy : IRouterCommandClient, IRouterReceiptClient
    {
        public readonly List<(string Path,IReadOnlyDictionary<string,string> Values)> Writes=[];
        public string? Receipt="*A";
        public bool Drop,Trap;
        public Task<RouterCommandReply> CommandWithReceiptAsync(
            string path,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Writes.Add((path,new Dictionary<string,string>(attributes!)));
            if (Trap) throw new RouterOsTrapException("profile not found");
            if (Drop) throw new IOException("lost write acknowledgement");
            return Task.FromResult(new RouterCommandReply([],
                Receipt is null ? new Dictionary<string,string>() :
                    new Dictionary<string,string> { ["ret"]=Receipt }));
        }
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string path,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default) =>
            throw new InvalidOperationException("Do not use discard-receipt path for PPPoE add");
    }

    [Fact]
    public async Task Creates_One_Pppoe_Secret_Using_Exact_Source_Attributes_And_Receipt()
    {
        var spy=new Spy();
        var result=await new RouterGateway(spy).CreatePppoeVoucherAsync(Card());
        Assert.True(result.Success);
        Assert.False(result.UncertainOutcome);
        Assert.Equal("*A",result.UserId);
        var command=Assert.Single(spy.Writes);
        Assert.Equal("/ppp/secret/add",command.Path);
        Assert.Equal("001234",command.Values["name"]);
        Assert.Equal("pass123",command.Values["password"]);
        Assert.Equal("gold",command.Values["profile"]);
        Assert.Equal("pppoe",command.Values["service"]);
        Assert.DoesNotContain("customer",command.Values.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("001234")]
    [InlineData("*A,*B")]
    [InlineData("*XYZ")]
    public async Task Missing_Or_Malformed_Receipt_Is_Uncertain_And_Never_Reissued(string? ret)
    {
        var spy=new Spy {Receipt=ret};
        var result=await new RouterGateway(spy).CreatePppoeVoucherAsync(Card());
        Assert.False(result.Success);
        Assert.True(result.UncertainOutcome);
        Assert.Single(spy.Writes);
    }

    [Fact]
    public async Task Definite_Trap_Is_Failed_But_Lost_Reply_Is_Uncertain()
    {
        var trapped=new Spy{Trap=true};
        var fail=await new RouterGateway(trapped).CreatePppoeVoucherAsync(Card());
        Assert.False(fail.Success);
        Assert.False(fail.UncertainOutcome);
        var dropped=new Spy{Drop=true};
        var unknown=await new RouterGateway(dropped).CreatePppoeVoucherAsync(Card());
        Assert.False(unknown.Success);
        Assert.True(unknown.UncertainOutcome);
        Assert.Single(dropped.Writes);
    }

    [Fact]
    public async Task Wrong_Service_Must_Not_Touch_Router()
    {
        var spy=new Spy();
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            new RouterGateway(spy).CreatePppoeVoucherAsync(Card("hotspot")));
        Assert.Empty(spy.Writes);
    }
}
