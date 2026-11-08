using System.Net;
using System.Net.Sockets;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>Real command-boundary tests for one-account delete and one-session disconnect.</summary>
public sealed class PppSingleDeleteAndDisconnectTests
{
    private static PppAccount Account() => new("*A", "00234", "gold", "pppoe", false, true, "Site A");
    private static PppActiveSession Session() => new("*B", "00234", "pppoe", "192.0.2.25", "AA:BB:CC:DD:EE:FF", "2h");
    private static IReadOnlyDictionary<string,string> Secret(string name="00234",string profile="gold",string service="pppoe") =>
        new Dictionary<string,string> { [".id"]="*A", ["name"]=name, ["profile"]=profile,
            ["service"]=service, ["disabled"]="no" };
    private static IReadOnlyDictionary<string,string> Active(string address="192.0.2.25",string caller="AA:BB:CC:DD:EE:FF") =>
        new Dictionary<string,string> { [".id"]="*B", ["name"]="00234", ["service"]="pppoe",
            ["address"]=address, ["caller-id"]=caller, ["uptime"]="3h" };
    private sealed class Spy : IRouterCommandClient, IRouterQueryClient
    {
        public List<string> Calls = [];
        public IReadOnlyList<IReadOnlyDictionary<string,string>> Secrets = [Secret()];
        public IReadOnlyList<IReadOnlyDictionary<string,string>> Sessions = [Active()];
        public bool LostReply;
        public bool DefiniteTrap;
        public bool IgnoreWrite;
        public bool PermissionDenied;
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByIdAsync(
            string path,string id,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Calls.Add(path+" ?.id="+id);
            if (PermissionDenied) throw new RouterOsTrapException("not enough permissions");
            if (path=="/ppp/secret/print")
            {
                Assert.Equal("*A",id);
                return Task.FromResult(Secrets);
            }
            if (path=="/ppp/active/print")
            {
                Assert.Equal("*B",id);
                return Task.FromResult(Sessions);
            }
            throw new InvalidOperationException(path);
        }
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string path,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Calls.Add(path);
            if (path is not ("/ppp/secret/remove" or "/ppp/active/remove"))
                throw new InvalidOperationException("Unexpected command: "+path);
            Assert.Equal(path=="/ppp/secret/remove"?"*A":"*B",attributes![".id"]);
            Assert.Single(attributes);
            if (DefiniteTrap) throw new RouterOsTrapException("not permitted");
            if (LostReply) throw new IOException("connection lost after write");
            if (!IgnoreWrite)
            {
                if (path=="/ppp/secret/remove") Secrets=[];
                else Sessions=[];
            }
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>([]);
        }
    }

    [Fact]
    public async Task Delete_One_Selected_Secret_Uses_Exact_Id_Preflight_Write_And_Verified_Absence()
    {
        var spy=new Spy();
        var result=await new RouterGateway(spy).DeletePppAccountAsync(Account());
        Assert.True(result.Success);
        Assert.False(result.UncertainOutcome);
        Assert.Equal([
            "/ppp/secret/print ?.id=*A", "/ppp/secret/remove",
            "/ppp/secret/print ?.id=*A"
        ], spy.Calls);
    }

    [Fact]
    public async Task Disconnect_One_Selected_Ppp_Session_Uses_Exact_Id_And_Identity_Fields()
    {
        var spy=new Spy();
        var result=await new RouterGateway(spy).DisconnectPppSessionAsync(Session());
        Assert.True(result.Success);
        Assert.Equal([
            "/ppp/active/print ?.id=*B", "/ppp/active/remove",
            "/ppp/active/print ?.id=*B"
        ], spy.Calls);
    }

    [Theory]
    [InlineData("new-user","gold","pppoe")]
    [InlineData("00234","changed","pppoe")]
    [InlineData("00234","gold","l2tp")]
    public async Task Changed_Secret_Identity_Never_Deletes(string username,string profile,string service)
    {
        var spy=new Spy { Secrets=[Secret(username,profile,service)] };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RouterGateway(spy).DeletePppAccountAsync(Account()));
        Assert.Single(spy.Calls);
    }

    [Theory]
    [InlineData("192.0.2.99","AA:BB:CC:DD:EE:FF")]
    [InlineData("192.0.2.25","DIFFERENT-MAC")]
    public async Task Changed_Session_Identity_Never_Disconnects(string ip,string caller)
    {
        var spy=new Spy { Sessions=[Active(ip,caller)] };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RouterGateway(spy).DisconnectPppSessionAsync(Session()));
        Assert.Single(spy.Calls);
    }

    [Fact]
    public async Task Permission_Denied_Before_Write_Fails_Closed()
    {
        var spy=new Spy { PermissionDenied=true };
        await Assert.ThrowsAsync<RouterOsTrapException>(() =>
            new RouterGateway(spy).DeletePppAccountAsync(Account()));
        await Assert.ThrowsAsync<RouterOsTrapException>(() =>
            new RouterGateway(spy).DisconnectPppSessionAsync(Session()));
        Assert.DoesNotContain(spy.Calls,x=>x.EndsWith("/remove"));
    }

    [Theory]
    [InlineData(false,true)]
    [InlineData(true,false)]
    public async Task Lost_Response_Or_Unconfirmed_Readback_Is_Uncertain(bool lost,bool ignored)
    {
        var spy=new Spy { LostReply=lost,IgnoreWrite=ignored };
        var account=await new RouterGateway(spy).DeletePppAccountAsync(Account());
        Assert.False(account.Success);
        Assert.True(account.UncertainOutcome);
        Assert.Equal(lost?2:3,spy.Calls.Count);
    }

    [Fact]
    public async Task Exact_Id_Queries_Are_Required_No_Unfiltered_Fallback()
    {
        var fake=new ReadOnlyAdapter();
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            new RouterGateway(fake).DeletePppAccountAsync(Account()));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            new RouterGateway(fake).DisconnectPppSessionAsync(Session()));
        Assert.Empty(fake.Calls);
    }

    private sealed class ReadOnlyAdapter : IRouterCommandClient
    {
        public List<string> Calls = [];
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string path,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Calls.Add(path);
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>([]);
        }
    }

    [Fact]
    public async Task Real_Tcp_Ppp_Session_Disconnect_Queries_Only_Selected_Session()
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);
        listener.Start();
        int port=((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var server=Task.Run(async () =>
        {
            using var remote=await listener.AcceptTcpClientAsync(timeout.Token);
            using var stream=remote.GetStream();
            Assert.Equal("/login",(await RouterOsProtocol.ReadSentenceAsync(stream,timeout.Token))[0]);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],timeout.Token);
            var first=await RouterOsProtocol.ReadSentenceAsync(stream,timeout.Token);
            Assert.Equal("/ppp/active/print",first[0]);
            Assert.Contains("?.id=*B",first);
            await RouterOsProtocol.WriteSentenceAsync(stream,
                ["!re","=.id=*B","=name=00234","=service=pppoe",
                 "=address=192.0.2.25","=caller-id=AA:BB:CC:DD:EE:FF"],timeout.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],timeout.Token);
            var remove=await RouterOsProtocol.ReadSentenceAsync(stream,timeout.Token);
            Assert.Equal("/ppp/active/remove",remove[0]);
            Assert.Equal("=.id=*B",remove[1]);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],timeout.Token);
            var after=await RouterOsProtocol.ReadSentenceAsync(stream,timeout.Token);
            Assert.Equal("/ppp/active/print",after[0]);
            Assert.Contains("?.id=*B",after);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],timeout.Token);
        },timeout.Token);
        await using(var connection=await RouterOsClient.ConnectAsync(
            new RouterEndpoint("127.0.0.1",port,"admin","test"),timeout.Token))
        {
            var result=await new RouterGateway(connection).DisconnectPppSessionAsync(
                Session(),timeout.Token);
            Assert.True(result.Success);
        }
        await server;
    }
}
