using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class ExpiredCardDeletionTests
{
    private static HotspotUser User(string id, string name, long used, long limit) =>
        new(id, name, "", "default", false, used, 0, limit, "");

    private static IReadOnlyDictionary<string, string> Row(string id, string name, long used, long limit) =>
        new Dictionary<string, string>
        {
            [".id"] = id,
            ["name"] = name,
            ["password"] = "",
            ["profile"] = "default",
            ["disabled"] = "false",
            ["bytes-in"] = used.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["bytes-out"] = "0",
            ["limit-bytes-total"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["comment"] = ""
        };

    private sealed class StubRouter : IRouterCommandClient
    {
        public IReadOnlyList<IReadOnlyDictionary<string, string>> CurrentUsers { get; set; } = [];
        public List<string> Removed { get; } = [];
        public int ReadCount { get; private set; }
        public string? TrapId { get; set; }
        public string? DisconnectId { get; set; }

        public Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> CommandAsync(
            string command, IReadOnlyDictionary<string, string>? attributes = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (command == "/ip/hotspot/user/print")
            {
                ReadCount++;
                return Task.FromResult(CurrentUsers);
            }
            if (command == "/ip/hotspot/user/remove" && attributes is not null)
            {
                var id = attributes[".id"];
                if (id == TrapId) throw new RouterOsTrapException("User already removed");
                if (id == DisconnectId) throw new IOException("Connection dropped after remove command");
                Removed.Add(id);
                return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, string>>>([]);
            }
            throw new InvalidOperationException("Unexpected RouterOS command: " + command);
        }
    }

    [Fact]
    public async Task Batch_Deletes_Only_Confirmed_Transfer_Expired_Users_And_Skips_Renewed_Ones()
    {
        var router = new StubRouter
        {
            CurrentUsers =
            [
                Row("*1", "EXPIRED", 101, 100),
                Row("*2", "ACTIVE", 20, 100),
                Row("*3", "RENEWED", 5, 100),
                Row("*4", "UNLIMITED", 999, 0)
            ]
        };
        var before = new[]
        {
            User("*1", "EXPIRED", 101, 100),
            User("*2", "ACTIVE", 20, 100),
            User("*3", "RENEWED", 200, 100),
            User("*4", "UNLIMITED", 999, 0)
        };
        var result = await new RouterGateway(router).DeleteExpiredByTransferAsync(before);
        Assert.Equal(2, result.Requested);
        Assert.Equal(1, result.Deleted);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(result.Failed);
        Assert.False(result.UncertainOutcome);
        Assert.Equal(["*1"], router.Removed);
        Assert.Equal(1, router.ReadCount); // one fresh snapshot, not an N+1 read loop
    }

    [Fact]
    public async Task Batch_Continues_After_Router_Rejects_One_User_And_Reports_Failure()
    {
        var router = new StubRouter
        {
            CurrentUsers = [Row("*1", "A", 110, 100), Row("*2", "B", 120, 100)],
            TrapId = "*1"
        };
        var before = new[] { User("*1", "A", 110, 100), User("*2", "B", 120, 100) };
        var result = await new RouterGateway(router).DeleteExpiredByTransferAsync(before);
        Assert.Equal(2, result.Requested);
        Assert.Equal(1, result.Deleted);
        Assert.Equal(["A"], result.Failed);
        Assert.Equal(["*2"], router.Removed);
        Assert.False(result.UncertainOutcome);
    }

    [Fact]
    public async Task Batch_Stops_Without_Retry_On_Uncertain_Router_Write()
    {
        var router = new StubRouter
        {
            CurrentUsers = [Row("*1", "A", 110, 100), Row("*2", "B", 120, 100)],
            DisconnectId = "*1"
        };
        var before = new[] { User("*1", "A", 110, 100), User("*2", "B", 120, 100) };
        var result = await new RouterGateway(router).DeleteExpiredByTransferAsync(before);
        Assert.True(result.UncertainOutcome);
        Assert.Equal(2, result.Requested);
        Assert.Empty(router.Removed);
        Assert.Equal(0, result.Deleted);
    }

    [Fact]
    public async Task Batch_With_No_Transfer_Expired_Users_Performs_No_Network_IO()
    {
        var router = new StubRouter();
        var result = await new RouterGateway(router).DeleteExpiredByTransferAsync(
            [User("*1", "UNLIMITED", 2000, 0)]);
        Assert.Equal(0, result.Requested);
        Assert.Equal(0, router.ReadCount);
        Assert.Empty(router.Removed);
    }
}
