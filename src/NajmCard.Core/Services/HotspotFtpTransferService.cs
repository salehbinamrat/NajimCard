using System.IO.Compression;
using System.Text;

namespace NajmCard.Core.Services;

public sealed record HotspotFtpConnection(
    string Host,int Port,string Username,string Password,
    bool UseTls=true,bool AllowPlainFtp=false);

public sealed record HotspotFtpEntry(string Name,bool IsDirectory);
public sealed record HotspotFtpDownloadResult(int Files,string LocalFolder);
public sealed record HotspotFtpUploadResult(int UploadedFiles,int BackupFiles,string BackupArchive);

/// <summary>
/// Explicitly bounded, testable router FTP interface.
/// In production the adapter uses short-lived FTP requests. All router
/// credentials are in memory only and must never be written to logs/ZIPs.
/// </summary>
public interface IHotspotFtpSession:IAsyncDisposable
{
    Task<IReadOnlyList<HotspotFtpEntry>> ListAsync(
        string path,CancellationToken ct=default);
    Task DownloadAsync(string path,Stream output,CancellationToken ct=default);
    Task UploadAsync(string path,Stream input,CancellationToken ct=default);
    Task EnsureDirectoryAsync(string path,CancellationToken ct=default);
}

public interface IHotspotFtpConnector
{
    Task<IHotspotFtpSession> OpenAsync(
        HotspotFtpConnection info,CancellationToken ct=default);
}

/// <summary>
/// RouterOS FTP transfer orchestration:
/// - download to a NEW staging directory and publish only after full success;
/// - before ANY upload, download ALL existing Hotspot assets to an immutable
///   user-selected backup ZIP; stop if listing, reading or backup fails;
/// - make no assumptions that an interrupted upload succeeded, never auto-retry.
/// A dedicated Hotspot remote directory (hotspot, flash/hotspot or disk1/hotspot)
/// is mandatory; the router filesystem root is NEVER an acceptable destination.
/// </summary>
public sealed class HotspotFtpTransferService(IHotspotFtpConnector connector)
{
    private const int MaxFiles=2000,MaxDepth=16;
    private const long MaxFileBytes=32L*1024*1024,MaxTotalBytes=256L*1024*1024;
    private const string BackupSuffix=".zip";
    private static readonly StringComparer Names=StringComparer.OrdinalIgnoreCase;

    private static void ValidateConnection(HotspotFtpConnection info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if(string.IsNullOrWhiteSpace(info.Host)||info.Host.Length>253 ||
            info.Host.Any(x=>char.IsControl(x)||char.IsWhiteSpace(x)||
                x is '/' or '\\' or '@' or '#' or '?')||
            info.Port is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(info.Username)||info.Username.Length>128||
            info.Username.Any(char.IsControl)||
            info.Password is null)
            throw new ArgumentException("إعدادات اتصال Hotspot FTP غير صالحة.");
        if(!info.UseTls && !info.AllowPlainFtp)
            throw new InvalidOperationException(
                "FTP غير المشفر ينقل كلمة المرور دون تشفير؛ يلزم موافقة صريحة.");
    }

