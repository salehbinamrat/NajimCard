using System.Net;
using System.Net.Sockets;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>
/// PPP secret changes must use a selected, freshly verified exact RouterOS
/// internal ID, never a username or blind unfiltered update.
/// </summary>
public sealed class PppSelectedAccountActionTests
{
    private static PppAccount Selected(bool disabled=false) =>
        new("*A", "001234", "gold", "pppoe", disabled, false, "Site Alpha");
    private static IReadOnlyDictionary<string,string> Row(
        string id="*A",string name="001234",string profile="gold",
        string service="pppoe",bool disabled=false) =>
        new Dictionary<string,string>
        {
            [".id"]=id, ["name"]=name, ["profile"]=profile,
            ["service"]=service, ["disabled"]=disabled?"yes":"no"
        };

    private sealed class Spy : IRouterCommandClient, IRouterQueryClient
    {
        public List<string> Calls {get;}=[];
        public IReadOnlyList<IReadOnlyDictionary<string,string>> Rows {get;set;}=[Row()];
        public bool DeniedRead {get;set;}
        public bool DropAfterWrite {get;set;}
        public bool TrapOnWrite {get;set;}
        public bool IgnoreWrite {get;set;}

        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByIdAsync(
            string command,string id,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Calls.Add(command+" ?.id="+id);
            if(DeniedRead) throw new RouterOsTrapException("not enough permissions");
            Assert.Equal("/ppp/secret/print",command);
            Assert.Equal("*A",id);
            Assert.Equal(".id,name,profile,service,disabled",attributes![".proplist"]);
            return Task.FromResult(Rows);
        }

        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Calls.Add(command);
            Assert.Equal("/ppp/secret/set",command);
            Assert.Equal("*A",attributes![".id"]);
            Assert.Equal(2,attributes.Count);
            if(TrapOnWrite) throw new RouterOsTrapException("not enough permissions");
            if(DropAfterWrite) throw new IOException("reply lost after sending");
            if(!IgnoreWrite)
                Rows=[Row(disabled:attributes["disabled"]=="yes")];
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>([]);
        }
    }

    [Fact]
    public async Task Disable_Rechecks_Exact_Id_Then_Confirms_Actual_New_Status()
    {
        var spy=new Spy();
        var result=await new RouterGateway(spy).SetPppAccountDisabledAsync(Selected(),true);
        Assert.True(result.Success);
        Assert.False(result.UncertainOutcome);
        Assert.Equal([
            "/ppp/secret/print ?.id=*A",
            "/ppp/secret/set",
            "/ppp/secret/print ?.id=*A"
        ],spy.Calls);
    }

    [Fact]
    public async Task Enable_Uses_The_Same_Targeted_And_Verified_Sequence()
    {
        var spy=new Spy {Rows=[Row(disabled:true)]};
        var result=await new RouterGateway(spy).SetPppAccountDisabledAsync(Selected(true),false);
        Assert.True(result.Success);
        Assert.Equal(3,spy.Calls.Count);
    }

    [Theory]
    [InlineData("another","gold","pppoe")]
    [InlineData("001234","silver","pppoe")]
    [InlineData("001234","gold","l2tp")]
    public async Task Stale_Identity_Profile_Or_Service_Must_Not_Be_Written(
        string username,string profile,string service)
    {
        var spy=new Spy {Rows=[Row(name:username,profile:profile,service:service)]};
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            new RouterGateway(spy).SetPppAccountDisabledAsync(Selected(),true));
        Assert.Single(spy.Calls);
    }

    [Fact]
    public async Task Missing_Or_Duplicate_Exact_Id_And_Read_Denial_Fail_Closed()
    {
        var spy=new Spy{Rows=[]};
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            new RouterGateway(spy).SetPppAccountDisabledAsync(Selected(),true));
        spy.Rows=[Row(),Row()];
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            new RouterGateway(spy).SetPppAccountDisabledAsync(Selected(),true));
        spy.DeniedRead=true;
        await Assert.ThrowsAsync<RouterOsTrapException>(() =>
            new RouterGateway(spy).SetPppAccountDisabledAsync(Selected(),true));
        Assert.All(spy.Calls,x=>Assert.Contains("/print",x));
    }

    [Fact]
    public async Task Lost_Reply_Is_Uncertain_Without_Any_Automatic_Retry()
    {
        var spy=new Spy{DropAfterWrite=true};
        var result=await new RouterGateway(spy).SetPppAccountDisabledAsync(Selected(),true);
        Assert.False(result.Success);
        Assert.True(result.UncertainOutcome);
        Assert.Equal(2,spy.Calls.Count);
    }

    [Fact]
    public async Task Definite_Trap_Is_Reported_As_Failed_Rather_Than_Confirmed_Change()
    {
        var spy=new Spy{TrapOnWrite=true};
        var result=await new RouterGateway(spy).SetPppAccountDisabledAsync(Selected(),true);
        Assert.False(result.Success);
        Assert.False(result.UncertainOutcome);
        Assert.Equal(2,spy.Calls.Count);
    }

    [Fact]
    public async Task Successful_Write_Without_Confirmed_Readback_Remains_Uncertain()
    {
        var spy=new Spy{IgnoreWrite=true};
        var result=await new RouterGateway(spy).SetPppAccountDisabledAsync(Selected(),true);
        Assert.False(result.Success);
        Assert.True(result.UncertainOutcome);
    }

    [Fact]
    public async Task Read_Only_Adapter_Without_Server_Side_Exact_Id_Query_Is_Rejected()
    {
        var client=new ReadOnlyAdapter();
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            new RouterGateway(client).SetPppAccountDisabledAsync(Selected(),true));
        Assert.Empty(client.Calls);
    }

    private sealed class ReadOnlyAdapter : IRouterCommandClient
    {
        public readonly List<string> Calls=[];
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Calls.Add(command);
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>([]);
        }
    }

    [Fact]
    public async Task Ppp_Set_Uses_Targeted_RouterOs_Tcp_Read_Write_Read_Not_Unfiltered_Commands()
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);
        listener.Start();
        int port=((IPEndPoint)listener.LocalEndpoint).Port;
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(16));
        var server=Task.Run(async () =>
        {
            using var remote=await listener.AcceptTcpClientAsync(deadline.Token);
            using var stream=remote.GetStream();
            Assert.Equal("/login",(await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token))[0]);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);

            var before=await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            Assert.Equal("/ppp/secret/print",before[0]);
            Assert.Contains("?.id=*A",before);
            await RouterOsProtocol.WriteSentenceAsync(stream,
                ["!re","=.id=*A","=name=001234","=profile=gold",
                 "=service=pppoe","=disabled=no"],deadline.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);

            var write=await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            Assert.Equal("/ppp/secret/set",write[0]);
            Assert.Contains("=.id=*A",write);
            Assert.Contains("=disabled=yes",write);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);

            var after=await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            Assert.Equal("/ppp/secret/print",after[0]);
            Assert.Contains("?.id=*A",after);
            await RouterOsProtocol.WriteSentenceAsync(stream,
                ["!re","=.id=*A","=name=001234","=profile=gold",
                 "=service=pppoe","=disabled=yes"],deadline.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);
        },deadline.Token);
        await using(var client=await RouterOsClient.ConnectAsync(
            new RouterEndpoint("127.0.0.1",port,"admin","test"),deadline.Token))
        {
            var result=await new RouterGateway(client).SetPppAccountDisabledAsync(
                Selected(),true,deadline.Token);
            Assert.True(result.Success);
        }
        await server;
    }
}
