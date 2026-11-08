using System.IO.Compression;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>
/// Real-file backup/restore gates and a deterministic FTP protocol boundary.
/// No live router is modified in these tests.
/// </summary>
public sealed class HotspotFtpTransferTests
{
    private static string Temp()
    {
        var path=Path.Combine(Path.GetTempPath(),"najm-ftp-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);return path;
    }
    private static HotspotFtpConnection Connection(bool allow=true) =>
        new("192.0.2.10",21,"admin","test-secret",UseTls:false,AllowPlainFtp:allow);

    private sealed class FakeFtp : IHotspotFtpSession
    {
        public readonly Dictionary<string,byte[]> Files=new(StringComparer.Ordinal);
        public readonly List<string> Events=[];
        public bool FailDownload,FailUpload;
        public Task<IReadOnlyList<HotspotFtpEntry>> ListAsync(string path,CancellationToken ct=default)
        {
            Events.Add("LIST "+path);
            var prefix=path.TrimEnd('/')+"/";
            var names=new Dictionary<string,bool>(StringComparer.Ordinal);
            foreach(var key in Files.Keys.Where(x=>x.StartsWith(prefix,StringComparison.Ordinal)))
            {
                var remaining=key[prefix.Length..];
                var item=remaining.Split('/')[0];
                if(item.Length>0)names[item]=remaining.Contains('/');
            }
            IReadOnlyList<HotspotFtpEntry> result=names.Select(x=>
                new HotspotFtpEntry(x.Key,x.Value)).ToArray();
            return Task.FromResult(result);
        }
        public async Task DownloadAsync(string path,Stream output,CancellationToken ct=default)
        {
            Events.Add("GET "+path);
            if(FailDownload)throw new IOException("simulated FTP data failure");
            await output.WriteAsync(Files[path],ct);
        }
        public async Task UploadAsync(string path,Stream input,CancellationToken ct=default)
        {
            Events.Add("PUT "+path);
            if(FailUpload)throw new IOException("simulated FTP write failure");
            using var ms=new MemoryStream();
            await input.CopyToAsync(ms,ct);
            Files[path]=ms.ToArray();
        }
        public Task EnsureDirectoryAsync(string path,CancellationToken ct=default)
        {
            Events.Add("MKDIR "+path);
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    private sealed class FakeConnector(FakeFtp ftp):IHotspotFtpConnector
    {
        public int Connections;
        public Task<IHotspotFtpSession> OpenAsync(HotspotFtpConnection info,CancellationToken ct=default)
        {
            Connections++;
            return Task.FromResult<IHotspotFtpSession>(ftp);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("..")]
    [InlineData("hotspot/../files")]
    [InlineData("other")]
    [InlineData("hotspot\\..\\secret")]
    public async Task Remote_Root_Must_Explicitly_Target_Hotspot_Not_Router_Root(string remote)
    {
        var fake=new FakeFtp();var connector=new FakeConnector(fake);
        var service=new HotspotFtpTransferService(connector);
        var root=Temp();
        try
        {
            await Assert.ThrowsAnyAsync<ArgumentException>(()=>
                service.DownloadSnapshotAsync(Connection(),remote,root));
            Assert.Equal(0,connector.Connections);
        }
        finally {Directory.Delete(root,true);}
    }

    [Fact]
    public async Task Plain_Ftp_Requires_Explicit_Consent_Before_Any_Connection()
    {
        var connector=new FakeConnector(new FakeFtp());
        var root=Temp();
        try
        {
            await Assert.ThrowsAnyAsync<InvalidOperationException>(()=>
                new HotspotFtpTransferService(connector)
                    .DownloadSnapshotAsync(Connection(false),"hotspot",root));
            Assert.Equal(0,connector.Connections);
        }
        finally {Directory.Delete(root,true);}
    }

    [Fact]
    public async Task Downloads_Nested_Assets_Into_New_Atomic_Snapshot_Not_An_Existing_Template()
    {
        var ftp=new FakeFtp();
        ftp.Files["hotspot/login.html"]=System.Text.Encoding.UTF8.GetBytes("$(username)");
        ftp.Files["hotspot/css/style.css"]=[1,2,3];
        ftp.Files["hotspot/images/logo.webp"]=[9,8,7];
        var root=Temp();
        try
        {
            var existing=Path.Combine(root,"login.html");
            File.WriteAllText(existing,"keep unchanged");
            var result=await new HotspotFtpTransferService(new FakeConnector(ftp))
                .DownloadSnapshotAsync(Connection(),"hotspot",root);
            Assert.Equal(3,result.Files);
            Assert.True(Directory.Exists(result.LocalFolder));
            Assert.Equal("$(username)",File.ReadAllText(Path.Combine(result.LocalFolder,"login.html")));
            Assert.Equal(new byte[]{9,8,7},File.ReadAllBytes(Path.Combine(result.LocalFolder,"images","logo.webp")));
            Assert.Equal("keep unchanged",File.ReadAllText(existing));
        }
        finally {Directory.Delete(root,true);}
    }

    [Fact]
    public async Task Upload_Backs_Up_Remote_Files_Before_Any_Change_And_Preserves_Existing_Router_Backup()
    {
        var parent=Temp();
        try
        {
            var local=Path.Combine(parent,"local");
            Directory.CreateDirectory(local);
            File.WriteAllText(Path.Combine(local,"login.html"),"new $(username)");
            File.WriteAllText(Path.Combine(local,"alogin.html"),"new alogin");
            var workspace=HotspotTemplateWorkspace.Open(local);
            var backup=Path.Combine(parent,"router-before-upload.zip");
            var ftp=new FakeFtp();
            ftp.Files["hotspot/login.html"]=System.Text.Encoding.UTF8.GetBytes("old login");
            ftp.Files["hotspot/assets/original.png"]=[1,2,3];
            var result=await new HotspotFtpTransferService(new FakeConnector(ftp))
                .UploadWithBackupAsync(Connection(),workspace,"hotspot",backup);
            Assert.Equal(2,result.UploadedFiles);
            Assert.Equal(2,result.BackupFiles);
            Assert.True(File.Exists(backup));
            using var zip=ZipFile.OpenRead(backup);
            Assert.Equal(2,zip.Entries.Count);
            using var sr=new StreamReader(zip.GetEntry("login.html")!.Open());
            Assert.Equal("old login",sr.ReadToEnd());
            Assert.Equal("new $(username)",System.Text.Encoding.UTF8.GetString(ftp.Files["hotspot/login.html"]));
            Assert.True(ftp.Events.FindIndex(x=>x.StartsWith("GET ")) <
                ftp.Events.FindIndex(x=>x.StartsWith("PUT ")));
            Assert.True(ftp.Events.FindLastIndex(x=>x.StartsWith("GET ")) <
                ftp.Events.FindIndex(x=>x.StartsWith("PUT ")));
        }
        finally {Directory.Delete(parent,true);}
    }

    [Fact]
    public async Task Failed_Remote_Backup_Stops_Upload_Without_Any_Ftp_Write()
    {
        var parent=Temp();
        try
        {
            var local=Path.Combine(parent,"local");
            Directory.CreateDirectory(local);
            File.WriteAllText(Path.Combine(local,"login.html"),"new");
            var workspace=HotspotTemplateWorkspace.Open(local);
            var ftp=new FakeFtp{FailDownload=true};
            ftp.Files["hotspot/login.html"]=System.Text.Encoding.UTF8.GetBytes("old");
            await Assert.ThrowsAnyAsync<IOException>(()=>
                new HotspotFtpTransferService(new FakeConnector(ftp))
                    .UploadWithBackupAsync(Connection(),workspace,"hotspot",
                        Path.Combine(parent,"backup.zip")));
            Assert.DoesNotContain(ftp.Events,x=>x.StartsWith("PUT ")||x.StartsWith("MKDIR "));
            Assert.Equal("old",System.Text.Encoding.UTF8.GetString(ftp.Files["hotspot/login.html"]));
        }
        finally {Directory.Delete(parent,true);}
    }

    [Fact]
    public async Task Rejects_Directory_Traversal_In_Listing_And_Leaves_No_Download_Snapshot()
    {
        var ftp=new FakeFtpWithBadName();
        var root=Temp();
        try
        {
            await Assert.ThrowsAnyAsync<InvalidDataException>(()=>
                new HotspotFtpTransferService(new BadConnector(ftp))
                    .DownloadSnapshotAsync(Connection(),"hotspot",root));
            Assert.Empty(Directory.GetDirectories(root));
        }
        finally {Directory.Delete(root,true);}
    }
    private sealed class FakeFtpWithBadName:IHotspotFtpSession
    {
        public Task<IReadOnlyList<HotspotFtpEntry>> ListAsync(string path,CancellationToken ct=default)=>
            Task.FromResult<IReadOnlyList<HotspotFtpEntry>>([new("../outside.html",false)]);
        public Task DownloadAsync(string path,Stream output,CancellationToken ct=default)=>
            throw new InvalidOperationException("Must reject before download");
        public Task UploadAsync(string path,Stream input,CancellationToken ct=default)=>
            throw new InvalidOperationException("Must reject before upload");
        public Task EnsureDirectoryAsync(string path,CancellationToken ct=default)=>
            throw new InvalidOperationException("Must reject before mkdir");
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    private sealed class BadConnector(FakeFtpWithBadName fake):IHotspotFtpConnector
    {
        public Task<IHotspotFtpSession> OpenAsync(HotspotFtpConnection info,CancellationToken ct=default)=>
            Task.FromResult<IHotspotFtpSession>(fake);
    }
}
