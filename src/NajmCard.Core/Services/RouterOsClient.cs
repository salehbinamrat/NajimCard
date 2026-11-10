using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

public sealed class RouterOsTrapException(string message) : IOException(message);

/// <summary>Sequential, disposable RouterOS v6 API connection. No command or credential logging.</summary>
public interface IRouterCommandClient
{
    Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> CommandAsync(
        string command, IReadOnlyDictionary<string, string>? attributes = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
  /// RouterOS commands for which the !done attributes (especially =ret=)
  /// must not be discarded after a state-changing operation.
  /// </summary>
public interface IRouterReceiptClient
{
    Task<RouterCommandReply> CommandWithReceiptAsync(
        string command, IReadOnlyDictionary<string, string>? attributes = null,
        CancellationToken cancellationToken = default);
}

public sealed class RouterOsClient : IAsyncDisposable, IRouterReceiptClient, IRouterCommandClient, IRouterQueryClient
{
    private readonly TcpClient _tcp;
    private readonly Stream _stream;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;
    internal bool IsUsable => !_disposed;

    private RouterOsClient(TcpClient tcp, Stream stream) { _tcp = tcp; _stream = stream; }

    public static async Task<RouterOsClient> ConnectAsync(RouterEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        endpoint.Validate();
        var tcp = new TcpClient();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(12));
        Stream? stream = null;
        try
        {
            await tcp.ConnectAsync(endpoint.Host, endpoint.Port, limit.Token);
            stream = tcp.GetStream();
            if (endpoint.UseTls)
            {
                var tls = new SslStream(stream, false); // system CA validation, never accept invalid certificate
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = endpoint.Host,
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.None
                }, limit.Token);
                stream = tls;
            }
            var client = new RouterOsClient(tcp, stream);
            await client.AuthenticateAsync(endpoint.Username, endpoint.Password, limit.Token);
            return client;
        }
        catch
        {
            stream?.Dispose(); tcp.Dispose(); throw;
        }
    }

    private async Task AuthenticateAsync(string username, string password, CancellationToken token)
    {
        var response = await ExchangeAsync(["/login", "=name=" + username, "=password=" + password], token);
        var challenge = response.SelectMany(x => x).FirstOrDefault(x => x.StartsWith("=ret=", StringComparison.Ordinal));
        if (challenge is null) return;
        byte[] nonce;
        try { nonce = Convert.FromHexString(challenge[5..]); }
        catch (FormatException) { throw new InvalidDataException("Invalid RouterOS legacy login challenge"); }
        byte[] credential = [0, .. Encoding.UTF8.GetBytes(password), .. nonce];
        string digest = Convert.ToHexString(MD5.HashData(credential)).ToLowerInvariant();
        await ExchangeAsync(["/login", "=name=" + username, "=response=00" + digest], token);
    }

    private async Task<List<IReadOnlyList<string>>> ExchangeAsync(IReadOnlyList<string> words, CancellationToken token)
    {
        await _lock.WaitAsync(token);
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RouterOsClient));
            if (words.Count == 0)
                throw new ArgumentException("RouterOS command cannot be empty.", nameof(words));
            var policy = RouterOsCommandTimeoutPolicy.For(words[0]);
            using var total = CancellationTokenSource.CreateLinkedTokenSource(token);
            total.CancelAfter(policy.Total);
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
            idle.CancelAfter(policy.Idle);
            await RouterOsProtocol.WriteSentenceAsync(_stream, words, idle.Token);
            var replies = new List<IReadOnlyList<string>>();
            string? trapped = null;
            long responseBytes = 0;
            while (true)
            {
                var sentence = await RouterOsProtocol.ReadSentenceAsync(_stream, idle.Token);
                // Reset the idle timer after each complete sentence, without
                // extending the hard total deadline for bulk User Manager scans.
                idle.CancelAfter(policy.Idle);
                responseBytes += sentence.Sum(word => Encoding.UTF8.GetByteCount(word));
                if (responseBytes > policy.MaxResponseBytes)
                    throw new InvalidDataException("RouterOS response exceeded the configured size limit");
                if (sentence.Count == 0) throw new InvalidDataException("Empty RouterOS response");
                string kind = sentence[0];
                if (kind == "!fatal")
                {
                    string msg = sentence.FirstOrDefault(w =>
                        w.StartsWith("=message=", StringComparison.Ordinal))?[9..]
                        ?? "RouterOS connection was terminated";
                    _disposed = true;
                    _stream.Dispose();
                    _tcp.Dispose();
                    throw new RouterOsTrapException(msg);
                }
                if (kind == "!trap")
                {
                    // RouterOS 6 follows !trap with !done. Consume !done before
                    // releasing the command lock or another call would wrongly
                    // accept the previous command's final response as its own.
                    trapped ??= sentence.FirstOrDefault(w =>
                        w.StartsWith("=message=", StringComparison.Ordinal))?[9..]
                        ?? "RouterOS rejected the operation";
                    continue;
                }
                replies.Add(sentence);
                if (kind == "!done")
                {
                    if (trapped is not null) throw new RouterOsTrapException(trapped);
                    return replies;
                }
                if (replies.Count > policy.MaxRows)
                    throw new InvalidDataException("Too many RouterOS records");
            }
        }
        catch (Exception ex) when (
            ex is OperationCanceledException ||
            (ex is IOException && ex is not RouterOsTrapException) ||
            ex is SocketException or InvalidDataException or ObjectDisposedException)
        {
            // An incomplete reply leaves arbitrary sentences on the TCP stream.
            // Fail closed: the next call must reconnect rather than consume stale data.
            _disposed = true;
            _stream.Dispose();
            _tcp.Dispose();
            throw;
        }
        finally { _lock.Release(); }
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> CommandAsync(
        string command, IReadOnlyDictionary<string, string>? attributes = null,
        CancellationToken cancellationToken = default) =>
        (await CommandWithReceiptAsync(command, attributes, cancellationToken)).Records;

    public async Task<RouterCommandReply> CommandWithReceiptAsync(
        string command, IReadOnlyDictionary<string, string>? attributes = null,
        CancellationToken cancellationToken = default)
    {
        RouterOsCommandPolicy.Validate(command, attributes);
        var words = new List<string> { command };
        if (attributes is not null)
            foreach (var (key, value) in attributes)
            {
                if (string.IsNullOrWhiteSpace(key) || key.StartsWith('=') || key.Contains('='))
                    throw new ArgumentException("Invalid RouterOS property name");
                words.Add("=" + key + "=" + value);
            }
        var sentences = await ExchangeAsync(words, cancellationToken);
        return RouterOsReplyDecoder.Decode(sentences);
    }

    /// <summary>
    /// RouterOS API query word ?.id=*HEX. Never accept arbitrary query text:
    /// the user-selected .id must represent exactly one internal record.
    /// </summary>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByIdAsync(
        string command, string id, IReadOnlyDictionary<string,string>? attributes = null,
        CancellationToken cancellationToken = default)
    {
        if (command is not ("/tool/user-manager/user/print" or "/ppp/secret/print" or "/ppp/active/print" or "/ppp/profile/print" or
            "/ip/hotspot/print" or "/ip/hotspot/active/print" or
            "/ip/hotspot/user/profile/print"))
            throw new ArgumentException(
                "الاستعلام المقيّد بمعرّف واحد مخصص لحسابات User Manager v6 وPPP فقط.",
                nameof(command));
        if (string.IsNullOrEmpty(id) || id.Length is < 2 or > 40 ||
            id[0] != '*' || id.Skip(1).Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("معرّف حساب RouterOS غير صالح.", nameof(id));
        RouterOsCommandPolicy.Validate(command, attributes);
        var words = new List<string> { command };
        if (attributes is not null)
            foreach (var (key,value) in attributes)
            {
                if (string.IsNullOrWhiteSpace(key) || key.Contains('='))
                    throw new ArgumentException("معامل قراءة RouterOS غير صالح.", nameof(attributes));
                words.Add("=" + key + "=" + value);
            }
        words.Add("?.id=" + id);
        return RouterOsReplyDecoder.Decode(
            await ExchangeAsync(words, cancellationToken)).Records;
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await _stream.DisposeAsync();
        _tcp.Dispose();
        _lock.Dispose();
    }
}
