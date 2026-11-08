using System.Net;
using System.Net.Sockets;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>Actual TCP serialization of a RouterOS 6 targeted read for selected accounts.</summary>
public sealed class RouterOsExactIdQueryTcpTests
{
    [Fact]
    public async Task Targeted_Query_Sends_Exactly_One_Internal_Id_Not_An_Unfiltered_User_List()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var server=Task.Run(async () =>
        {
            using var remote=await listener.AcceptTcpClientAsync(deadline.Token);
            using var stream=remote.GetStream();
            var login=await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            Assert.Equal("/login",login[0]);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);

            var request=await RouterOsProtocol.ReadSentenceAsync(stream,deadline.Token);
            Assert.Equal("/tool/user-manager/user/print",request[0]);
            Assert.Contains("=.proplist=.id,username,disabled,actual-profile",request);
            Assert.Contains("?.id=*A",request);
            Assert.Single(request.Where(x=>x.StartsWith("?.id=",StringComparison.Ordinal)));
            Assert.Equal(3,request.Count);

            await RouterOsProtocol.WriteSentenceAsync(stream,
                ["!re","=.id=*A","=username=001234","=disabled=no","=actual-profile=gold"],
                deadline.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream,["!done"],deadline.Token);
        },deadline.Token);

        await using(var client=await RouterOsClient.ConnectAsync(
            new RouterEndpoint("127.0.0.1",port,"admin","test-secret"),deadline.Token))
        {
            await Assert.ThrowsAnyAsync<ArgumentException>(() =>
                client.QueryByIdAsync("/tool/user-manager/user/print","*A,*B",
                    cancellationToken:deadline.Token));
            var records=await client.QueryByIdAsync("/tool/user-manager/user/print","*A",
                new Dictionary<string,string>
                { [".proplist"]=".id,username,disabled,actual-profile" },deadline.Token);
            var user=Assert.Single(records);
            Assert.Equal("*A",user[".id"]);
            Assert.Equal("001234",user["username"]);
        }
        await server;
    }
}
