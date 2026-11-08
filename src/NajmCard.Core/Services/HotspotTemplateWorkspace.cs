using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace NajmCard.Core.Services;

/// <summary>One local file in a MikroTik Hotspot page template. Binary assets are never treated as HTML text.</summary>
public sealed record HotspotTemplateFile(string RelativePath, long SizeBytes, bool Editable);

public sealed record HotspotTemplateInventory(
    IReadOnlyList<HotspotTemplateFile> Files,
    IReadOnlyList<string> MissingRecommended);

/// <summary>
/// Safe LOCAL Hotspot page validation, text editing and ZIP export.
/// This class never uploads to MikroTik and does not execute HTML/JavaScript.
/// It preserves RouterOS $(...) variables byte-for-byte when the user leaves
/// them unchanged. All local path and reparse-point checks are enforced
/// by the file service, not only by WPF controls.
/// </summary>
public sealed class HotspotTemplateWorkspace
{
    private const int MaxFiles=2000;
    private const long MaxFileBytes=32L*1024*1024;
    private const long MaxTotalBytes=256L*1024*1024;
    private const int MaxTextBytes=2*1024*1024;
    private const int MaxDepth=16;
    private const string BackupDirectory=".najm-backups";
    private static readonly Encoding StrictUtf8=new UTF8Encoding(false,true);
    private static readonly HashSet<string> EditableExtensions=
        new(StringComparer.OrdinalIgnoreCase)
        {".html",".htm",".css",".js",".json",".txt",".svg",".xml"};
    private static readonly string[] Recommended=
        ["alogin.html","status.html","logout.html"];

    private readonly string _root;
    private readonly Dictionary<string,string> _textVersions=
        new(StringComparer.OrdinalIgnoreCase);
    public HotspotTemplateInventory Inventory { get; private set; }

    private HotspotTemplateWorkspace(string root)
    {
        _root=root;
        Inventory=LoadInventory(updateVersion:true);
    }

