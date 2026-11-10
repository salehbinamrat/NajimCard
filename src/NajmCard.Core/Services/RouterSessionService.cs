using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

public interface IRouterSessionService :
    IRouterCommandClient, IRouterReceiptClient, IRouterQueryClient, IAsyncDisposable
{
}

/// <summary>Owns the transport for one login. Pages share a gateway backed by this session.</summary>
public sealed class RouterSessionService : IRouterSessionService
{
    private readonly RouterEndpoint _endpoint;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _closed;
    private RouterOsClient? _client;
    private int _closing;

    private RouterSessionService(RouterEndpoint endpoint, RouterOsClient client)
    {
        _endpoint = endpoint;
        _client = client;
        _closed = _lifetime.Token;
    }

    public static async Task<IRouterSessionService> OpenAsync(RouterEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        var client = await RouterOsClient.ConnectAsync(endpoint, cancellationToken);
        return new RouterSessionService(endpoint, client);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> CommandAsync(
        string command, IReadOnlyDictionary<string, string>? attributes = null,
        CancellationToken cancellationToken = default) =>
        (await CommandWithReceiptAsync(command, attributes, cancellationToken)).Records;

    public Task<RouterCommandReply> CommandWithReceiptAsync(
        string command, IReadOnlyDictionary<string, string>? attributes = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(command.EndsWith("/print", StringComparison.Ordinal),
            (client, token) => client.CommandWithReceiptAsync(command, attributes, token),
            cancellationToken);

    public Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> QueryByIdAsync(
        string command, string id, IReadOnlyDictionary<string, string>? attributes = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(true, (client, token) => client.QueryByIdAsync(command, id, attributes, token),
            cancellationToken);

    private async Task<T> ExecuteAsync<T>(bool canRetry,
        Func<RouterOsClient, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_closed, cancellationToken);
        var token = request.Token;
        await _gate.WaitAsync(token);
        try
        {
            var client = await GetClientAsync(token);
            try
            {
                return await operation(client, token);
            }
            catch (Exception) when (canRetry && !client.IsUsable && !token.IsCancellationRequested)
            {
                // An incomplete write may already have committed on RouterOS.
                // Only reads can be replayed, once, on a fresh transport.
                client = await GetClientAsync(token);
                return await operation(client, token);
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<RouterOsClient> GetClientAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_client is { IsUsable: true } current) return current;
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
        _client = await RouterOsClient.ConnectAsync(_endpoint, token);
        return _client;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closing, 1) != 0) return;
        _lifetime.Cancel();
        await _gate.WaitAsync();
        try
        {
            if (_client is not null) await _client.DisposeAsync();
            _client = null;
        }
        finally
        {
            _gate.Release();
            _lifetime.Dispose();
        }
    }
}
