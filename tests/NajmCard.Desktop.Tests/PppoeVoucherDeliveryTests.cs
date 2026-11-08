using System.IO;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Desktop.Tests;

/// <summary>Real SQLite + DPAPI -> durable RouterOS PPPoE add + receipt state.</summary>
public sealed class PppoeVoucherDeliveryTests
{
    private static Voucher Card(string name,string service) =>
        new(name,"secret","gold","batch1",DateTimeOffset.UtcNow,Service:service);

    private sealed class Spy : IRouterCommandClient, IRouterReceiptClient
    {
        public readonly List<string> Writes=[];
        public bool MissingReceipt;
        public Task<RouterCommandReply> CommandWithReceiptAsync(
            string command,IReadOnlyDictionary<string,string>? attrs=null,
            CancellationToken ct=default)
        {
            Writes.Add(command);
            return Task.FromResult(new RouterCommandReply([],
                MissingReceipt ? new Dictionary<string,string>() :
                    new Dictionary<string,string>{{"ret","*A"}}));
        }
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attrs=null,
            CancellationToken ct=default)
        {
            if (!command.EndsWith("/print",StringComparison.Ordinal))
                throw new InvalidOperationException("No non-read PPPoE command expected");
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>([]);
        }
    }

    [Theory]
    [InlineData(false,"SENT")]
    [InlineData(true,"UNKNOWN")]
    public async Task Pppoe_Batch_Marks_Only_That_Service_With_Correct_State(bool missing,string state)
    {
        string root=Path.Combine(Path.GetTempPath(),"najm-pppoe-test",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store=new VoucherStore(root);
            store.SaveBatch([Card("001234","hotspot"),Card("001234","pppoe")]);
            var client=new Spy {MissingReceipt=missing};
            var response=await new VoucherBatchDispatcher(new RouterGateway(client),store)
                .SendAsync([Card("001234","pppoe") with {BatchId="batch1"}]);
            Assert.Equal(missing,response.UncertainOutcome);
            Assert.Equal(["/ppp/secret/add"],client.Writes);
            var saved=store.GetVouchers();
            Assert.Equal(state,Assert.Single(saved.Where(x=>x.Service=="pppoe")).DeliveryStatus);
            Assert.Equal("CREATED",Assert.Single(saved.Where(x=>x.Service=="hotspot")).DeliveryStatus);
            if(missing)
                await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
                    new VoucherBatchDispatcher(new RouterGateway(client),store)
                        .SendAsync([Card("001234","pppoe") with {BatchId="batch1"}]));
            Assert.Single(client.Writes);
        }
        finally {Directory.Delete(root,true);}
    }
}
