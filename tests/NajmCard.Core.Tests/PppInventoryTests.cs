using System.Net;
using System.Net.Sockets;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>
/// RouterOS 6 PPP secrets, PPP profiles and currently active sessions:
/// read-only, authenticated RouterGateway and real TCP transport boundaries.
/// </summary>
public sealed class PppInventoryTests
{
    private sealed class FakePpp : IRouterCommandClient
    {
        public readonly List<string> Commands = [];
        public string? TrapOn { get; set; }
        public Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> CommandAsync(
            string command, IReadOnlyDictionary<string, string>? attributes = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);
            if (command == TrapOn) throw new RouterOsTrapException("not enough permissions");
            IReadOnlyList<IReadOnlyDictionary<string, string>> rows = command switch
            {
                "/ppp/secret/print" =>
                [
                    new Dictionary<string,string>
                    {
                        [".id"]="*A", ["name"]="001234", ["profile"]="gold",
                        ["service"]="pppoe", ["disabled"]="no", ["comment"]="Site Alpha",
                        ["password"]="private password must never reach the UI"
                    },
                    new Dictionary<string,string>
                    {
                        [".id"]="*B", ["name"]="009876", ["profile"]="silver",
                        ["service"]="any", ["disabled"]="yes"
                    },
                    new Dictionary<string,string>
                    {
                        [".id"]="*C", ["name"]="mobile", ["profile"]="default",
                        ["service"]="l2tp", ["disabled"]="false"
                    }
                ],
                "/ppp/profile/print" =>
                [
                    new Dictionary<string,string>
                    {
                        [".id"]="*D", ["name"]="gold", ["rate-limit"]="8M/8M",
                        ["local-address"]="192.0.2.1", ["remote-address"]="ppp-pool"
                    }
                ],
                "/ppp/active/print" =>
                [
                    new Dictionary<string,string>
                    {
                        [".id"]="*E", ["name"]="001234", ["service"]="pppoe",
                        ["address"]="192.0.2.25", ["caller-id"]="AA:BB:CC:DD:EE:FF",
                        ["uptime"]="2h12m"
                    },
                    new Dictionary<string,string>
                    {
                        [".id"]="*F", ["name"]="unknown", ["service"]="pppoe",
                        ["address"]="192.0.2.39", ["uptime"]="12m"
                    }
                ],
                _ => throw new InvalidOperationException("Unexpected RouterOS API request " + command)
            };
            return Task.FromResult(rows);
        }
    }

    [Fact]
    public async Task Reads_All_Three_Real_Ppp_Menus_And_Joins_Sessions_By_Account()
    {
        var spy = new FakePpp();
        var inventory = await new RouterGateway(spy).ReadPppInventoryAsync();
        Assert.Equal(3, inventory.Accounts.Count);
        Assert.Equal(2, inventory.ActiveSessions.Count);
        Assert.Single(inventory.Profiles);
        Assert.Equal("gold", inventory.Profiles[0].Name);
        Assert.Equal("8M/8M", inventory.Profiles[0].RateLimit);
        var account = Assert.Single(inventory.Accounts.Where(x => x.Name == "001234"));
        Assert.True(account.Online);
        Assert.False(account.Disabled);
        Assert.Equal("pppoe", account.Service);
        Assert.Equal("Site Alpha", account.Comment);
        Assert.True(Assert.Single(inventory.Accounts.Where(x => x.Name == "009876")).Disabled);
        Assert.False(Assert.Single(inventory.Accounts.Where(x => x.Name == "mobile")).Online);
        Assert.Equal("192.0.2.25",
            Assert.Single(inventory.ActiveSessions.Where(x => x.Name == "001234")).Address);
        Assert.Equal([
            "/ppp/secret/print", "/ppp/profile/print", "/ppp/active/print"
        ], spy.Commands);
        Assert.DoesNotContain("private password", account.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Creator_Profile_Read_Does_Not_Fetch_Accounts_Or_Sessions()
    {
        var spy=new FakePpp();
        var profile=Assert.Single(await new RouterGateway(spy).ReadPppProfilesAsync());
        Assert.Equal("gold",profile.Name);
        Assert.Equal(["/ppp/profile/print"],spy.Commands);
    }

    [Theory]
    [InlineData("/ppp/secret/print")]
    [InlineData("/ppp/profile/print")]
    [InlineData("/ppp/active/print")]
    public async Task Permission_Denial_Fails_Closed_Without_Reporting_An_Empty_Inventory(string menu)
    {
        var spy = new FakePpp { TrapOn = menu };
        await Assert.ThrowsAsync<RouterOsTrapException>(
            async () => await new RouterGateway(spy).ReadPppInventoryAsync());
        Assert.DoesNotContain(spy.Commands, c => c.EndsWith("/add") ||
            c.EndsWith("/set") || c.EndsWith("/remove"));
    }

    [Fact]
    public async Task Read_Only_RouterOs6_Ppp_Inventory_Traverses_Actual_Tcp_Protocol()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(16));
        var server = Task.Run(async () =>
        {
            using var remote = await listener.AcceptTcpClientAsync(deadline.Token);
            using var stream = remote.GetStream();
            var login = await RouterOsProtocol.ReadSentenceAsync(stream, deadline.Token);
            Assert.Equal("/login", login[0]);
            await RouterOsProtocol.WriteSentenceAsync(stream, ["!done"], deadline.Token);
            foreach (var (path, fields) in new[]
            {
                ("/ppp/secret/print", new[] {"=.id=*A","=name=001234","=profile=gold","=service=pppoe","=disabled=false"}),
                ("/ppp/profile/print", new[] {"=.id=*B","=name=gold","=rate-limit=8M/8M"}),
                ("/ppp/active/print", new[] {"=.id=*C","=name=001234","=service=pppoe","=uptime=1m"})
            })
            {
                var command = await RouterOsProtocol.ReadSentenceAsync(stream, deadline.Token);
                Assert.Equal(path, command[0]);
                Assert.Single(command);
                await RouterOsProtocol.WriteSentenceAsync(stream,
                    ["!re", .. fields], deadline.Token);
                await RouterOsProtocol.WriteSentenceAsync(stream, ["!done"], deadline.Token);
            }
        }, deadline.Token);

        await using (var client = await RouterOsClient.ConnectAsync(
            new RouterEndpoint("127.0.0.1", port, "admin", "test"),
            deadline.Token))
        {
            var report = await new RouterGateway(client).ReadPppInventoryAsync(deadline.Token);
            Assert.True(Assert.Single(report.Accounts).Online);
            Assert.Equal("001234", Assert.Single(report.ActiveSessions).Name);
        }
        await server;
    }
}