    public static HotspotTemplateWorkspace Open(string root)
    {
        if(string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("اختر مجلد صفحات Hotspot.",nameof(root));
        var full=Path.GetFullPath(root);
        if(!Directory.Exists(full))
            throw new DirectoryNotFoundException("مجلد صفحات Hotspot غير موجود: "+full);
        if((File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)
            throw new InvalidDataException("لا يمكن فتح مجلد Hotspot ذي رابط رمزي.");
        return new HotspotTemplateWorkspace(full);
    }

    public HotspotTemplateInventory Refresh() =>
        Inventory=LoadInventory(updateVersion:true);

    private static bool IsEditable(string relative,long length) =>
        length<=MaxTextBytes&&
        EditableExtensions.Contains(Path.GetExtension(relative));

    private HotspotTemplateInventory LoadInventory(bool updateVersion)
    {
        var files=EnumerateSafeFiles();
        if(!files.Any(x=>x.RelativePath.Equals("login.html",StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(
                "القالب لا يحتوي على login.html في المجلد الأساسي لصفحات Hotspot.");

        var firstLevel=files.Select(x=>x.RelativePath)
            .Where(x=>!x.Contains('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing=Recommended.Where(x=>!firstLevel.Contains(x)).ToArray();
        if(updateVersion)
        {
            _textVersions.Clear();
            foreach(var item in files.Where(x=>x.Editable))
                _textVersions[item.RelativePath]=Fingerprint(
                    Path.Combine(_root,item.RelativePath.Replace('/',Path.DirectorySeparatorChar)));
        }
        return new HotspotTemplateInventory(files,missing);
    }

    private IReadOnlyList<HotspotTemplateFile> EnumerateSafeFiles()
    {
        var files=new List<HotspotTemplateFile>();
        long size=0;
        void Visit(string directory,int depth)
        {
            if(depth>MaxDepth)
                throw new InvalidDataException("تجاوز القالب الحد الآمن لعمق المجلدات.");
            foreach(var entry in new DirectoryInfo(directory)
                .EnumerateFileSystemInfos().OrderBy(x=>x.Name,StringComparer.OrdinalIgnoreCase))
            {
                if((entry.Attributes&FileAttributes.ReparsePoint)!=0)
                    throw new InvalidDataException(
                        "رفض رابط رمزي داخل مجلد Hotspot: "+entry.Name);
                var relative=Path.GetRelativePath(_root,entry.FullName).Replace('\\','/');
                // Our own backup directory is not a Hotspot template file.
                // Check reparse points first, then skip only this exact internal
                // top-level folder; all other paths still require validation.
                if(relative.Equals(BackupDirectory,StringComparison.OrdinalIgnoreCase)
                    && (entry.Attributes&FileAttributes.Directory)!=0)
                    continue;
                ValidateRelative(relative);
                if((entry.Attributes&FileAttributes.Directory)!=0)
                {
                    Visit(entry.FullName,depth+1);
                    continue;
                }
                if(entry is not FileInfo file)
                    throw new InvalidDataException("نوع ملف Hotspot غير معروف.");
                if(file.Length>MaxFileBytes)
                    throw new InvalidDataException(
                        "ملف Hotspot أكبر من الحد الآمن: "+relative);
                size=checked(size+file.Length);
                if(size>MaxTotalBytes || files.Count>=MaxFiles)
                    throw new InvalidDataException(
                        "مجلد صفحات Hotspot أكبر من الحد الآمن للتصدير.");
                files.Add(new HotspotTemplateFile(relative,file.Length,
                    IsEditable(relative,file.Length)));
            }
        }
        Visit(_root,0);
        return files;
    }

    private static void ValidateRelative(string relative)
    {
        if(string.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative)||
            relative.Contains(':')||relative.StartsWith('/')||
            relative.StartsWith('\\'))
            throw new ArgumentException("مسار ملف Hotspot غير آمن.",nameof(relative));
        var parts=relative.Replace('\\','/').Split('/');
        if(parts.Any(x=>string.IsNullOrEmpty(x)||x is "." or ".."||
            x.IndexOfAny(['*','?','"','<','>','|','\0'])>=0||
            x.Any(char.IsControl)||x.EndsWith(' ')||x.EndsWith('.'))
            ||parts[0].Equals(BackupDirectory,StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("مسار ملف Hotspot غير آمن.",nameof(relative));
    }

    private string ResolveFile(string relative)
    {
        ValidateRelative(relative);
        var full=Path.GetFullPath(Path.Combine(_root,
            relative.Replace('/',Path.DirectorySeparatorChar)
                .Replace('\\',Path.DirectorySeparatorChar)));
        var prefix=_root.EndsWith(Path.DirectorySeparatorChar)
            ? _root : _root+Path.DirectorySeparatorChar;
        if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("المسار يقع خارج مجلد Hotspot.",nameof(relative));
        var cursor=Path.GetDirectoryName(full);
        while(cursor is not null &&
            !cursor.Equals(_root,StringComparison.OrdinalIgnoreCase))
        {
            if(Directory.Exists(cursor) &&
                (File.GetAttributes(cursor)&FileAttributes.ReparsePoint)!=0)
                throw new InvalidDataException("رابط رمزي غير مسموح في مسار صفحة Hotspot.");
            cursor=Path.GetDirectoryName(cursor);
        }
        if(File.Exists(full)&&(File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)
            throw new InvalidDataException("رابط رمزي غير مسموح لملف Hotspot.");
        return full;
    }

    private HotspotTemplateFile EditableFile(string relative)
    {
        ValidateRelative(relative);
        var item=Inventory.Files.FirstOrDefault(x=>x.RelativePath.Equals(
            relative.Replace('\\','/'),StringComparison.OrdinalIgnoreCase));
        if(item is null)
            throw new FileNotFoundException("ملف Hotspot غير موجود في قائمة القالب.",relative);
        if(!item.Editable)
            throw new InvalidOperationException(
                "هذا ملف ثنائي أو كبير؛ لا يجوز تحريره كمحتوى HTML/UTF-8.");
        return item;
    }

    private static string Fingerprint(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public string ReadText(string relative)
    {
        var item=EditableFile(relative);
        var full=ResolveFile(item.RelativePath);
        var bytes=File.ReadAllBytes(full);
        if(bytes.Length>MaxTextBytes)
            throw new InvalidDataException("ملف Hotspot النصي أكبر من حد المحرر.");
        return StrictUtf8.GetString(bytes).TrimStart('\uFEFF');
    }

    /// <summary>
    /// Save only an existing text file after comparing the exact open/last-save
    /// content hash; keep a durable local backup before an atomic replacement.
    /// Returned path is the immutable pre-edit backup, excluded from ZIP export.
    /// </summary>
    public string SaveText(string relative,string newText)
    {
        ArgumentNullException.ThrowIfNull(newText);
        var item=EditableFile(relative);
        var full=ResolveFile(item.RelativePath);
        var originalHash=Fingerprint(full);
        if(!_textVersions.TryGetValue(item.RelativePath,out var expected)||
            !originalHash.Equals(expected,StringComparison.Ordinal))
            throw new InvalidOperationException(
                "عُدل الملف خارج نجم كارد بعد فتحه؛ حدّث القالب قبل الحفظ لتجنب فقدان التعديل.");
        var encoded=StrictUtf8.GetBytes(newText);
        if(encoded.Length>MaxTextBytes)
            throw new ArgumentException("حجم الملف النصي المعدل يتجاوز الحد المسموح.",nameof(newText));

        var backupRoot=Path.Combine(_root,BackupDirectory);
        if(Directory.Exists(backupRoot) &&
            (File.GetAttributes(backupRoot)&FileAttributes.ReparsePoint)!=0)
            throw new InvalidDataException("مجلد النسخ الاحتياطية مرتبط بمسار غير آمن.");
        Directory.CreateDirectory(backupRoot);
        var backup=Path.Combine(backupRoot,
            DateTime.UtcNow.ToString("yyyyMMddHHmmss")+"_"+
            Guid.NewGuid().ToString("N")+"_"+Path.GetFileName(full)+".bak");
        File.Copy(full,backup,overwrite:false);
        var temp=Path.Combine(Path.GetDirectoryName(full)!,
            ".najm-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using(var stream=new FileStream(temp,FileMode.CreateNew,
                FileAccess.Write,FileShare.None,8192,FileOptions.WriteThrough))
            {
                stream.Write(encoded);
                stream.Flush(flushToDisk:true);
            }
            File.Move(temp,full,overwrite:true);
            _textVersions[item.RelativePath]=Fingerprint(full);
            Inventory=LoadInventory(updateVersion:false);
            return backup;
        }
        finally
        {
            if(File.Exists(temp))File.Delete(temp);
        }
    }

    public int ExportZip(string archivePath)
    {
        if(string.IsNullOrWhiteSpace(archivePath) ||
            !archivePath.EndsWith(".zip",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("اختر ملف تصدير بامتداد ZIP.",nameof(archivePath));
        var target=Path.GetFullPath(archivePath);
        var prefix=_root.EndsWith(Path.DirectorySeparatorChar)
            ? _root : _root+Path.DirectorySeparatorChar;
        if(target.StartsWith(prefix,StringComparison.OrdinalIgnoreCase) ||
            target.Equals(_root,StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "لا تضع ملف ZIP داخل مجلد صفحات Hotspot؛ اختر مجلداً آخر.",
                nameof(archivePath));
        var destination=Path.GetDirectoryName(target)!;
        if(!Directory.Exists(destination))
            throw new DirectoryNotFoundException(destination);

        var files=LoadInventory(updateVersion:false).Files;
        var tmp=Path.Combine(destination,".najm-export-"+
            Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using(var stream=new FileStream(tmp,FileMode.CreateNew,
                FileAccess.Write,FileShare.None))
            using(var archive=new ZipArchive(stream,ZipArchiveMode.Create,
                leaveOpen:false,entryNameEncoding:Encoding.UTF8))
            {
                foreach(var file in files)
                {
                    var source=ResolveFile(file.RelativePath);
                    var entry=archive.CreateEntry(file.RelativePath,CompressionLevel.Optimal);
                    using var targetStream=entry.Open();
                    using var input=File.OpenRead(source);
                    input.CopyTo(targetStream);
                }
            }
            File.Move(tmp,target,overwrite:true);
            return files.Count;
        }
        finally
        {
            if(File.Exists(tmp))File.Delete(tmp);
        }
    }
}
