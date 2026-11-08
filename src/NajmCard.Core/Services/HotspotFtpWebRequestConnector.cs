using System.Net;
using System.Text;

namespace NajmCard.Core.Services;

/// <summary>
/// FTP/explicit-FTPS transport compatible with RouterOS 6 file services.
/// Uses .NET legacy FTP protocol implementation because RouterOS 6 does not
/// offer SFTP. Plain FTP is forbidden unless the operator explicitly opts in.
/// Credentials are not embedded in URLs, logs, backups or exception messages.
/// </summary>
public sealed class HotspotFtpWebRequestConnector : IHotspotFtpConnector
{
    public Task<IHotspotFtpSession> OpenAsync(
        HotspotFtpConnection info,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        if(info is null||string.IsNullOrWhiteSpace(info.Host)||
            info.Port is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(info.Username) ||
            (!info.UseTls && !info.AllowPlainFtp))
            throw new ArgumentException(
                "بيانات FTP ناقصة أو الاتصال غير المشفر غير مصرح به.");
        return Task.FromResult<IHotspotFtpSession>(
            new HotspotFtpWebRequestSession(info));
    }
}

internal sealed class HotspotFtpWebRequestSession(HotspotFtpConnection credentials)
    : IHotspotFtpSession
{
    private const int TimeoutMilliseconds=30_000;
    private const int TransferBuffer=64*1024;
    private bool _disposed;

#pragma warning disable SYSLIB0014
    private FtpWebRequest Request(string relative,string method)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        // The service validates path segments. Never include credentials in the URI.
        var parts=relative.Split('/');
        if(parts.Length==0||parts.Any(x=>x.Length==0||x is "." or ".."||
            x.IndexOfAny(['/', '\\', ':', '?', '#', '@', '%'])>=0||
            x.Any(char.IsControl)))
            throw new ArgumentException("مسار FTP غير صالح.");
        var uri=new UriBuilder(Uri.UriSchemeFtp,credentials.Host,credentials.Port)
        {
            Path="/"+string.Join("/",parts.Select(Uri.EscapeDataString))
        }.Uri;
        var request=(FtpWebRequest)WebRequest.Create(uri);
        request.Method=method;
        request.Credentials=new NetworkCredential(
            credentials.Username,credentials.Password);
        request.UseBinary=true;
        request.UsePassive=true;
        request.KeepAlive=false;
        request.EnableSsl=credentials.UseTls;
        request.Timeout=TimeoutMilliseconds;
        request.ReadWriteTimeout=TimeoutMilliseconds;
        return request;
    }
