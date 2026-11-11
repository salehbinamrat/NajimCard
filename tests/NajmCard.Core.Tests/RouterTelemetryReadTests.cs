using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class RouterTelemetryReadTests
{
    private sealed class Client : IRouterCommandClient
    {
        public readonly List<string> Calls = [];
        public string Name = "MAIN";
        public int Cpu;
        public bool Fail;
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command, IReadOnlyDictionary<string,string>? attributes = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(command);
            if (Fail) throw new IOException("connection lost");
            IReadOnlyDictionary<string,string> row = command.Contains("identity")
                ? new Dictionary<string,string> { ["name"] = Name }
                : new Dictionary<string,string> { ["cpu-load"] = (++Cpu).ToString(), ["uptime"] = Cpu+"s" };
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>([row]);
        }
    }

    [Fact]
    public async Task Repeated_Telemetry_Reads_Fetch_Fresh_Resource_With_One_Command_Each()
    {
        var client = new Client();
        var gateway = new RouterGateway(client);
        Assert.Equal("MAIN", (await gateway.ReadResourceAsync()).Identity);
        client.Calls.Clear();
        Assert.Equal("2", (await gateway.ReadTelemetryResourceAsync()).CpuLoad);
        Assert.Equal("3", (await gateway.ReadTelemetryResourceAsync()).CpuLoad);
        Assert.Equal(["/system/resource/print", "/system/resource/print"], client.Calls);
        client.Name = "RENAMED";
        Assert.Equal("RENAMED", (await gateway.ReadResourceAsync()).Identity);
        client.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => gateway.ReadTelemetryResourceAsync());
    }
}
