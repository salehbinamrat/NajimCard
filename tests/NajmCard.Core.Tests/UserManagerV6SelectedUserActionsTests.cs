using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>RouterOS v6 single-user actions must reread exact .id and username before writing.</summary>
public sealed class UserManagerV6SelectedUserActionsTests
{
    private static UserManagerV6Voucher Selected(string name = "00127", string id = "*A",
        string profile = "gold", bool disabled = false) =>
        new(id, name, profile, disabled ? "disabled" : "active", "",
            "", 15, 4, 100, true, disabled, false);

    private static IReadOnlyDictionary<string, string> Row(string id = "*A",
        string name = "00127", string profile = "gold", bool disabled = false) =>
        new Dictionary<string,string>
        {
            [".id"] = id,
            ["username"] = name,
            ["actual-profile"] = profile,
            ["disabled"] = disabled ? "true" : "false"
        };

    private sealed class Spy : IRouterCommandClient, IRouterQueryClient
    {
        public List<string> Commands = [];
        public int Queries { get; private set; }
        public IReadOnlyList<IReadOnlyDictionary<string,string>> Rows { get; set; } = [Row()];
        public bool DropAfterWrite { get; set; }
        public bool PermissionDenied { get; set; }
        public bool DeleteOnRemove { get; set; } = true;

        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByIdAsync(
            string command, string id, IReadOnlyDictionary<string,string>? attributes = null,
            CancellationToken cancellationToken = default)
        {
            Queries++;
            Commands.Add(command + " ?.id=" + id);
            if (PermissionDenied) throw new RouterOsTrapException("not enough permissions");
            Assert.Equal("/tool/user-manager/user/print", command);
            Assert.Equal("*A", id);
            return Task.FromResult(Rows);
        }

        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command, IReadOnlyDictionary<string,string>? attributes = null,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            if (DropAfterWrite) throw new IOException("lost reply after write");
            if (command == "/tool/user-manager/user/set")
            {
                Assert.Equal("*A", attributes![".id"]);
                Assert.Equal("yes", attributes["disabled"]);
                Rows = [Row(disabled:true)];
            }
            else if (command == "/tool/user-manager/user/remove")
            {
                Assert.Equal("*A", attributes![".id"]);
                if (DeleteOnRemove) Rows = [];
            }
            else throw new InvalidOperationException(command);
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>([]);
        }
    }

    [Fact]
    public async Task Disable_Uses_Exact_Id_With_Query_Then_Confirms_After_Write()
    {
        var spy = new Spy();
        var result = await new RouterGateway(spy).ChangeUserManagerV6UserAsync(Selected(), "disable");
        Assert.True(result.Success);
        Assert.False(result.UncertainOutcome);
        Assert.Equal([
            "/tool/user-manager/user/print ?.id=*A",
            "/tool/user-manager/user/set",
            "/tool/user-manager/user/print ?.id=*A"
        ], spy.Commands);
    }

    [Fact]
    public async Task Changed_Username_Or_Profile_Blocks_All_Writes()
    {
        var spy = new Spy { Rows = [Row(name:"another")] };
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            new RouterGateway(spy).ChangeUserManagerV6UserAsync(Selected(), "remove"));
        Assert.Single(spy.Commands);
        spy.Rows=[Row(profile:"renewed")];
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            new RouterGateway(spy).ChangeUserManagerV6UserAsync(Selected(), "disable"));
        Assert.DoesNotContain(spy.Commands, x => x.EndsWith("/set") || x.EndsWith("/remove"));
    }

    [Fact]
    public async Task Duplicate_Or_Missing_Record_Always_Fails_Closed()
    {
        var spy = new Spy { Rows=[Row(),Row()] };
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            new RouterGateway(spy).ChangeUserManagerV6UserAsync(Selected(), "remove"));
        spy.Rows=[];
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            new RouterGateway(spy).ChangeUserManagerV6UserAsync(Selected(), "remove"));
        Assert.All(spy.Commands, x => Assert.Contains("/print",x));
    }

    [Fact]
    public async Task Remove_Verifies_Exactly_The_Chosen_User_Disappeared()
    {
        var spy = new Spy();
        var result = await new RouterGateway(spy).ChangeUserManagerV6UserAsync(Selected(), "remove");
        Assert.True(result.Success);
        Assert.Equal(3,spy.Commands.Count);
        Assert.Equal("/tool/user-manager/user/remove",spy.Commands[1]);
    }

    [Fact]
    public async Task Remove_When_User_Still_Exists_Reports_Uncertain_Not_Success()
    {
        var spy = new Spy { DeleteOnRemove = false };
        var result = await new RouterGateway(spy).ChangeUserManagerV6UserAsync(Selected(), "remove");
        Assert.False(result.Success);
        Assert.True(result.UncertainOutcome);
        Assert.Equal(3,spy.Commands.Count);
    }

    [Fact]
    public async Task Lost_Reply_After_Write_Is_Uncertain_And_Never_Retried()
    {
        var spy = new Spy { DropAfterWrite = true };
        var result = await new RouterGateway(spy).ChangeUserManagerV6UserAsync(Selected(), "disable");
        Assert.False(result.Success);
        Assert.True(result.UncertainOutcome);
        Assert.Equal(2,spy.Commands.Count);
    }

    [Fact]
    public async Task Missing_Query_Support_Or_Denied_Read_Prevents_Write()
    {
        var spy = new Spy { PermissionDenied = true };
        await Assert.ThrowsAsync<RouterOsTrapException>(() =>
            new RouterGateway(spy).ChangeUserManagerV6UserAsync(Selected(), "disable"));
        Assert.Single(spy.Commands);
    }

    [Theory]
    [InlineData("set")]
    [InlineData("disconnect")]
    [InlineData("bulk-remove")]
    public async Task Unsupported_Actions_Are_Rejected_Before_Router_Read(string action)
    {
        var spy = new Spy();
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            new RouterGateway(spy).ChangeUserManagerV6UserAsync(Selected(), action));
        Assert.Empty(spy.Commands);
    }
}
