using System.IO;
using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// One authenticated RouterOS session per login, with independent fast and
/// User Manager inventory lanes. Transport failures can safely reconnect
/// reads once; mutations are NEVER replayed when an outcome is unknown.
/// No automatic connection is made after explicit logout.
/// </summary>
public sealed class ResilientRouterSession : IRouterCommandClient, IRouterReceiptClient,
    IRouterQueryClient, IAsyncDisposable
{
    private sealed class Lane(IRouterCommandClient? current=null)
    {
        internal readonly SemaphoreSlim Gate=new(1,1);
        internal IRouterCommandClient? Current=current;
    }

    private readonly Lane _fast;
    private readonly Lane _inventory=new();
    private readonly Func<CancellationToken,Task<IRouterCommandClient>> _connect;
    private bool _closed;

    public ResilientRouterSession(IRouterCommandClient authenticated,
        Func<CancellationToken,Task<IRouterCommandClient>> connect)
    {
        ArgumentNullException.ThrowIfNull(authenticated);
        _connect=connect??throw new ArgumentNullException(nameof(connect));
        _fast=new Lane(authenticated);
    }

    private static bool IsReadOnly(string command)=>
        command.EndsWith("/print",StringComparison.Ordinal);

    private Lane For(string command)=>
        command=="/tool/user-manager/user/print"?_inventory:_fast;

    private static bool Disconnected(Exception e)=>e is ObjectDisposedException
        or OperationCanceledException
        || e is IOException and not RouterOsTrapException;

    private static async Task ReleaseAsync(IRouterCommandClient? old)
    {
        if(old is IAsyncDisposable disposable)
        {
            try{await disposable.DisposeAsync();}
            catch(IOException){}
            catch(ObjectDisposedException){}
        }
    }

    private async Task<T> ExecuteAsync<T>(Lane lane,
        Func<IRouterCommandClient,CancellationToken,Task<T>> operation,
        bool retryRead,CancellationToken ct)
    {
        await lane.Gate.WaitAsync(ct);
        try
        {
            if(_closed)throw new ObjectDisposedException(nameof(ResilientRouterSession),
                "انتهت جلسة الراوتر؛ سجل الدخول مجددا.");
            lane.Current ??= await _connect(ct);
            try{return await operation(lane.Current,ct);}
            catch(Exception ex) when(Disconnected(ex))
            {
                var dead=lane.Current;
                lane.Current=null;
                await ReleaseAsync(dead);
                // Reads are idempotent; do not retry a write because a router
                // may have applied it before the TCP connection broke.
                if(!retryRead || ct.IsCancellationRequested || _closed ||
                    ex is OperationCanceledException)
                    throw;
                lane.Current=await _connect(ct);
                try{return await operation(lane.Current,ct);}
                catch(Exception repeat) when(Disconnected(repeat))
                {
                    var second=lane.Current;
                    lane.Current=null;
                    await ReleaseAsync(second);
                    throw;
                }
            }
        }
        finally{lane.Gate.Release();}
    }

    public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
        string command,IReadOnlyDictionary<string,string>? attributes=null,
        CancellationToken cancellationToken=default)
    {
        RouterOsCommandPolicy.Validate(command,attributes);
        return ExecuteAsync(For(command),
            (current,ct)=>current.CommandAsync(command,attributes,ct),
            IsReadOnly(command),cancellationToken);
    }

    public Task<RouterCommandReply> CommandWithReceiptAsync(
        string command,IReadOnlyDictionary<string,string>? attributes=null,
        CancellationToken cancellationToken=default)
    {
        RouterOsCommandPolicy.Validate(command,attributes);
        return ExecuteAsync(For(command),(current,ct)=>
            current is IRouterReceiptClient receipt
                ?receipt.CommandWithReceiptAsync(command,attributes,ct)
                :throw new InvalidOperationException(
                    "الاتصال لا يدعم التحقق من رد RouterOS."),
            IsReadOnly(command),cancellationToken);
    }

    public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByIdAsync(
        string command,string id,IReadOnlyDictionary<string,string>? attributes=null,
        CancellationToken cancellationToken=default) =>
        ExecuteAsync(For(command),(current,ct)=>
            current is IRouterQueryClient filtered
                ?filtered.QueryByIdAsync(command,id,attributes,ct)
                :throw new InvalidOperationException(
                    "الاتصال لا يدعم قراءة السجل المحدد."),
            true,cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if(_closed)return;
        _closed=true;
        // Closing the transport aborts in-flight requests; after logout the UI
        // has detached all pages, and no subsequent operation may reconnect.
        var fast=_fast.Current;_fast.Current=null;
        var inventory=_inventory.Current;_inventory.Current=null;
        await ReleaseAsync(fast);
        if(!ReferenceEquals(fast,inventory))await ReleaseAsync(inventory);
    }
}
