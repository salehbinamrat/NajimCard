using NajmCard.Core.Services;
using Xunit;
namespace NajmCard.Core.Tests;
public sealed class DashboardPppSessionsTests
{
    private sealed class Spy : IRouterCommandClient
    {
        public readonly List<string> Commands=[];
        public bool Deny;
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Commands.Add(command);
            Assert.Equal("/ppp/active/print",command);
            Assert.Equal(".id,name,service,address,caller-id,uptime",
                attributes![".proplist"]);
            if(Deny) throw new RouterOsTrapException("not enough permissions");
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows =
            [
                new Dictionary<string,string>{{".id","*A"},{"name","ppp1"},
                    {"service","pppoe"},{"address","192.0.2.2"},{"uptime","2m"}},
                new Dictionary<string,string>{{".id","*B"},{"name","ppp2"},
                    {"service","pppoe"},{"address","192.0.2.3"},{"uptime","3m"}}
            ];
            return Task.FromResult(rows);
        }
    }
    [Fact]
    public async Task Reads_Only_The_Lightweight_Active_Sessions_Menu()
    {
        var spy=new Spy();
        var sessions=await new RouterGateway(spy).ReadPppActiveSessionsAsync();
        Assert.Equal(2,sessions.Count);
        Assert.All(sessions,s=>Assert.Equal("pppoe",s.Service));
        Assert.Equal(["/ppp/active/print"],spy.Commands);
    }
    [Fact]
    public async Task Denied_Ppp_Read_Throws_Instead_Of_Reporting_Zero()
    {
        var spy=new Spy{Deny=true};
        await Assert.ThrowsAsync<RouterOsTrapException>(
            ()=>new RouterGateway(spy).ReadPppActiveSessionsAsync());
        Assert.Single(spy.Commands);
    }
}
