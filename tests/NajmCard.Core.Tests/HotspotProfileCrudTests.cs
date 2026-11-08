using System.IO;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>RouterOS API-boundary regression tests for Hadramout Hotspot package CRUD.
/// A selected package is re-read by the exact RouterOS internal ID before writes.</summary>
public sealed class HotspotProfileCrudTests
{
    private sealed class Spy : IRouterCommandClient, IRouterReceiptClient, IRouterQueryClient
    {
        public readonly List<(string Path,Dictionary<string,string> Args)> Calls=[];
        public bool MissingReceipt;
        public bool Reject;
        public bool Drop;
        public bool Changed;
        public Dictionary<string,string>? Current=NewRow();
        private static Dictionary<string,string> NewRow(string name="gold",string rate="2M/512k",
            string timeout="1h",string shared="1")=>new()
        {
            [".id"]="*A",["name"]=name,["rate-limit"]=rate,
            ["session-timeout"]=timeout,["shared-users"]=shared
        };
        private void Record(string path,IReadOnlyDictionary<string,string>? attrs)
            => Calls.Add((path,new(attrs??new Dictionary<string,string>())));
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByIdAsync(
            string command,string id,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Record(command,attributes);
            Assert.Equal("/ip/hotspot/user/profile/print",command);
            Assert.Equal("*A",id);
            IReadOnlyList<IReadOnlyDictionary<string,string>> rows=Current is null?[]:
                [Changed?NewRow(name:"different"):new(Current)];
            return Task.FromResult(rows);
        }
        public async Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            var reply=await Respond(command,attributes);
            return reply.Records;
        }
        public Task<RouterCommandReply> CommandWithReceiptAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)=>Respond(command,attributes);
        private Task<RouterCommandReply> Respond(string command,IReadOnlyDictionary<string,string>? attributes)
        {
            Record(command,attributes);
            if(command=="/ip/hotspot/user/profile/print")
                return Task.FromResult(new RouterCommandReply(
                    Current is null ? [] : [new Dictionary<string,string>(Current)],new Dictionary<string,string>()));
            if(Reject)throw new RouterOsTrapException("not permitted");
            if(Drop)throw new IOException("reply lost");
            if(command=="/ip/hotspot/user/profile/add")
            {
                Current=NewRow(attributes!["name"],attributes["rate-limit"],
                    attributes["session-timeout"],attributes["shared-users"]);
                return Task.FromResult(new RouterCommandReply([],
                    MissingReceipt?new Dictionary<string,string>():
                    new Dictionary<string,string>{{"ret","*A"}}));
            }
            if(command=="/ip/hotspot/user/profile/set")
            {
                Assert.Equal("*A",attributes![".id"]);
                Current=NewRow(attributes["name"],attributes["rate-limit"],
                    attributes["session-timeout"],attributes["shared-users"]);
                return Task.FromResult(new RouterCommandReply([],new Dictionary<string,string>()));
            }
            if(command=="/ip/hotspot/user/profile/remove")
            {
                Assert.Equal("*A",attributes![".id"]);
                Current=null;
                return Task.FromResult(new RouterCommandReply([],new Dictionary<string,string>()));
            }
            throw new InvalidOperationException(command);
        }
    }
    private static RouterCardProfile Existing()
        => new("*A","gold","2M/512k","1h","1");
    private static HotspotProfileDraft Updated()
        => new("gold-plus","4M/1M","2h","2");

    [Fact]
    public async Task Create_Uses_Receipt_Id_And_Verifies_Server_Readback()
    {
        var spy=new Spy{Current=null};
        var result=await new RouterGateway(spy).SaveHotspotProfileAsync(Updated());
        Assert.True(result.Success);
        Assert.False(result.UncertainOutcome);
        Assert.Equal("*A",result.ProfileId);
        Assert.Equal(new[]{
            "/ip/hotspot/user/profile/print",
            "/ip/hotspot/user/profile/add",
            "/ip/hotspot/user/profile/print"
        },spy.Calls.Select(x=>x.Path));
        Assert.Equal(4,spy.Calls[1].Args.Count); // only safe profile fields, no script properties
    }

    [Fact]
    public async Task Update_Matches_Selected_Profile_Before_Write_And_Confirms_State()
    {
        var spy=new Spy();
        var result=await new RouterGateway(spy).SaveHotspotProfileAsync(Updated(),Existing());
        Assert.True(result.Success);
        Assert.Equal("*A",result.ProfileId);
        Assert.Equal(3,spy.Calls.Count);
        Assert.Equal("/ip/hotspot/user/profile/set",spy.Calls[1].Path);
        Assert.Equal(5,spy.Calls[1].Args.Count);
    }

    [Fact]
    public async Task Delete_Requires_Exact_Id_And_Confirms_Removal()
    {
        var spy=new Spy();
        var result=await new RouterGateway(spy).DeleteHotspotProfileAsync(Existing());
        Assert.True(result.Success);
        Assert.Equal(new[]{
            "/ip/hotspot/user/profile/print",
            "/ip/hotspot/user/profile/remove",
            "/ip/hotspot/user/profile/print"
        },spy.Calls.Select(x=>x.Path));
        Assert.Single(spy.Calls[1].Args);
    }

    [Theory]
    [InlineData("gold","2M/512k","other","1")]
    [InlineData("gold","3M/512k","1h","1")]
    [InlineData("gold","2M/512k","1h","8")]
    [InlineData("different","2M/512k","1h","1")]
    public async Task Changed_Profile_Aborts_Before_Any_Mutation(
        string name,string rate,string timeout,string shared)
    {
        var spy=new Spy();
        spy.Current=new Dictionary<string,string>
        {
            [".id"]="*A",["name"]=name,["rate-limit"]=rate,
            ["session-timeout"]=timeout,["shared-users"]=shared
        };
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            new RouterGateway(spy).SaveHotspotProfileAsync(Updated(),Existing()));
        Assert.Single(spy.Calls);
    }

    [Theory]
    [InlineData("bad name; reboot","4M/1M","1h","1")]
    [InlineData("gold","not-a-rate","1h","1")]
    [InlineData("gold","4M/1M","bad; /system reboot","1")]
    [InlineData("gold","4M/1M","1h","0")]
    public async Task Unsafe_Draft_Stops_Before_Router_Io(
        string name,string rate,string timeout,string shared)
    {
        var spy=new Spy();
        await Assert.ThrowsAnyAsync<ArgumentException>(()=>
            new RouterGateway(spy).SaveHotspotProfileAsync(
                new(name,rate,timeout,shared),Existing()));
        Assert.Empty(spy.Calls);
    }

    [Fact]
    public async Task Lost_Reply_Or_Missing_Add_Id_Is_Uncertain_And_Never_Retried()
    {
        var dropped=new Spy{Drop=true,Current=null};
        var bad=await new RouterGateway(dropped).SaveHotspotProfileAsync(Updated());
        Assert.True(bad.UncertainOutcome);
        Assert.False(bad.Success);
        Assert.Equal(2,dropped.Calls.Count);
        var noReceipt=new Spy{Current=null,MissingReceipt=true};
        var unknown=await new RouterGateway(noReceipt).SaveHotspotProfileAsync(Updated());
        Assert.True(unknown.UncertainOutcome);
        Assert.Equal(2,noReceipt.Calls.Count);
    }
    [Fact]
    public async Task Router_Trap_Is_Known_Failure_Not_Uncertain()
    {
        var spy=new Spy{Reject=true};
        var result=await new RouterGateway(spy).SaveHotspotProfileAsync(Updated(),Existing());
        Assert.False(result.Success);
        Assert.False(result.UncertainOutcome);
        Assert.Equal(2,spy.Calls.Count);
    }
}
