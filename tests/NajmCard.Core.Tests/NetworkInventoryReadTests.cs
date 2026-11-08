using System.Net;
using System.Net.Sockets;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>
  /// The exact RouterOS6 API boundary for read-only interface/IP/neighbor discovery.
  /// These assertions must fail before the network inventory implementation exists.
  /// </summary>
public sealed class NetworkInventoryReadTests
{
    private sealed class RouterSpy : IRouterCommandClient, IRouterReceiptClient
    {
        public readonly List<(string Path, IReadOnlyDictionary<string,string>? Args)> Calls = [];
        public string? Deny;
        public bool MissingInterfaceName;
        public bool UnknownRunning;
        public Task<RouterCommandReply> CommandWithReceiptAsync(
            string command, IReadOnlyDictionary<string,string>? attributes = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add((command,attributes));
            Assert.Equal("/ip/neighbor/print",command);
            Assert.Equal("",attributes!["count-only"]);
            if(Deny == command) throw new RouterOsTrapException("not enough permissions");
            return Task.FromResult(new RouterCommandReply([],
                new Dictionary<string,string> { ["ret"]="1" }));
        }

        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command, IReadOnlyDictionary<string,string>? attributes = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add((command,attributes));
            if (Deny == command) throw new RouterOsTrapException("not enough permissions");
            static IReadOnlyDictionary<string,string> Row(params (string Key,string Value)[] fields)
                => fields.ToDictionary(x => x.Key,x => x.Value);
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows = command switch
            {
                "/interface/print" => [Row((".id","*1"),
                    ("name",MissingInterfaceName ? "" : "ether1"),("type","ether"),
                    ("running",UnknownRunning ? "unexpected" : "true"),
                    ("disabled","false"),("mac-address","AA:BB:CC:DD:EE:FF"))],
                "/ip/address/print" => [Row((".id","*2"),("address","10.0.0.1/24"),
                    ("network","10.0.0.0"),("interface","ether1"),("disabled","no"))],
                "/ip/neighbor/print" => [Row((".id","*3"),("identity","AP east"),
                    ("interface","ether1"),("address","10.0.0.2"),("address4","10.0.0.2"),
                    ("mac-address","01:02:03:04:05:06"),("platform","MikroTik"))],
                _ => throw new InvalidOperationException("Unexpected RouterOS command: " + command)
            };
            return Task.FromResult(rows);
        }
    }

    [Fact]
    public async Task Reads_Projected_Lists_With_Bounded_Neighbor_Count_No_Writes_And_No_Secrets()
    {
        var spy = new RouterSpy();
        var inventory = await new RouterGateway(spy).ReadNetworkInventoryAsync();
        Assert.Equal(["/interface/print","/ip/address/print","/ip/neighbor/print","/ip/neighbor/print"],
            spy.Calls.Select(x => x.Path));
        Assert.Equal(".id,name,type,running,disabled,mac-address",
            spy.Calls[0].Args![".proplist"]);
        Assert.Equal(".id,address,network,interface,disabled",
            spy.Calls[1].Args![".proplist"]);
        Assert.Equal("",spy.Calls[2].Args!["count-only"]);
        Assert.Equal("interface,address,address4,mac-address,identity,platform,version,board",
            spy.Calls[3].Args![".proplist"]);
        Assert.All(spy.Calls, x => Assert.Single(x.Args!));
        Assert.Equal("ether1",Assert.Single(inventory.Interfaces).Name);
        Assert.Equal(true,Assert.Single(inventory.Interfaces).Running);
        Assert.Equal(false,Assert.Single(inventory.Interfaces).Disabled);
        Assert.Equal("10.0.0.1/24",Assert.Single(inventory.Addresses).Address);
        Assert.Equal(false,Assert.Single(inventory.Addresses).Disabled);
        Assert.Equal("AP east",Assert.Single(inventory.Neighbors).Identity);
        Assert.Equal("ether1",Assert.Single(inventory.Neighbors).Interface);
    }

    [Fact]
    public async Task Denied_Neighbor_Read_Fails_Closed_Not_Partial_Success()
    {
        var spy = new RouterSpy { Deny="/ip/neighbor/print" };
        await Assert.ThrowsAsync<RouterOsTrapException>(
            () => new RouterGateway(spy).ReadNetworkInventoryAsync());
        Assert.Equal(3,spy.Calls.Count);
        Assert.All(spy.Calls, x => Assert.EndsWith("/print",x.Path));
    }

    [Fact]
    public async Task Missing_Interface_Name_Is_Not_Accepted_As_Trusted_Inventory()
    {
        var spy = new RouterSpy { MissingInterfaceName=true };
        await Assert.ThrowsAsync<InvalidDataException>(
            () => new RouterGateway(spy).ReadNetworkInventoryAsync());
    }

    [Fact]
    public async Task Unrecognized_State_Is_Unknown_Rather_Than_Falsely_Disabled()
    {
        var spy = new RouterSpy { UnknownRunning=true };
        var inventory=await new RouterGateway(spy).ReadNetworkInventoryAsync();
        Assert.Null(Assert.Single(inventory.Interfaces).Running);
    }

    [Fact]
    public async Task Actual_Authenticated_Tcp_Transport_Counts_Before_Neighbor_Detail_Read()
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);
        listener.Start();
        var port=((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(18));
        var server=Task.Run(async () =>
        {
            using var socket=await listener.AcceptTcpClientAsync(timeout.Token);
            using var stream=socket.GetStream();
            var login=await RouterOsProtocol.ReadSentenceAsync(stream,timeout.Token);
            Assert.Equal("/login",login[0]);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],timeout.Token);
            var cases=new (string Path,string Projection,string[] Reply)[]
            {
                ("/interface/print",".id,name,type,running,disabled,mac-address",
                    ["=.id=*1","=name=ether1","=type=ether","=running=yes","=disabled=no"]),
                ("/ip/address/print",".id,address,network,interface,disabled",
                    ["=.id=*2","=address=192.0.2.1/24","=network=192.0.2.0",
                        "=interface=ether1","=disabled=no"])
            };
            foreach(var item in cases)
            {
                var sentence=await RouterOsProtocol.ReadSentenceAsync(stream,timeout.Token);
                Assert.Equal(item.Path,sentence[0]);
                Assert.Contains("=.proplist="+item.Projection,sentence);
                Assert.Equal(2,sentence.Count);
                await RouterOsProtocol.WriteSentenceAsync(stream,["!re",..item.Reply],timeout.Token);
                await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],timeout.Token);
            }
            var countQuery=await RouterOsProtocol.ReadSentenceAsync(stream,timeout.Token);
            Assert.Equal("/ip/neighbor/print",countQuery[0]);
            Assert.Contains("=count-only=",countQuery);
            Assert.Equal(2,countQuery.Count);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done","=ret=1"],timeout.Token);

            var neighbors=await RouterOsProtocol.ReadSentenceAsync(stream,timeout.Token);
            Assert.Equal("/ip/neighbor/print",neighbors[0]);
            Assert.Contains("=.proplist=interface,address,address4,mac-address,identity,platform,version,board",
                neighbors);
            Assert.Equal(2,neighbors.Count);
            await RouterOsProtocol.WriteSentenceAsync(stream,
                ["!re","=identity=LiteBeam","=interface=ether1",
                    "=address=192.0.2.2","=address4=192.0.2.2","=version=8.7"],timeout.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],timeout.Token);
        },timeout.Token);
        await using(var client=await RouterOsClient.ConnectAsync(
            new RouterEndpoint("127.0.0.1",port,"admin","fake-password"),timeout.Token))
        {
            var result=await new RouterGateway(client).ReadNetworkInventoryAsync(timeout.Token);
            Assert.Single(result.Interfaces);
            Assert.Single(result.Addresses);
            Assert.Equal("LiteBeam",Assert.Single(result.Neighbors).Identity);
        }
        await server;
    }
}
