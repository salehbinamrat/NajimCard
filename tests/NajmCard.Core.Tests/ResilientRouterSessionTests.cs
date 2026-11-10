using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class ResilientRouterSessionTests
{
    private sealed class Fake : IRouterCommandClient, IAsyncDisposable
    {
        public int Calls;
        public int Disposals;
        public bool Break;
        public TaskCompletionSource? Block;
        public async Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            Calls++;
            if(Break)throw new ObjectDisposedException(nameof(Fake));
            if(Block is not null)await Block.Task.WaitAsync(cancellationToken);
            return new IReadOnlyDictionary<string,string>[]
                {new Dictionary<string,string>{{"ok","yes"}}};
        }
        public ValueTask DisposeAsync(){Disposals++;return ValueTask.CompletedTask;}
    }

    [Fact]
    public async Task Read_Reconnects_After_Disposed_Client_Only_Once()
    {
        var broken=new Fake{Break=true};
        var working=new Fake();
        int connections=0;
        await using var session=new ResilientRouterSession(broken,_=>
        {
            connections++;
            return Task.FromResult<IRouterCommandClient>(working);
        });
        var data=await session.CommandAsync("/system/resource/print");
        Assert.Single(data);
        Assert.Equal(1,broken.Calls);
        Assert.Equal(1,working.Calls);
        Assert.Equal(1,connections);
        Assert.Equal(1,broken.Disposals);
    }

    [Fact]
    public async Task Router_Writes_Are_Never_Automatically_Retried()
    {
        var broken=new Fake{Break=true};
        var healthy=new Fake();
        int connections=0;
        await using var session=new ResilientRouterSession(broken,_=>
        {
            connections++;
            return Task.FromResult<IRouterCommandClient>(healthy);
        });
        await Assert.ThrowsAsync<ObjectDisposedException>(async()=>
            await session.CommandAsync("/ip/hotspot/user/set",
                new Dictionary<string,string>{{".id","*1"},{"disabled","yes"}}));
        Assert.Equal(0,connections);
        Assert.Equal(0,healthy.Calls);
        await session.CommandAsync("/system/resource/print");
        Assert.Equal(1,connections);
        Assert.Equal(1,healthy.Calls);
    }

    [Fact]
    public async Task Long_UserManager_Inventory_Does_Not_Block_Fast_Lane()
    {
        var current=new Fake();
        var inventory=new Fake{Block=new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously)};
        await using var session=new ResilientRouterSession(current,_=>
            Task.FromResult<IRouterCommandClient>(inventory));
        var scan=session.CommandAsync("/tool/user-manager/user/print");
        // The inventory lane runs asynchronously and never takes the live gate.
        await session.CommandAsync("/system/resource/print").WaitAsync(
            TimeSpan.FromSeconds(2));
        Assert.Equal(1,current.Calls);
        inventory.Block!.SetResult();
        await scan.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Logout_Prevents_Any_Transparent_Reconnect()
    {
        var first=new Fake();
        var session=new ResilientRouterSession(first,_=>
            Task.FromResult<IRouterCommandClient>(new Fake()));
        await session.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async()=>
            await session.CommandAsync("/system/resource/print"));
        Assert.Equal(1,first.Disposals);
    }
}
