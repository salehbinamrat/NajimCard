using System.Net;
using System.Net.Sockets;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class RouterSessionServiceTests
{
    [Theory]
    [InlineData("/ip/hotspot/user/print", true)]
    [InlineData("/ip/hotspot/user/remove", false)]
    public async Task BrokenTransportIsReplacedWithoutReplayingWrites(string command, bool isRead)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = deadline.Token;
        var endpoint = new RouterEndpoint("127.0.0.1",
            ((IPEndPoint)listener.LocalEndpoint).Port, "admin", "dummy-password");

        var server = Task.Run(async () =>
        {
            using (var first = await listener.AcceptTcpClientAsync(token))
            {
                var stream = first.GetStream();
                Assert.Equal("/login", (await RouterOsProtocol.ReadSentenceAsync(stream, token))[0]);
                await RouterOsProtocol.WriteSentenceAsync(stream, ["!done"], token);
                Assert.Equal(command, (await RouterOsProtocol.ReadSentenceAsync(stream, token))[0]);
                // Close the socket without the command's terminal sentence.
            }

            using var second = await listener.AcceptTcpClientAsync(token);
            var recovered = second.GetStream();
            Assert.Equal("/login", (await RouterOsProtocol.ReadSentenceAsync(recovered, token))[0]);
            await RouterOsProtocol.WriteSentenceAsync(recovered, ["!done"], token);
            Assert.Equal("/ip/hotspot/user/print",
                (await RouterOsProtocol.ReadSentenceAsync(recovered, token))[0]);
            await RouterOsProtocol.WriteSentenceAsync(recovered, ["!re", "=name=connected"], token);
            await RouterOsProtocol.WriteSentenceAsync(recovered, ["!done"], token);
        }, token);

        var session = await RouterSessionService.OpenAsync(endpoint, token);
        await using (session)
        {
            if (!isRead)
                await Assert.ThrowsAnyAsync<IOException>(() => session.CommandAsync(command,
                    new Dictionary<string, string> { [".id"] = "*A" }, token));

            var users = await session.CommandAsync("/ip/hotspot/user/print", cancellationToken: token);
            Assert.Equal("connected", Assert.Single(users)["name"]);
            await server;
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.CommandAsync("/ip/hotspot/user/print", cancellationToken: token));
    }
}
