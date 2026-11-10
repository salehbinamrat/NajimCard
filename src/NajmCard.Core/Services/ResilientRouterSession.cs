using System.IO;
using System.Net.Sockets;
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
    private readonly CancellationTokenSource _lifetime=new();
    private readonly CancellationToken _sessionToken;
    private int _closed;

    public ResilientRouterSession(IRouterCommandClient authenticated,
        Func<CancellationToken,Task<IRouterCommandClient>> connect)
    {
        ArgumentNullException.ThrowIfNull(authenticated);
        _connect=connect??throw new ArgumentNullException(nameof(connect));
        _fast=new Lane(authenticated);
        _sessionToken=_lifetime.Token;
    }

    private static bool IsReadOnly(string command)=>
        command.EndsWith("/print",StringComparison.Ordinal);

    private Lane For(string command)=>
        command=="/tool/user-manager/user/print"?_inventory:_fast;

    private static bool Disconnected(IRouterCommandClient current,Exception error)=>
        current is RouterOsClient { IsUsable:false } ||
        error is ObjectDisposedException or OperationCanceledException or SocketException or InvalidDataException ||
        error is IOException and not RouterOsTrapException;

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
        if(Volatile.Read(ref _closed)!=0)
            throw new ObjectDisposedException(nameof(ResilientRouterSession),
                "انتهت جلسة الراوتر، سجل الدخول مجددا.");
        using var request=CancellationTokenSource.CreateLinkedTokenSource(ct,_sessionToken);
        var token=request.Token;
        await lane.Gate.WaitAsync(token);
        try
        {
            var current=lane.Current ??= await _connect(token);
            token.ThrowIfCancellationRequested();
            try{return await operation(current,token);}
            catch(Exception ex) when(Disconnected(current,ex))
            {
                lane.Current=null;
                await ReleaseAsync(current);
                // A write may already have reached the router before the reply was lost.
                if(!retryRead || token.IsCancellationRequested)
                    throw;
                var replacement=lane.Current=await _connect(token);
                token.ThrowIfCancellationRequested();
                try{return await operation(replacement,token);}
                catch(Exception repeat) when(Disconnected(replacement,repeat))
                {
                    lane.Current=null;
                    await ReleaseAsync(replacement);
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
        if(Interlocked.Exchange(ref _closed,1)!=0)return;
        _lifetime.Cancel();
        try { await Task.WhenAll(CloseLaneAsync(_fast),CloseLaneAsync(_inventory)); }
        finally { _lifetime.Dispose(); }
    }

    private static async Task CloseLaneAsync(Lane lane)
    {
        // Wait for commands to release the transport before disposing its lock.
        await lane.Gate.WaitAsync();
        try
        {
            var client=lane.Current;
            lane.Current=null;
            await ReleaseAsync(client);
        }
        finally { lane.Gate.Release(); }
    }
}
