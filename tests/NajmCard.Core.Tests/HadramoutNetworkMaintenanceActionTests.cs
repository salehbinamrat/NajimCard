using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;
namespace NajmCard.Core.Tests;

/// <summary>Selected single RouterOS 6 hotspot server and session operations;
/// no broad server toggle or blind retry after uncertain write.</summary>
public sealed class HadramoutNetworkMaintenanceActionTests
{
    private sealed class RouterFake : IRouterCommandClient, IRouterQueryClient
    {
        public int Writes;
        public bool ChangedIdentity;
        public bool FailReply;
        public bool Removed;
        public bool ServerDisabled;
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByIdAsync(
            string command,string id,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Assert.Equal("*AB",id);
            if(command=="/ip/hotspot/print")
                return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(
                [new Dictionary<string,string>
                {
                    [".id"]=id,["name"]=ChangedIdentity?"different":"hs-one",
                    ["interface"]="bridge1",["profile"]="hsprof1",
                    ["disabled"]=ServerDisabled?"yes":"no",["invalid"]="no",["running"]="yes"
                }]);
            if(command=="/ip/hotspot/active/print")
                return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(
                    Removed ? [] : [new Dictionary<string,string>
                    {
                        [".id"]=id,["user"]=ChangedIdentity?"wrong":"123456",
                        ["address"]="10.0.1.2",["mac-address"]="AA:BB:CC:DD:EE:FF"
                    }]);
            throw new InvalidOperationException(command);
        }
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Assert.True(command is "/ip/hotspot/set" or "/ip/hotspot/active/remove");
            Assert.Equal("*AB",attributes![".id"]);
            Writes++;
            if(FailReply)throw new IOException("lost RouterOS write acknowledgement");
            if(command=="/ip/hotspot/set")ServerDisabled=attributes["disabled"]=="yes";
            else Removed=true;
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>([]);
        }
    }
    [Fact]
    public async Task HotspotServer_State_Change_Validates_Identity_And_Confirms_Readback()
    {
        var fake=new RouterFake();
        var selected=new RouterHotspotServerStatus("*AB","hs-one","bridge1","hsprof1",false,false,true);
        var result=await new RouterGateway(fake).ChangeHotspotServerStateAsync(selected,true);
        Assert.True(result.Success);
        Assert.False(result.UncertainOutcome);
        Assert.Equal(1,fake.Writes);
        Assert.True(fake.ServerDisabled);
    }
    [Fact]
    public async Task Changed_Server_Does_Not_Send_Any_Mutation()
    {
        var fake=new RouterFake{ChangedIdentity=true};
        var selected=new RouterHotspotServerStatus("*AB","hs-one","bridge1","hsprof1",false,false,true);
        await Assert.ThrowsAsync<InvalidOperationException>(
            ()=>new RouterGateway(fake).ChangeHotspotServerStateAsync(selected,true));
        Assert.Equal(0,fake.Writes);
    }
    [Fact]
    public async Task Uncertain_Server_Write_Must_Not_Auto_Retry()
    {
        var fake=new RouterFake{FailReply=true};
        var selected=new RouterHotspotServerStatus("*AB","hs-one","bridge1","hsprof1",false,false,true);
        var result=await new RouterGateway(fake).ChangeHotspotServerStateAsync(selected,true);
        Assert.False(result.Success);
        Assert.True(result.UncertainOutcome);
        Assert.Equal(1,fake.Writes);
    }
    [Fact]
    public async Task Hotspot_Session_Disconnect_Checks_Exact_Identity_And_Absence()
    {
        var fake=new RouterFake();
        var selected=new HotspotSession("*AB","123456","10.0.1.2","AA:BB:CC:DD:EE:FF","1h");
        var result=await new RouterGateway(fake).DisconnectHotspotSessionAsync(selected);
        Assert.True(result.Success);
        Assert.True(fake.Removed);
        Assert.Equal(1,fake.Writes);
    }
    [Fact]
    public async Task Changed_Session_Is_Not_Disconnected()
    {
        var fake=new RouterFake{ChangedIdentity=true};
        var selected=new HotspotSession("*AB","123456","10.0.1.2","AA:BB:CC:DD:EE:FF","1h");
        await Assert.ThrowsAsync<InvalidOperationException>(
            ()=>new RouterGateway(fake).DisconnectHotspotSessionAsync(selected));
        Assert.Equal(0,fake.Writes);
    }
}