    /// <summary>Restrict all remote paths to a named Hotspot directory.</summary>
    public static string ValidateRemoteRoot(string? root)
    {
        if(string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("حدد مسار Hotspot على الراوتر.",nameof(root));
        if(root.StartsWith('/')||root.Contains('\\')||root.EndsWith('/'))
            throw new ArgumentException("مسار Hotspot غير آمن.",nameof(root));
        var parts=root.Split('/');
        if(parts.Length>MaxDepth || parts.Any(x=>!ValidSegment(x)) ||
           !parts[^1].Equals("hotspot",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "المسار يجب أن ينتهي بمجلد hotspot المحدد لا بجذر الراوتر.",
                nameof(root));
        return string.Join("/",parts);
    }

    private static bool ValidSegment(string name)
    {
        if(string.IsNullOrWhiteSpace(name)||name is "." or ".."||
            name.Length>180||name.EndsWith(' ')||name.EndsWith('.'))
            return false;
        if(name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|'])>=0||
            name.Any(char.IsControl))return false;
        string reserved=name.Split('.')[0].ToUpperInvariant();
        return reserved is not ("CON" or "PRN" or "AUX" or "NUL")&&
               !(reserved.Length==4 && (reserved.StartsWith("COM")||
                reserved.StartsWith("LPT"))&&
                reserved[3] is >= '1' and <= '9');
    }

    private static string ValidateRelative(string path)
    {
        if(string.IsNullOrWhiteSpace(path)||path.StartsWith('/')||
            path.Contains('\\')||path.Contains(':'))
            throw new InvalidDataException("الراوتر أعاد مسار ملف غير آمن.");
        var parts=path.Split('/');
        if(parts.Length>MaxDepth||parts.Any(x=>!ValidSegment(x)))
            throw new InvalidDataException("رفض اسم ملف خارج نطاق مجلد Hotspot.");
        return string.Join("/",parts);
    }

    private static async Task<IReadOnlyList<string>> EnumerateAsync(
        IHotspotFtpSession session,string remoteRoot,CancellationToken ct)
    {
        var entries=new List<string>();
        var seen=new HashSet<string>(Names);
        async Task Walk(string dir,string relative,int depth)
        {
            if(depth>MaxDepth)
                throw new InvalidDataException("تجاوز مجلد FTP الحد الآمن للعمق.");
            var rows=await session.ListAsync(dir,ct);
            foreach(var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                if(!ValidSegment(row.Name))
                    throw new InvalidDataException(
                        "قائمة FTP تحتوي اسماً غير آمن؛ تم إيقاف النقل.");
                string name=relative.Length==0?row.Name:relative+"/"+row.Name;
                ValidateRelative(name);
                if(row.IsDirectory)
                {
                    if(depth>=MaxDepth)
                        throw new InvalidDataException("مجلدات FTP متداخلة أكثر من المسموح.");
                    await Walk(dir+"/"+row.Name,name,depth+1);
                }
                else
                {
                    if(!seen.Add(name))
                        throw new InvalidDataException("أسماء ملفات FTP مكررة أو متعارضة.");
                    entries.Add(name);
                    if(entries.Count>MaxFiles)
                        throw new InvalidDataException("عدد ملفات FTP أكبر من الحد الآمن.");
                }
            }
        }
        await Walk(remoteRoot,"",0);
        if(!entries.Any(x=>x.Equals("login.html",StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(
                "لم يُعثر على login.html في مجلد Hotspot للراوتر. أُوقف النقل.");
        return entries.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task<int> FetchAllAsync(
        IHotspotFtpSession session,string root,IReadOnlyList<string> files,
        string directory,CancellationToken ct)
    {
        long total=0;
        foreach(var relative in files)
        {
            ct.ThrowIfCancellationRequested();
            ValidateRelative(relative);
            var local=Path.Combine(directory,relative.Replace('/',
                Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(local)!);
            await using var file=new FileStream(local,FileMode.CreateNew,
                FileAccess.Write,FileShare.None,64*1024,FileOptions.Asynchronous);
            await using(var capped=new CappedWriteStream(file,MaxFileBytes))
            {
                await session.DownloadAsync(root+"/"+relative,capped,ct);
                await capped.FlushAsync(ct);
                total=checked(total+capped.Bytes);
            }
            if(total>MaxTotalBytes)
                throw new InvalidDataException("حجم ملفات الراوتر يتجاوز الحد الآمن.");
        }
        return files.Count;
    }

    public async Task<HotspotFtpDownloadResult> DownloadSnapshotAsync(
        HotspotFtpConnection connection,string remoteRoot,string destinationParent,
        CancellationToken ct=default)
    {
        ValidateConnection(connection);
        var root=ValidateRemoteRoot(remoteRoot);
        if(string.IsNullOrWhiteSpace(destinationParent)||
            !Directory.Exists(destinationParent))
            throw new DirectoryNotFoundException("اختر مجلداً محلياً موجوداً للاستلام.");
        var parent=Path.GetFullPath(destinationParent);
        await using var ftp=await connector.OpenAsync(connection,ct);
        var paths=await EnumerateAsync(ftp,root,ct);
        var nonce=Guid.NewGuid().ToString("N");
        var staging=Path.Combine(parent,".najm-ftp-staging-"+nonce);
        var published=Path.Combine(parent,"NajmCard-Hotspot-"+
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+nonce[..8]);
        try
        {
            Directory.CreateDirectory(staging);
            await FetchAllAsync(ftp,root,paths,staging,ct);
            ct.ThrowIfCancellationRequested();
            Directory.Move(staging,published);
            return new HotspotFtpDownloadResult(paths.Count,published);
        }
        finally
        {
            if(Directory.Exists(staging))Directory.Delete(staging,true);
        }
    }

    public async Task<HotspotFtpUploadResult> UploadWithBackupAsync(
        HotspotFtpConnection connection,HotspotTemplateWorkspace template,
        string remoteRoot,string backupArchive,CancellationToken ct=default)
    {
        ValidateConnection(connection);
        ArgumentNullException.ThrowIfNull(template);
        var root=ValidateRemoteRoot(remoteRoot);
        if(string.IsNullOrWhiteSpace(backupArchive)||
            !backupArchive.EndsWith(BackupSuffix,StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("حدد ملف ZIP منفصلاً لنسخة الراوتر قبل الرفع.");
        var backup=Path.GetFullPath(backupArchive);
        if(File.Exists(backup)||Directory.Exists(backup))
            throw new IOException(
                "ملف النسخة الاحتياطية موجود بالفعل؛ اختر اسماً جديداً لمنع الاستبدال.");
        var parent=Path.GetDirectoryName(backup)!;
        if(!Directory.Exists(parent))
            throw new DirectoryNotFoundException("مجلد حفظ النسخة الاحتياطية غير موجود.");
        string nonce=Guid.NewGuid().ToString("N");
        var localZip=Path.Combine(parent,".najm-upload-snapshot-"+nonce+".zip");
        var remoteStage=Path.Combine(parent,".najm-remote-backup-"+nonce);
        var backupTemp=Path.Combine(parent,".najm-router-before-"+nonce+".tmp");
        try
        {
            // The validated local snapshot is immutable throughout FTP transfer.
            // ExportZip also rejects backing up into the template source tree.
            var localCount=template.ExportZip(localZip);
            if(localCount<=0)
                throw new InvalidDataException("القالب المحلي فارغ.");
            await using var ftp=await connector.OpenAsync(connection,ct);
            var remoteFiles=await EnumerateAsync(ftp,root,ct);
            Directory.CreateDirectory(remoteStage);
            await FetchAllAsync(ftp,root,remoteFiles,remoteStage,ct);
            ct.ThrowIfCancellationRequested();

            // All files are now saved locally, before the FIRST remote write.
            ZipFile.CreateFromDirectory(remoteStage,backupTemp,
                CompressionLevel.Optimal,includeBaseDirectory:false);
            File.Move(backupTemp,backup,overwrite:false);

            using var localArchive=ZipFile.OpenRead(localZip);
            if(localArchive.Entries.Count!=localCount)
                throw new InvalidDataException("تغير عدد ملفات القالب المحلي أثناء تجهيز النقل.");
            var created=new HashSet<string>(Names);
            var uploaded=0;
            foreach(var item in localArchive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                var relative=ValidateRelative(item.FullName);
                if(item.Length>MaxFileBytes)
                    throw new InvalidDataException("حجم مورد Hotspot أكبر من حد الرفع.");
                var segments=relative.Split('/');
                if(segments.Length>1)
                {
                    var directory=root;
                    foreach(var segment in segments[..^1])
                    {
                        directory+="/"+segment;
                        if(created.Add(directory))
                            await ftp.EnsureDirectoryAsync(directory,ct);
                    }
                }
                await using var input=item.Open();
                await ftp.UploadAsync(root+"/"+relative,input,ct);
                uploaded++;
            }
            return new HotspotFtpUploadResult(uploaded,remoteFiles.Count,backup);
        }
        finally
        {
            // The durable remote backup must survive even if FTP upload fails.
            if(File.Exists(localZip))File.Delete(localZip);
            if(File.Exists(backupTemp))File.Delete(backupTemp);
            if(Directory.Exists(remoteStage))Directory.Delete(remoteStage,true);
        }
    }

    /// <summary>Enforces a per-file size even if an untrusted FTP peer streams forever.</summary>
    private sealed class CappedWriteStream(Stream inner,long cap):Stream
    {
        public long Bytes {get;private set;}
        public override bool CanRead=>false;
        public override bool CanSeek=>false;
        public override bool CanWrite=>true;
        public override long Length=>Bytes;
        public override long Position {get=>Bytes;set=>throw new NotSupportedException();}
        public override void Flush()=>inner.Flush();
        public override Task FlushAsync(CancellationToken ct)=>inner.FlushAsync(ct);
        private void Check(int n)
        {
            if(n<0||Bytes>cap-n)
                throw new InvalidDataException("حجم ملف FTP تجاوز الحد المسموح.");
            Bytes+=n;
        }
        public override void Write(byte[] buffer,int offset,int count)
        {
            Check(count);inner.Write(buffer,offset,count);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Check(buffer.Length);inner.Write(buffer);
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken ct=default)
        {
            Check(buffer.Length);return inner.WriteAsync(buffer,ct);
        }
        public override Task WriteAsync(byte[] buffer,int offset,int count,
            CancellationToken ct)
        {
            Check(count);return inner.WriteAsync(buffer,offset,count,ct);
        }
        public override int Read(byte[] buffer,int offset,int count)=>
            throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>
            throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if(disposing)inner.Dispose();
            base.Dispose(disposing);
        }
        public override ValueTask DisposeAsync()=>inner.DisposeAsync();
    }
}
