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
        public bool NoHotspotServers { get; set; }
        public bool DisableHotspotRadius { get; set; }
        public bool NoRadiusClient { get; set; }
        public bool NoRegisteredNas { get; set; }
        public bool WrongProfileOwner { get; set; }
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
                IReadOnlyList<IReadOnlyDictionary<string,string>> data=command switch
                {
                    "/ppp/secret/print" when PppCollision is not null =>
                        [new Dictionary<string,string>{["name"]=PppCollision}],
                    "/tool/user-manager/profile/print" =>
                        [new Dictionary<string,string>
                        {
                            ["name"]="gold",["owner"]=WrongProfileOwner?"another-owner":"admin"
                        }],
                    "/ip/hotspot/print" when !NoHotspotServers =>
                        [new Dictionary<string,string>
                        {
                            ["name"]="hotspot1",["profile"]="hsprof",["disabled"]="no"
                        }],
                    "/ip/hotspot/profile/print" =>
                        [new Dictionary<string,string>
                        {
                            ["name"]="hsprof",["use-radius"]=DisableHotspotRadius?"no":"yes",
                            ["radius-accounting"]="yes"
                        }],
                    "/radius/print" when !NoRadiusClient =>
                        [new Dictionary<string,string>
                        {
                            ["service"]="hotspot",["address"]="127.0.0.1",
                            ["disabled"]="no"
                        }],
                    "/tool/user-manager/router/print" when !NoRegisteredNas =>
                        [new Dictionary<string,string>
                        {
                            ["name"]="local-nas",["ip-address"]="127.0.0.1",
                            ["disabled"]="no"
                        }],
                    _=>[]
                };
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

    [Theory]
    [InlineData("hotspot-disabled")]
    [InlineData("radius-missing")]
    [InlineData("nas-missing")]
    [InlineData("wrong-owner")]
    [InlineData("no-hotspot")]
    public async Task Unworkable_UserManager_Nas_Is_Rejected_Before_Any_User_Write(
        string failure)
    {
        var root=Root();
        try
        {
            using var store=new VoucherStore(root);
            var card=Voucher("725514","userman","admin");
            store.SaveBatch([card]);
            var spy=new Spy
            {
                DisableHotspotRadius=failure=="hotspot-disabled",
                NoRadiusClient=failure=="radius-missing",
                NoRegisteredNas=failure=="nas-missing",
                WrongProfileOwner=failure=="wrong-owner",
                NoHotspotServers=failure=="no-hotspot"
            };
            await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>
                new VoucherBatchDispatcher(new RouterGateway(spy),store)
                    .SendAsync([card]));
            Assert.Empty(spy.Writes);
            Assert.Equal("CREATED",Assert.Single(store.GetVouchers()).DeliveryStatus);
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public async Task UserManager_Preflight_Uses_Only_Reads_Before_Creating_Voucher()
    {
        var root=Root();
        try
        {
            using var store=new VoucherStore(root);
            var card=Voucher("725516","userman","admin");
            store.SaveBatch([card]);
            var spy=new Spy();
            var result=await new VoucherBatchDispatcher(new RouterGateway(spy),store)
                .SendAsync([card]);
            Assert.True(result.Success);
            Assert.Contains("/ip/hotspot/print",spy.Reads);
            Assert.Contains("/ip/hotspot/profile/print",spy.Reads);
            Assert.Contains("/radius/print",spy.Reads);
            Assert.Contains("/tool/user-manager/router/print",spy.Reads);
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

    [Fact]
    public async Task Username_Only_UM6_Voucher_Round_Trips_Sqlite_And_Sends_Blank_Password()
    {
        var root=Root();
        try
        {
            using var store=new VoucherStore(root);
            var card=Voucher("721540","userman","admin") with{Password=""};
            store.SaveBatch([card]);
            var before=Assert.Single(store.GetVouchers());
            Assert.Empty(before.Password);
            Assert.Equal("CREATED",before.DeliveryStatus);
            var spy=new Spy();
            var result=await new VoucherBatchDispatcher(new RouterGateway(spy),store)
                .SendAsync([card]);
            Assert.True(result.Success);
            var sent=Assert.Single(store.GetVouchers());
            Assert.Empty(sent.Password);
            Assert.Equal("SENT",sent.DeliveryStatus);
            Assert.True(sent.SentToRouter);
            Assert.Equal("",spy.Writes[0].Data["password"]);
            Assert.Equal("721540",spy.Writes[0].Data["username"]);
        }
        finally{Directory.Delete(root,true);}
    }

    [Fact]
    public async Task Username_Only_Hotspot_Sends_Blank_Password_Without_UM6_Radius_Preflight()
    {
        var root=Root();
        try
        {
            using var store=new VoucherStore(root);
            var card=Voucher("721541","hotspot") with{Password=""};
            store.SaveBatch([card]);
            var spy=new Spy();
            var result=await new VoucherBatchDispatcher(new RouterGateway(spy),store)
                .SendAsync([card]);
            Assert.True(result.Success);
            Assert.False(Assert.Single(spy.Writes).Data.ContainsKey("password"));
            Assert.Equal("SENT",Assert.Single(store.GetVouchers()).DeliveryStatus);
        }
        finally{Directory.Delete(root,true);}
    }



    [Theory]
    [InlineData("hotspot", "username-only")]
    [InlineData("hotspot", "matching")]
    [InlineData("hotspot", "different")]
    [InlineData("userman", "username-only")]
    [InlineData("userman", "matching")]
    [InlineData("userman", "different")]
    [InlineData("pppoe", "username-only")]
    [InlineData("pppoe", "matching")]
    [InlineData("pppoe", "different")]
    public async Task Credential_Mode_Survives_Generation_Storage_And_Actual_Router_Command(
        string service, string mode)
    {
        var root=Root();
        try
        {
            using var store=new VoucherStore(root);
            var options=new VoucherGenerationOptions(1,"",8,VoucherGenerator.Digits,
                mode=="matching","gold",PasswordLength:6,PasswordCharacters:"abcdef",
                UsernameOnly:mode=="username-only");
            var card=Assert.Single(VoucherGenerator.Generate(options)) with
            {
                Service=service,Customer=service=="userman"?"admin":""
            };
            store.SaveBatch([card]);
            var before=Assert.Single(store.GetBatchVouchers(service,card.BatchId));
            Assert.Equal(card.Password,before.Password);
            Assert.Equal("CREATED",before.DeliveryStatus);

            var spy=new Spy();
            var result=await new VoucherBatchDispatcher(new RouterGateway(spy),store)
                .SendAsync([card]);
            Assert.True(result.Success);

            var addPath=service switch
            {
                "userman"=>"/tool/user-manager/user/add",
                "pppoe"=>"/ppp/secret/add",
                _=>"/ip/hotspot/user/add"
            };
            var attributes=Assert.Single(spy.Writes.Where(x=>x.Name==addPath)).Data;
            Assert.Equal(card.Username,attributes[service=="userman"?"username":"name"]);
            if(mode=="username-only")
            {
                Assert.Empty(card.Password);
                if(service=="hotspot")
                    Assert.False(attributes.ContainsKey("password"));
                else
                    Assert.Equal("",attributes["password"]);
            }
            else if(mode=="matching")
            {
                Assert.Equal(card.Username,card.Password);
                Assert.Equal(card.Username,attributes["password"]);
            }
            else
            {
                Assert.NotEmpty(card.Password);
                Assert.NotEqual(card.Username,card.Password);
                Assert.Equal(card.Password,attributes["password"]);
            }

            var after=Assert.Single(store.GetBatchVouchers(service,card.BatchId));
            Assert.Equal(card.Password,after.Password);
            Assert.Equal("SENT",after.DeliveryStatus);
            Assert.True(after.SentToRouter);
        }
        finally{Directory.Delete(root,true);}
    }
}
