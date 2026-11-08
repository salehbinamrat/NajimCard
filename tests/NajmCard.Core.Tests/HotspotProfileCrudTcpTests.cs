using System.Net;
using System.Net.Sockets;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>Real RouterOS 6 TCP API sentence integration for Hotspot profile creation.
/// Uses a loopback protocol peer, never a production router.</summary>
public sealed class HotspotProfileCrudTcpTests
{
    [Fact]
    public async Task Add_Profile_Uses_Real_Done_Receipt_And_Exact_Id_Readback()
    {
        using var listener=new TcpListener(IPAddress.Loopback,0);
        listener.Start();
        int port=((IPEndPoint)listener.LocalEndpoint).Port;
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var server=Task.Run(async()=>
        {
            using var peer=await listener.AcceptTcpClientAsync(deadline.Token);
            using var stream=peer.GetStream();
            var login=await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            Assert.Equal("/login",login[0]);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);

            var preflight=await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            Assert.Equal("/ip/hotspot/user/profile/print",preflight[0]);
            Assert.Contains("=.proplist=.id,name",preflight);
            Assert.DoesNotContain(preflight,w=>w.StartsWith("?.id=",StringComparison.Ordinal));
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);

            var add=await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            Assert.Equal("/ip/hotspot/user/profile/add",add[0]);
            Assert.Contains("=name=gold",add);
            Assert.Contains("=rate-limit=2M/512k",add);
            Assert.Contains("=session-timeout=1h",add);
            Assert.Contains("=shared-users=1",add);
            Assert.Equal(5,add.Count);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done","=ret=*A"],deadline.Token);

            var check=await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            Assert.Equal("/ip/hotspot/user/profile/print",check[0]);
            Assert.Contains("?.id=*A",check);
            Assert.Contains("=.proplist=.id,name,rate-limit,session-timeout,shared-users",check);
            Assert.Equal(3,check.Count);
            await RouterOsProtocol.WriteSentenceAsync(stream,
                ["!re","=.id=*A","=name=gold","=rate-limit=2M/512k",
                "=session-timeout=1h","=shared-users=1"],deadline.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);
        },deadline.Token);

        await using(var client=await RouterOsClient.ConnectAsync(
            new RouterEndpoint("127.0.0.1",port,"admin","loopback-test"),deadline.Token))
        {
            var result=await new RouterGateway(client).SaveHotspotProfileAsync(
                new HotspotProfileDraft("gold","2M/512k","1h","1"),null,deadline.Token);
            Assert.True(result.Success,result.Diagnostic);
            Assert.False(result.UncertainOutcome);
            Assert.Equal("*A",result.ProfileId);
        }
        await server;
    }
}
