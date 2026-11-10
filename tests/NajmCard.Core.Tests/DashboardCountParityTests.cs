using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;
namespace NajmCard.Core.Tests;
public sealed class DashboardCountParityTests
{
    private sealed class RouterSpy : IRouterCommandClient,IRouterReceiptClient
    {
        public readonly List<string> Paths=[];
        public string? Denied;
        public string? BadCountPath;
        public Task<RouterCommandReply> CommandWithReceiptAsync(string command,
            IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Paths.Add(command);
            Assert.EndsWith("/print",command);
            Assert.NotNull(attributes);
            Assert.Single(attributes);
            Assert.Equal("",attributes["count-only"]);
            if(command==Denied)throw new RouterOsTrapException("not enough permissions");
            var n=command==BadCountPath?"bogus":command switch
            {
                "/tool/user-manager/user/print"=>"9457",
                "/ip/hotspot/active/print"=>"261",
                "/ppp/active/print"=>"0",
                _=>throw new InvalidOperationException(command)
            };
            return Task.FromResult(new RouterCommandReply([],
                new Dictionary<string,string> {["ret"]=n}));
        }
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string path,IReadOnlyDictionary<string,string>? attrs=null,
            CancellationToken cancellationToken=default)=>
            throw new InvalidOperationException("A full inventory read is forbidden on dashboard.");
    }
    [Fact]
    public async Task Dashboard_Uses_Exactly_Three_Small_CountOnly_Commands()
    {
        var spy=new RouterSpy();
        var result=await new RouterGateway(spy).ReadDashboardLiveCountsAsync();
        Assert.Equal(9457,result.UserManagerTotal);
        Assert.Equal(261,result.OnlineTotal);
        Assert.False(result.IsPartial);
        Assert.Equal(["/tool/user-manager/user/print","/ip/hotspot/active/print",
            "/ppp/active/print"],spy.Paths);
    }
    [Fact]
    public async Task PerSecond_Online_Refresh_Must_Not_Requery_UserManager_Total()
    {
        var spy=new RouterSpy();
        var result=await new RouterGateway(spy).ReadDashboardOnlineOnlyAsync();
        Assert.Null(result.UserManagerTotal);
        Assert.Equal(261,result.OnlineTotal);
        Assert.Equal(["/ip/hotspot/active/print","/ppp/active/print"],spy.Paths);
    }

    [Fact]
    public async Task Partial_Online_Counter_Reports_Only_Known_Components()
    {
        var spy=new RouterSpy{Denied="/ppp/active/print"};
        var result=await new RouterGateway(spy).ReadDashboardLiveCountsAsync();
        Assert.Equal(261,result.OnlineTotal);
        Assert.Null(result.PppOnline);
        Assert.True(result.IsPartial);
    }
    [Fact]
    public async Task Malformed_Total_Cannot_Become_A_False_Zero()
    {
        var spy=new RouterSpy{BadCountPath="/tool/user-manager/user/print"};
        var result=await new RouterGateway(spy).ReadDashboardLiveCountsAsync();
        Assert.Null(result.UserManagerTotal);
        Assert.Equal(261,result.OnlineTotal);
    }
    [Fact]
    public async Task Denied_Hotspot_Count_Does_Not_Read_Whole_Session_Table()
    {
        var spy=new RouterSpy{Denied="/ip/hotspot/active/print"};
        var result=await new RouterGateway(spy).ReadDashboardLiveCountsAsync();
        Assert.Null(result.HotspotOnline);
        Assert.True(result.IsPartial);
        Assert.Equal(3,spy.Paths.Count);
    }
}
