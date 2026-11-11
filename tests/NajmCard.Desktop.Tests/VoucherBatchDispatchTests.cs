using System.IO;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Desktop.Tests;

/// <summary>Real SQLite+DPAPI -> dispatch -> RouterGateway command-boundary integration.</summary>
public sealed class VoucherBatchDispatchTests
{
    private static Voucher Voucher(string name, string service, string customer = "") =>
        new(name, "pass123", "gold", "batch-r", DateTimeOffset.UtcNow,
            Service: service, Customer: customer);

    private sealed class Spy : IRouterCommandClient, IRouterReceiptClient, IRouterQueryClient
    {
        public readonly List<(string Name, IReadOnlyDictionary<string,string> Data)> Writes = [];
        public bool MissingId { get; set; }
        public Action? OnInventoryRead { get; set; }
        public bool FailNameRead { get; set; }
        public string? PppCollision { get; set; }
        public string? LastUsername { get; set; }
        public string? LastProfile { get; set; }
        public string? LastCustomer { get; set; }
        public bool SuppressActualProfile { get; set; }
        public int QueryCount { get; set; }
        public readonly List<string> Reads = [];
        public Task<RouterCommandReply> CommandWithReceiptAsync(
            string command, IReadOnlyDictionary<string,string>? attributes = null,
            CancellationToken cancellationToken = default)
        {
            Writes.Add((command, new Dictionary<string,string>(attributes!)));
            if(command=="/tool/user-manager/user/add")
            {
                LastUsername=attributes!["username"];
                LastCustomer=attributes["customer"];
            }
            return Task.FromResult(new RouterCommandReply(
                [], MissingId ? new Dictionary<string,string>() : new Dictionary<string,string> { ["ret"] = "*A" }));
        }
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command, IReadOnlyDictionary<string,string>? attributes = null,
            CancellationToken cancellationToken = default)
        {
            if (command.EndsWith("/print", StringComparison.Ordinal))
            {
                Reads.Add(command);
                OnInventoryRead?.Invoke();
                if (FailNameRead && command == "/ppp/secret/print")
                    throw new RouterOsTrapException("not enough permissions");
                IReadOnlyList<IReadOnlyDictionary<string,string>> data =
                    command == "/ppp/secret/print" && PppCollision is not null
                        ? [new Dictionary<string,string> { ["name"] = PppCollision }]
                        : [];
                return Task.FromResult(data);
            }
            Writes.Add((command, new Dictionary<string,string>(attributes ?? new Dictionary<string,string>())));
            if(command=="/tool/user-manager/user/create-and-activate-profile")
                LastProfile=attributes!["profile"];
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>([]);
        }
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByIdAsync(
            string command,string id,IReadOnlyDictionary<string,string>? attributes = null,
            CancellationToken cancellationToken = default)
        {
            QueryCount++;
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows=
            [
                new Dictionary<string,string>
                {
                    [".id"]=id,["username"]=LastUsername??"",
                    ["actual-profile"]=SuppressActualProfile?"":LastProfile??"",
                    ["customer"]=LastCustomer??""
                }
            ];
            return Task.FromResult(rows);
        }
    }

    private static string Root()
    {
        var root = Path.Combine(Path.GetTempPath(),"najm-dispatch",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public async Task UserManager_Delivery_Goes_Through_V6_And_Only_Updates_Matching_Service()
    {
        var root = Root();
        try
        {
            using var store = new VoucherStore(root);
            var hotspot = Voucher("123456", "hotspot");
            var userman = Voucher("123456", "userman", "admin");
            store.SaveBatch([hotspot, userman]);
            var spy = new Spy();
            var result = await new VoucherBatchDispatcher(new RouterGateway(spy), store)
                .SendAsync([userman]);
            Assert.True(result.Success);
            Assert.Equal([
                "/tool/user-manager/user/add",
                "/tool/user-manager/user/create-and-activate-profile"
            ], spy.Writes.Select(x=>x.Name));
            Assert.Equal("*A", spy.Writes[1].Data["numbers"]);
            Assert.Equal("admin", spy.Writes[1].Data["customer"]);
            var saved = store.GetVouchers();
            Assert.True(Assert.Single(saved.Where(x => x.Service == "userman")).SentToRouter);
            Assert.False(Assert.Single(saved.Where(x => x.Service == "hotspot")).SentToRouter);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Unassigned_UserManager_Profile_Does_Not_Become_Printable()
    {
        var root=Root();
        try
        {
            using var store=new VoucherStore(root);
            var card=Voucher("712345","userman","admin");
            store.SaveBatch([card]);
            var spy=new Spy{SuppressActualProfile=true};
            var result=await new VoucherBatchDispatcher(new RouterGateway(spy),store)
                .SendAsync([card]);
            Assert.True(result.UncertainOutcome);
            Assert.Equal(0,result.Succeeded);
            var saved=Assert.Single(store.GetVouchers());
            Assert.Equal("UNKNOWN",saved.DeliveryStatus);
            Assert.False(saved.SentToRouter);
            Assert.True(spy.QueryCount>=1);
            Assert.Equal(2,spy.Writes.Count);
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public async Task Missing_Router_Receipt_Leaves_Local_UserManager_Voucher_Unsent_And_Does_Not_Retry()
    {
        var root = Root();
        try
        {
            using var store = new VoucherStore(root);
            var um = Voucher("456712", "userman", "admin");
            store.SaveBatch([um]);
            var spy = new Spy { MissingId = true };
            var result = await new VoucherBatchDispatcher(new RouterGateway(spy), store).SendAsync([um]);
            Assert.True(result.UncertainOutcome);
            Assert.Single(spy.Writes);
            Assert.False(Assert.Single(store.GetVouchers()).SentToRouter);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Ppp_Voucher_Name_Collision_Stops_UserManager_Issue_Before_Router_Write()
    {
        var root=Root();
        try
        {
            using var store=new VoucherStore(root);
            var card=Voucher("888111","userman","admin");
            store.SaveBatch([card]);
            var spy=new Spy { PppCollision="888111" };
            await Assert.ThrowsAnyAsync<ArgumentException>(
                async () => await new VoucherBatchDispatcher(
                    new RouterGateway(spy),store).SendAsync([card]));
            Assert.Empty(spy.Writes);
            Assert.Equal("CREATED",Assert.Single(store.GetVouchers()).DeliveryStatus);
        }
        finally { Directory.Delete(root,true); }
    }

    [Fact]
    public async Task Denied_One_Service_Inventory_Stops_Preflight_Without_Any_Router_Write()
    {
        var root=Root();
        try
        {
            using var store=new VoucherStore(root);
            var card=Voucher("333444","userman","admin");
            store.SaveBatch([card]);
            var spy=new Spy { FailNameRead=true };
            await Assert.ThrowsAsync<RouterOsTrapException>(
                async () => await new VoucherBatchDispatcher(
                    new RouterGateway(spy),store).SendAsync([card]));
            Assert.Empty(spy.Writes);
            Assert.Equal("CREATED",Assert.Single(store.GetVouchers()).DeliveryStatus);
        }
        finally { Directory.Delete(root,true); }
    }

    [Fact]
    public async Task Caller_Changes_During_Preflight_Do_Not_Replace_Validated_Batch()
    {
        var root=Root();
        try
        {
            using var store=new VoucherStore(root);
            var original=Voucher("111222","hotspot");
            var replacement=Voucher("333444","hotspot");
            store.SaveBatch([original,replacement]);
            var batch=new List<Voucher>{original};
            var spy=new Spy{OnInventoryRead=()=>batch[0]=replacement};
            var result=await new VoucherBatchDispatcher(new RouterGateway(spy),store)
                .SendAsync(batch);
            Assert.True(result.Success);
            var saved=store.GetBatchVouchers("hotspot",original.BatchId);
            Assert.Equal("SENT",saved.Single(x=>x.Username==original.Username).DeliveryStatus);
            Assert.Equal("CREATED",saved.Single(x=>x.Username==replacement.Username).DeliveryStatus);
            Assert.Equal(original.Username,Assert.Single(spy.Writes).Data["name"]);
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public async Task Cancelled_Batch_Does_Not_Lease_Or_Write_Cards()
    {
        var root=Root();
        try
        {
            using var store=new VoucherStore(root);
            var card=Voucher("111222","hotspot");
            store.SaveBatch([card]);
            var spy=new Spy();
            using var cancellation=new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>
                new VoucherBatchDispatcher(new RouterGateway(spy),store)
                    .SendAsync([card],cancellation.Token));
            Assert.Empty(spy.Writes);
            Assert.Equal("CREATED",Assert.Single(store.GetVouchers()).DeliveryStatus);
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public async Task Rejects_Mixed_Services_Before_Any_Router_Command()
    {
        var root = Root();
        try
        {
            using var store = new VoucherStore(root);
            var spy = new Spy();
            await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
                await new VoucherBatchDispatcher(new RouterGateway(spy), store)
                    .SendAsync([Voucher("one", "hotspot"),
                        Voucher("two", "userman", "admin")]));
            Assert.Empty(spy.Writes);
        }
        finally { Directory.Delete(root, true); }
    }
}
