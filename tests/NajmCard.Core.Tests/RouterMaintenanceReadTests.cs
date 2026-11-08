using System.Globalization;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>Checks the actual RouterOS API command and !done receipt boundary.</summary>
public sealed class RouterMaintenanceReadTests
{
    private sealed class RouterSpy : IRouterCommandClient,IRouterReceiptClient
    {
        public List<(string Name,IReadOnlyDictionary<string,string>? Args)> Calls = [];
        public string CountValue="2";
        public bool RejectCount,RejectNeighbors;
        public bool UnknownServerState;
        public Task<RouterCommandReply> CommandWithReceiptAsync(string command,
            IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add((command,attributes));
            Assert.Equal("/ip/neighbor/print",command);
            Assert.NotNull(attributes);
            Assert.Single(attributes);
            Assert.Equal("",attributes["count-only"]);
            if(RejectCount)throw new RouterOsTrapException("not enough permissions");
            return Task.FromResult(new RouterCommandReply([],new Dictionary<string,string>
                {["ret"]=CountValue}));
        }

        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(string command,
            IReadOnlyDictionary<string,string>? attributes=null,CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add((command,attributes));
            if(command=="/ip/neighbor/print")
            {
                if(RejectNeighbors)throw new RouterOsTrapException("not enough permissions");
                Assert.Equal("interface,address,address4,mac-address,identity,platform,version,board",
                    attributes![".proplist"]);
                IReadOnlyList<IReadOnlyDictionary<string,string>> neighbors=[
                    new Dictionary<string,string>{{"interface","ether3"},{"address","fe80::1"},
                        {"address4","192.0.2.5"},{"mac-address","AA:BB:CC:DD:EE:FF"},
                        {"identity","LiteBeam AC"},{"platform","Ubiquiti"},
                        {"version","8.7"},{"board","LiteBeam"}},
                    new Dictionary<string,string>{{"interface","ether4"},{"address","192.0.2.6"},
                        {"mac-address","11:22:33:44:55:66"},{"identity","M5"}}
                ];
                return Task.FromResult(neighbors);
            }
            if(command=="/ip/hotspot/print")
            {
                Assert.Equal(".id,name,interface,profile,disabled,invalid,running",
                    attributes![".proplist"]);
                IReadOnlyList<IReadOnlyDictionary<string,string>> rows=[
                    new Dictionary<string,string>{{".id","*A"},{"name","MAIN"},
                        {"interface","bridge1"},{"profile","default"},{"disabled","no"},
                        {"invalid","no"},{"running",UnknownServerState?"invalid-state":"yes"}},
                    new Dictionary<string,string>{{".id","not-an-id"},{"name","ignore"}},
                    new Dictionary<string,string>{{".id","*B"},{"name","SECOND"},
                        {"interface","ether3"},{"profile","guest"},{"disabled","yes"},
                        {"invalid","no"},{"running","no"}}
                ];
                return Task.FromResult(rows);
            }
            throw new InvalidOperationException("Unexpected RouterOS command: "+command);
        }
    }

    [Fact]
    public async Task Neighbor_Discovery_Counts_Before_Projecting_And_Returns_Version_And_IPv4()
    {
        var spy=new RouterSpy();
        var result=await new RouterGateway(spy).ReadNearbyDevicesAsync();
        Assert.Equal(2,result.Count);
        Assert.Equal(["/ip/neighbor/print","/ip/neighbor/print"],spy.Calls.Select(x=>x.Name));
        Assert.Equal("",spy.Calls[0].Args!["count-only"]);
        Assert.Single(spy.Calls[0].Args!);
        Assert.Single(spy.Calls[1].Args!);
        Assert.Equal("192.0.2.5",result[0].PreferredAddress);
        Assert.Equal("LiteBeam AC",result[0].Identity);
        Assert.Equal("8.7",result[0].Version);
        Assert.Equal("192.0.2.6",result[1].PreferredAddress);
    }

    [Theory]
    [InlineData("5001")]
    [InlineData("-1")]
    [InlineData("bogus")]
    [InlineData("")]
    public async Task Untrusted_Or_Excessive_Count_Fails_Before_Large_Inventory_Request(string count)
    {
        var spy=new RouterSpy {CountValue=count};
        await Assert.ThrowsAsync<InvalidDataException>(
            ()=>new RouterGateway(spy).ReadNearbyDevicesAsync());
        Assert.Single(spy.Calls);
        Assert.Contains("count-only",spy.Calls[0].Args!.Keys);
    }

    [Fact]
    public async Task Denied_Read_Fails_Closed_Without_Partial_Success()
    {
        var spy=new RouterSpy {RejectNeighbors=true};
        await Assert.ThrowsAsync<RouterOsTrapException>(
            ()=>new RouterGateway(spy).ReadNearbyDevicesAsync());
        Assert.Equal(2,spy.Calls.Count);
    }

    [Fact]
    public async Task Projected_Hotspot_Servers_Preserve_RouterOS6_States_And_Ignore_Invalid_Ids()
    {
        var spy=new RouterSpy();
        var servers=await new RouterGateway(spy).ReadHotspotServerStatusAsync();
        Assert.Equal(2,servers.Count);
        Assert.Equal("MAIN",servers[0].Name);
        Assert.True(servers[0].Running);
        Assert.False(servers[0].Disabled);
        Assert.Equal("يعمل",servers[0].StateLabel);
        Assert.Equal("معطل",servers[1].StateLabel);
        Assert.Single(spy.Calls);
        Assert.Equal("/ip/hotspot/print",spy.Calls[0].Name);
        Assert.DoesNotContain("password",spy.Calls[0].Args![".proplist"]);
    }

    [Fact]
    public async Task Unrecognized_Server_State_Is_Unknown_Not_Falsely_Stopped()
    {
        var spy=new RouterSpy {UnknownServerState=true};
        var servers=await new RouterGateway(spy).ReadHotspotServerStatusAsync();
        Assert.Null(servers[0].Running);
        Assert.Equal("غير معروف",servers[0].StateLabel);
    }
}