#pragma warning restore SYSLIB0014

    private static async Task<T> WithCancellation<T>(
        FtpWebRequest req,Func<Task<T>> operation,CancellationToken ct)
    {
        using var cancel=ct.Register(static obj=>((FtpWebRequest)obj!).Abort(),req);
        ct.ThrowIfCancellationRequested();
        return await operation();
    }

    private static bool IsMissingFile(WebException e) =>
        e.Response is FtpWebResponse ftp &&
        ftp.StatusCode==FtpStatusCode.ActionNotTakenFileUnavailable;

    private async Task<string[]> ReadNamesAsync(string directory,CancellationToken ct)
    {
        var request=Request(directory,WebRequestMethods.Ftp.ListDirectory);
        using var response=await WithCancellation(request,
            async()=>(FtpWebResponse)await request.GetResponseAsync(),ct);
        using var source=response.GetResponseStream()
            ?? throw new IOException("تعذر قراءة قائمة ملفات FTP.");
        using var reader=new StreamReader(source,new UTF8Encoding(false,true),
            detectEncodingFromByteOrderMarks:true);
        var names=new List<string>();
        while(!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var raw=await reader.ReadLineAsync(ct);
            if(string.IsNullOrEmpty(raw))continue;
            // NLST may return "file", "/hotspot/file" or "hotspot/file"
            // depending on RouterOS version. Strip ONLY the requested prefix.
            var text=raw.TrimEnd('\r');
            var prefix=directory.TrimEnd('/')+"/";
            if(text.StartsWith("/"+prefix,StringComparison.Ordinal))
                text=text[1..];
            if(text.StartsWith(prefix,StringComparison.Ordinal))
                text=text[prefix.Length..];
            if(text.Length==0||text is "." or ".."||text.Contains('/')||
                text.Contains('\\')||text.Any(char.IsControl))
                throw new InvalidDataException(
                    "الراوتر أعاد اسم ملف FTP غير آمن أو غير متوقع.");
            names.Add(text);
            if(names.Count>2500)
                throw new InvalidDataException("نتيجة قائمة FTP أكبر من الحد الآمن.");
        }
        return names.ToArray();
    }

    private async Task<bool> IsDirectoryAsync(string path,CancellationToken ct)
    {
        // RouterOS6 supports SIZE for files; for directories it returns 550.
        // In that case, NLST must positively confirm the directory. Do not
        // assume an arbitrary 550 response is sufficient to classify it.
        var req=Request(path,WebRequestMethods.Ftp.GetFileSize);
        try
        {
            using var response=await WithCancellation(req,
                async()=>(FtpWebResponse)await req.GetResponseAsync(),ct);
            return false;
        }
        catch(WebException ex) when(IsMissingFile(ex))
        {
            try
            {
                _=await ReadNamesAsync(path,ct);
                return true;
            }
            catch(WebException probe) when(IsMissingFile(probe))
            {
                throw new InvalidDataException(
                    "تعذر التأكد من نوع الملف في قائمة FTP؛ أُوقف النقل.");
            }
        }
    }

    public async Task<IReadOnlyList<HotspotFtpEntry>> ListAsync(
        string path,CancellationToken ct=default)
    {
        var names=await ReadNamesAsync(path,ct);
        var result=new List<HotspotFtpEntry>();
        foreach(var name in names)
        {
            ct.ThrowIfCancellationRequested();
            result.Add(new HotspotFtpEntry(name,
                await IsDirectoryAsync(path+"/"+name,ct)));
        }
        return result;
    }

    public async Task DownloadAsync(string path,Stream output,CancellationToken ct=default)
    {
        var request=Request(path,WebRequestMethods.Ftp.DownloadFile);
        using var response=await WithCancellation(request,
            async()=>(FtpWebResponse)await request.GetResponseAsync(),ct);
        await using var stream=response.GetResponseStream() ??
            throw new IOException("تعذر فتح ملف FTP للتنزيل.");
        await stream.CopyToAsync(output,TransferBuffer,ct);
    }

    public async Task UploadAsync(string path,Stream input,CancellationToken ct=default)
    {
        var request=Request(path,WebRequestMethods.Ftp.UploadFile);
        using var cancel=ct.Register(static obj=>((FtpWebRequest)obj!).Abort(),request);
        ct.ThrowIfCancellationRequested();
        await using(var stream=await request.GetRequestStreamAsync())
        {
            await input.CopyToAsync(stream,TransferBuffer,ct);
            await stream.FlushAsync(ct);
        }
        using var response=(FtpWebResponse)await request.GetResponseAsync();
    }

    public async Task EnsureDirectoryAsync(string path,CancellationToken ct=default)
    {
        try
        {
            _=await ReadNamesAsync(path,ct);
            return;
        }
        catch(WebException ex) when(IsMissingFile(ex))
        {
            // A missing directory may be created only AFTER the complete
            // backup phase, as enforced in HotspotFtpTransferService.
        }

        var request=Request(path,WebRequestMethods.Ftp.MakeDirectory);
        using(var response=await WithCancellation(request,
            async()=>(FtpWebResponse)await request.GetResponseAsync(),ct))
        {
            // No body expected.
        }
        _=await ReadNamesAsync(path,ct);
    }

    public ValueTask DisposeAsync()
    {
        _disposed=true;
        return ValueTask.CompletedTask;
    }
}
