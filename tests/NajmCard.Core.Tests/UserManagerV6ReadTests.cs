using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class UserManagerV6ReadTests
{
    private sealed class FakeCommands : IRouterCommandClient
    {
        public readonly List<string> Calls = [];
        public bool Unsupported { get; set; }
        public Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> CommandAsync(
            string command, IReadOnlyDictionary<string, string>? attributes = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(command);
            if (Unsupported) throw new RouterOsTrapException("no such command");
            IReadOnlyList<IReadOnlyDictionary<string, string>> rows = command switch
            {
                "/tool/user-manager/profile/print" =>
                    [new Dictionary<string, string>
                    {
                        [".id"] = "*A", ["name"] = "gold-1gb", ["owner"] = "admin",
                        ["validity"] = "30d", ["price"] = "1500", ["starts-at"] = "logon"
                    }],
                "/tool/user-manager/customer/print" =>
                    [new Dictionary<string, string>
                    {
                        [".id"] = "*B", ["login"] = "admin", ["name"] = "Administrator"
                    }],
                _ => throw new InvalidOperationException("Wrong RouterOS 6 UM command: " + command)
            };
            return Task.FromResult(rows);
        }
    }

    [Fact]
    public async Task Reads_RouterOs6_UserManager_Profiles_Without_Using_RouterOs7_Path()
    {
        var api = new FakeCommands();
        var profiles = await new RouterGateway(api).ReadUserManagerV6ProfilesAsync();
        var profile = Assert.Single(profiles);
        Assert.Equal("gold-1gb", profile.Name);
        Assert.Equal("admin", profile.Owner);
        Assert.Equal("30d", profile.Validity);
        Assert.Equal("1500", profile.Price);
        Assert.Equal("logon", profile.StartsAt);
        Assert.Equal(["/tool/user-manager/profile/print"], api.Calls);
    }

    [Fact]
    public async Task Reads_RouterOs6_Customer_Logins_Without_Creating_Users()
    {
        var api = new FakeCommands();
        var customers = await new RouterGateway(api).ReadUserManagerV6CustomersAsync();
        var customer = Assert.Single(customers);
        Assert.Equal("admin", customer.Login);
        Assert.Equal("*B", customer.Id);
        Assert.Equal(["/tool/user-manager/customer/print"], api.Calls);
    }

    [Fact]
    public async Task Unsupported_UserManager_Must_Produce_An_Error_Not_A_False_Empty_List()
    {
        var api = new FakeCommands { Unsupported = true };
        await Assert.ThrowsAsync<RouterOsTrapException>(
            async () => await new RouterGateway(api).ReadUserManagerV6ProfilesAsync());
    }
}
