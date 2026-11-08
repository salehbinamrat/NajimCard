using System.Net;
using System.Net.Sockets;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>Actual TCP/RouterOsProtocol regression: !trap is followed by !done on RouterOS 6.</summary>
public sealed class RouterOsTrapDrainIntegrationTests
{
    [Fact]
    public async Task Trap_Consumes_Its_Done_Before_Next_Command_Begins()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(14));

        var server = Task.Run(async () =>
        {
            using var remote = await listener.AcceptTcpClientAsync(deadline.Token);
            using var stream = remote.GetStream();

            var login = await RouterOsProtocol.ReadSentenceAsync(stream, deadline.Token);
            Assert.Equal("/login", login[0]);
            await RouterOsProtocol.WriteSentenceAsync(stream, ["!done"], deadline.Token);

            var remove = await RouterOsProtocol.ReadSentenceAsync(stream, deadline.Token);
            Assert.Equal("/ip/hotspot/user/remove", remove[0]);
            Assert.Contains("=.id=*A", remove);
            await RouterOsProtocol.WriteSentenceAsync(stream,
                ["!trap", "=message=not-allowed"], deadline.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream, ["!done"], deadline.Token);

            var subsequent = await RouterOsProtocol.ReadSentenceAsync(stream, deadline.Token);
            Assert.Equal("/ip/hotspot/user/print", subsequent[0]);
            await RouterOsProtocol.WriteSentenceAsync(stream,
                ["!re", "=.id=*B", "=name=still-exists"], deadline.Token);
            await RouterOsProtocol.WriteSentenceAsync(stream, ["!done"], deadline.Token);
        }, deadline.Token);

        await using (var client = await RouterOsClient.ConnectAsync(
            new RouterEndpoint("127.0.0.1", port, "admin", "dummy-password"),
            deadline.Token))
        {
            var trap = await Assert.ThrowsAsync<RouterOsTrapException>(
                async () => await client.CommandAsync("/ip/hotspot/user/remove",
                    new Dictionary<string,string> { [".id"] = "*A" }, deadline.Token));
            Assert.Contains("not-allowed", trap.Message);
            var users = await client.CommandAsync("/ip/hotspot/user/print",
                cancellationToken: deadline.Token);
            Assert.Equal("still-exists", Assert.Single(users)["name"]);
        }
        await server;
    }
}
