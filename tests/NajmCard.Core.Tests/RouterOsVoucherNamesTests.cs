using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>Cross-service name preflight from Hadramout NetworkGateway.voucher_names.</summary>
public sealed class RouterOsVoucherNamesTests
{
    private sealed class RouterNames : IRouterCommandClient
    {
        public readonly List<(string Path, bool Projected)> Calls = [];
        public string? UmTrap { get; set; }
        public bool BrokenProjection { get; set; }
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command, IReadOnlyDictionary<string,string>? attributes = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((command, attributes is not null));
            if (command == "/tool/user-manager/user/print" && UmTrap is not null)
                throw new RouterOsTrapException(UmTrap);
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows = command switch
            {
                "/ip/hotspot/user/print" =>
                    [new Dictionary<string,string> { ["name"]="hotspot01" }],
                "/ppp/secret/print" =>
                    [new Dictionary<string,string> { ["name"]="pppoe01" }],
                "/tool/user-manager/user/print" when BrokenProjection && attributes is not null =>
                    [new Dictionary<string,string> { [".id"]="*A" }],
                "/tool/user-manager/user/print" =>
                    [new Dictionary<string,string> { ["username"]="um01" }],
                _ => throw new InvalidOperationException("Unexpected service " + command)
            };
            return Task.FromResult(rows);
        }
    }

    [Fact]
    public async Task Reserves_Exactly_All_Three_Installed_Services_Before_Voucher_Creation()
    {
        var fake = new RouterNames();
        var names = await new RouterGateway(fake).ReadExistingVoucherNamesAsync();
        Assert.Equal(3, names.Count);
        Assert.Contains("hotspot01",names);
        Assert.Contains("pppoe01",names);
        Assert.Contains("um01",names);
        Assert.Equal([
            "/ip/hotspot/user/print",
            "/ppp/secret/print",
            "/tool/user-manager/user/print"
        ],fake.Calls.Select(x=>x.Path));
    }

    [Fact]
    public async Task Only_A_Confirmed_Missing_UserManager_Menu_Can_Be_Skipped()
    {
        var fake = new RouterNames { UmTrap="no such command" };
        var names = await new RouterGateway(fake).ReadExistingVoucherNamesAsync();
        Assert.Equal(2, names.Count);
        Assert.Contains("pppoe01",names);
    }

    [Fact]
    public async Task Unauthorized_UserManager_Read_Rejects_Even_If_Other_Services_Succeed()
    {
        var fake = new RouterNames { UmTrap="not enough permissions (9)" };
        await Assert.ThrowsAsync<RouterOsTrapException>(() =>
            new RouterGateway(fake).ReadExistingVoucherNamesAsync());
    }

    [Fact]
    public async Task Missing_Username_Projection_Falls_Back_To_Full_Read_Once()
    {
        var fake = new RouterNames { BrokenProjection=true };
        var names = await new RouterGateway(fake).ReadExistingVoucherNamesAsync();
        Assert.Contains("um01",names);
        Assert.Equal(2, fake.Calls.Count(x=>x.Path=="/tool/user-manager/user/print"));
        Assert.True(fake.Calls[^2].Projected);
        Assert.False(fake.Calls[^1].Projected);
    }
}
