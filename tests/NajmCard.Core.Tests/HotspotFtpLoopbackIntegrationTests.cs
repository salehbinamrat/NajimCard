using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>
/// Real TCP/FTP protocol exercise of the production FtpWebRequest adapter.
/// A private loopback FTP fixture simulates MikroTik RouterOS filesystem
/// commands; this is not a live-router acceptance test.
/// </summary>
public sealed class HotspotFtpLoopbackIntegrationTests
{
    [Fact]
    public async Task Real_Ftp_Sockets_Download_Nested_Hotspot_Files()
    {
        await using var server=new LoopbackFtpServer();
        server.Files["hotspot/login.html"]=Encoding.UTF8.GetBytes("$(username)");
        server.Files["hotspot/css/site.css"]=Encoding.UTF8.GetBytes("body{color:red}");
        var parent=Directory.CreateTempSubdirectory("najm-ftp-download-").FullName;
        try
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var service=new HotspotFtpTransferService(new HotspotFtpWebRequestConnector());
            HotspotFtpDownloadResult result;
            try
            {
                result=await service.DownloadSnapshotAsync(
                    new HotspotFtpConnection("127.0.0.1",server.Port,
                        "admin","test",UseTls:false,AllowPlainFtp:true),
                    "hotspot",parent,deadline.Token);
            }
            catch(Exception ex)
            {
                throw new IOException("Loopback FTP trace: "+
                    string.Join(" | ",server.Commands),ex);
            }
            Assert.Equal(2,result.Files);
            Assert.Equal("$(username)",File.ReadAllText(
                Path.Combine(result.LocalFolder,"login.html")));
            Assert.Equal("body{color:red}",File.ReadAllText(
                Path.Combine(result.LocalFolder,"css","site.css")));
        }
        finally {Directory.Delete(parent,true);}
    }

    [Fact]
    public async Task Real_Ftp_Sockets_Upload_Only_After_Existing_Router_Backup()
    {
        await using var server=new LoopbackFtpServer();
        server.Files["hotspot/login.html"]=Encoding.UTF8.GetBytes("original");
        var parent=Directory.CreateTempSubdirectory("najm-ftp-upload-").FullName;
        try
        {
            var local=Path.Combine(parent,"site");
            Directory.CreateDirectory(local);
            File.WriteAllText(Path.Combine(local,"login.html"),"new $(username)");
            File.WriteAllText(Path.Combine(local,"status.html"),"new status");
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var result=await new HotspotFtpTransferService(
                new HotspotFtpWebRequestConnector()).UploadWithBackupAsync(
                new HotspotFtpConnection("127.0.0.1",server.Port,
                    "admin","test",UseTls:false,AllowPlainFtp:true),
                HotspotTemplateWorkspace.Open(local),"hotspot",
                Path.Combine(parent,"old-hotspot.zip"),deadline.Token);
            Assert.Equal(2,result.UploadedFiles);
            using var zip=ZipFile.OpenRead(result.BackupArchive);
            using var reader=new StreamReader(zip.GetEntry("login.html")!.Open());
            Assert.Equal("original",reader.ReadToEnd());
            Assert.Equal("new $(username)",Encoding.UTF8.GetString(
                server.Files["hotspot/login.html"]));
        }
        finally {Directory.Delete(parent,true);}
    }

    private sealed class LoopbackFtpServer:IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop=new();
        private readonly Task _loop;
        private readonly ConcurrentBag<Task> _sessions=[];
        public ConcurrentDictionary<string,byte[]> Files {get;}=new(StringComparer.Ordinal);
        public ConcurrentQueue<string> Commands {get;}=new();
        public int Port=>((IPEndPoint)_listener.LocalEndpoint).Port;
        public LoopbackFtpServer()
        {
            _listener=new TcpListener(IPAddress.Loopback,0);
            _listener.Start();
            _loop=Task.Run(async()=>
            {
                try
                {
                    while(!_stop.IsCancellationRequested)
                    {
                        var socket=await _listener.AcceptTcpClientAsync(_stop.Token);
                        _sessions.Add(Task.Run(()=>HandleAsync(socket,_stop.Token)));
                    }
                }
                catch(OperationCanceledException){}
                catch(ObjectDisposedException){}
                catch(SocketException) when(_stop.IsCancellationRequested){}
            });
        }
        private async Task HandleAsync(TcpClient socket,CancellationToken ct)
        {
            using(socket)
            using(var channel=socket.GetStream())
            using(var reader=new StreamReader(channel,Encoding.ASCII,
                detectEncodingFromByteOrderMarks:false,leaveOpen:true))
            using(var writer=new StreamWriter(channel,Encoding.ASCII,
                leaveOpen:true){NewLine="\r\n",AutoFlush=true})
            {
                TcpListener? passive=null;
                var cwd="";
                try
                {
                    await writer.WriteLineAsync("220 NajmCard loopback FTP fixture");
                    while(!ct.IsCancellationRequested)
                    {
                        var line=await reader.ReadLineAsync(ct);
                        if(line is null)break;
                        var space=line.IndexOf(' ');
                        string cmd=(space<0?line:line[..space]).ToUpperInvariant();
                        string arg=space<0?"":line[(space+1)..];
                        string path=Normalize(arg.Length>0?arg:cwd);
                        // FtpWebRequest may CWD into /hotspot and then issue
                        // NLST css or RETR login.html. FTP paths are relative
                        // to the current working directory unless rooted.
                        if(cwd.Length>0 && arg.Length>0 && !arg.StartsWith('/') &&
                            (cmd is "NLST" or "RETR" or "STOR" or "SIZE" or "MKD" or "CWD") &&
                            !path.StartsWith(cwd+"/",StringComparison.Ordinal))
                            path=cwd+"/"+path;
                        Commands.Enqueue(cmd=="PASS" ? "PASS <redacted>" :
                            cmd+" "+arg+" [cwd="+cwd+", normalized="+path+"]");
                        switch(cmd)
                        {
                            case "USER":
                                await writer.WriteLineAsync("331 Password required");break;
                            case "PASS":
                                await writer.WriteLineAsync("230 Logged in");break;
                            case "TYPE":
                            case "OPTS":
                            case "NOOP":
                                await writer.WriteLineAsync("200 OK");break;
                            case "SYST":
                                await writer.WriteLineAsync("215 UNIX Type: L8");break;
                            case "FEAT":
                                await writer.WriteLineAsync("211 End");break;
                            case "PWD":
                                await writer.WriteLineAsync("257 \"/\"");break;
                            case "CWD":
                                if(IsDirectory(path)){cwd=path;await writer.WriteLineAsync("250 Directory OK");}
                                else await writer.WriteLineAsync("550 Directory not found");
                                break;
                            case "SIZE":
                                if(Files.TryGetValue(path,out var bytes))
                                    await writer.WriteLineAsync("213 "+bytes.Length);
                                else await writer.WriteLineAsync("550 Not a file");
                                break;
                            case "PASV":
                            case "EPSV":
                                passive?.Stop();
                                passive=new TcpListener(IPAddress.Loopback,0);
                                passive.Start();
                                int port=((IPEndPoint)passive.LocalEndpoint).Port;
                                if(cmd=="PASV")await writer.WriteLineAsync(
                                    "227 Entering Passive Mode (127,0,0,1,"+
                                    (port/256)+","+(port%256)+")");
                                else await writer.WriteLineAsync(
                                    "229 Entering Extended Passive Mode (|||"+port+"|)");
                                break;
                            case "NLST":
                                if(!IsDirectory(path)||passive is null)
                                {await writer.WriteLineAsync("550 Directory unavailable");break;}
                                await writer.WriteLineAsync("150 Listing");
                                using(var data=await passive.AcceptTcpClientAsync(ct))
                                await using(var stream=data.GetStream())
                                {
                                    var prefix=path.Length==0?"":path+"/";
                                    var names=Files.Keys.Where(x=>x.StartsWith(prefix,StringComparison.Ordinal))
                                        .Select(x=>x[prefix.Length..].Split('/')[0])
                                        .Where(x=>x.Length>0).Distinct(StringComparer.Ordinal)
                                        .OrderBy(x=>x,StringComparer.Ordinal).ToArray();
                                    var payload=Encoding.UTF8.GetBytes(string.Join("\r\n",names)+"\r\n");
                                    await stream.WriteAsync(payload,ct);
                                }
                                passive.Stop();passive=null;
                                await writer.WriteLineAsync("226 Done");
                                break;
                            case "RETR":
                                if(!Files.TryGetValue(path,out var file)||passive is null)
                                {await writer.WriteLineAsync("550 File missing");break;}
                                await writer.WriteLineAsync("150 Downloading");
                                using(var data=await passive.AcceptTcpClientAsync(ct))
                                await using(var stream=data.GetStream())
                                    await stream.WriteAsync(file,ct);
                                passive.Stop();passive=null;
                                await writer.WriteLineAsync("226 Done");
                                break;
                            case "STOR":
                                if(passive is null)
                                {await writer.WriteLineAsync("425 Passive mode required");break;}
                                await writer.WriteLineAsync("150 Uploading");
                                using(var data=await passive.AcceptTcpClientAsync(ct))
                                await using(var stream=data.GetStream())
                                using(var ms=new MemoryStream())
                                {
                                    await stream.CopyToAsync(ms,ct);
                                    Files[path]=ms.ToArray();
                                }
                                passive.Stop();passive=null;
                                await writer.WriteLineAsync("226 Done");
                                break;
                            case "MKD":
                                await writer.WriteLineAsync("257 \""+path+"\" created");break;
                            case "QUIT":
                                await writer.WriteLineAsync("221 Bye");return;
                            default:
                                await writer.WriteLineAsync("502 Unsupported command");break;
                        }
                    }
                }
                catch(OperationCanceledException){}
                catch(IOException) when(ct.IsCancellationRequested){}
                finally {passive?.Stop();}
            }
        }
        private static string Normalize(string input)=>input.Replace('\\','/').TrimStart('/');
        private bool IsDirectory(string path)
        {
            if(path.Length==0)return true;
            var prefix=path.TrimEnd('/')+"/";
            return Files.Keys.Any(x=>x.StartsWith(prefix,StringComparison.Ordinal));
        }
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            try{await _loop;}catch(OperationCanceledException){}
            foreach(var task in _sessions)
                try{await task.WaitAsync(TimeSpan.FromSeconds(3));}
                catch(OperationCanceledException){}
                catch(TimeoutException){}
            _stop.Dispose();
        }
    }
}
