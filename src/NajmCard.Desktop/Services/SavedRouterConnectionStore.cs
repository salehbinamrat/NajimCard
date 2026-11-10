using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NajmCard.Desktop.Services;

/// <summary>A user-selected RouterOS endpoint; no connection or VPN tunnel is created by loading it.</summary>
public sealed record SavedRouterConnection(string Host, int Port, string Username,
    bool UseTls, string Mode, string Password = "")
{
    public string ModeLabel => Mode=="remote"?"VPN":"محلي";
    public string DisplayName => $"{(Mode == "remote" ? "VPN · " : "محلي · ")}{Host}:{Port} · {Username}" +
        (UseTls ? " · SSL" : "");
}

/// <summary>
/// Independent NajmCard-only connection history. Passwords are never written in
/// plaintext and are saved only after explicit opt-in, with user-scoped DPAPI.
/// Never open, migrate or modify Hadramout's profile or credential files.
/// </summary>
public sealed class SavedRouterConnectionStore
{
    private readonly string _filename;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NajmCard.ConnectionProfiles.v1");
    private const int MaximumProfiles = 50;
    private const long MaximumFileBytes = 1024 * 1024;

    public SavedRouterConnectionStore(string? directory = null)
    {
        string root = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NajmCard", "Connections");
        _filename = Path.Combine(root, "profiles.json");
    }

    public IReadOnlyList<SavedRouterConnection> Load() =>
        ReadRows().Select(x => new SavedRouterConnection(x.Host, x.Port,
            x.Username, x.UseTls, x.Mode,
            string.IsNullOrEmpty(x.PasswordBlob) ? "" : Unprotect(x.PasswordBlob))).ToArray();

    public void Save(SavedRouterConnection connection, bool rememberPassword)
    {
        Validate(connection);
        var rows = ReadRows();
        var next = new SavedProfileRow(connection.Host.Trim(), connection.Port,
            connection.Username.Trim(), connection.UseTls, connection.Mode,
            rememberPassword && !string.IsNullOrEmpty(connection.Password)
                ? Protect(connection.Password) : "");
        rows.RemoveAll(row => Same(row, connection));
        rows.Insert(0, next);
        if(rows.Count > MaximumProfiles) rows.RemoveRange(MaximumProfiles, rows.Count - MaximumProfiles);
        WriteRows(rows);
    }

    public void Delete(SavedRouterConnection connection)
    {
        Validate(connection);
        var rows = ReadRows();
        if(rows.RemoveAll(row => Same(row, connection)) > 0)
            WriteRows(rows);
    }

    private static bool Same(SavedProfileRow row, SavedRouterConnection c) =>
        string.Equals(row.Host, c.Host.Trim(), StringComparison.OrdinalIgnoreCase) &&
        row.Port == c.Port &&
        string.Equals(row.Username, c.Username.Trim(), StringComparison.OrdinalIgnoreCase) &&
        row.UseTls == c.UseTls &&
        row.Mode == c.Mode;

    private static void Validate(SavedRouterConnection c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (string.IsNullOrWhiteSpace(c.Host) || c.Host.Length > 253 ||
            c.Host.Any(char.IsWhiteSpace) || c.Host.Contains('\0') ||
            c.Port is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(c.Username) || c.Username.Length > 100 ||
            c.Mode is not ("local" or "remote") ||
            c.Password.Length > 4096)
            throw new ArgumentException("بيانات الاتصال المحفوظ غير صالحة.");
    }

    private List<SavedProfileRow> ReadRows()
    {
        if(!File.Exists(_filename)) return [];
        try
        {
            var info = new FileInfo(_filename);
            if(info.Length > MaximumFileBytes)
                throw new InvalidDataException("ملف الاتصالات أكبر من الحد الآمن.");
            var rows = JsonSerializer.Deserialize<List<SavedProfileRow>>(
                File.ReadAllText(_filename, Encoding.UTF8));
            if (rows is null || rows.Count > MaximumProfiles || rows.Any(r => r is null))
                throw new InvalidDataException("صيغة محفوظات الاتصال غير صالحة.");
            foreach(var row in rows)
                Validate(new SavedRouterConnection(row.Host,row.Port,row.Username,
                    row.UseTls,row.Mode));
            if (rows.Any(r => r.PasswordBlob.Length > 10000))
                throw new InvalidDataException("حقل كلمة المرور المحمية غير صالح.");
            return rows;
        }
        catch(JsonException ex)
        {
            throw new InvalidDataException(
                "ملف الاتصالات المحفوظة تالف؛ لم يتم تغييره أو حذفه.",ex);
        }
        catch(CryptographicException ex)
        {
            throw new InvalidDataException(
                "لا يمكن فك بيانات الاتصال المحفوظة لهذا المستخدم.",ex);
        }
    }

    private static string Protect(string password) =>
        Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser));

    private static string Unprotect(string coded)
    {
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(
                Convert.FromBase64String(coded), Entropy, DataProtectionScope.CurrentUser));
        }
        catch(Exception ex) when (ex is CryptographicException or FormatException)
        {
            throw new InvalidDataException(
                "تعذر فك كلمة مرور الاتصال المحفوظة؛ لن تُعاد كتابة الملف تلقائيا.",ex);
        }
    }

    private void WriteRows(List<SavedProfileRow> rows)
    {
        var parent = Path.GetDirectoryName(_filename)!;
        Directory.CreateDirectory(parent);
        var staged=Path.Combine(parent,".najm-profiles-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using(var fs=new FileStream(staged,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                JsonSerializer.Serialize(fs, rows, new JsonSerializerOptions {WriteIndented=true});
                fs.Flush(flushToDisk:true);
            }
            if(File.Exists(_filename))File.Replace(staged,_filename,null);
            else File.Move(staged,_filename);
        }
        finally { if(File.Exists(staged)) File.Delete(staged); }
    }

    // Public constructor/properties for the JSON serialization contract only.
    private sealed class SavedProfileRow
    {
        public SavedProfileRow() { }
        public SavedProfileRow(string host,int port,string username,bool useTls,
            string mode,string passwordBlob)
        {
            Host=host;Port=port;Username=username;UseTls=useTls;Mode=mode;
            PasswordBlob=passwordBlob;
        }
        public string Host {get;set;}="";
        public int Port {get;set;}
        public string Username {get;set;}="";
        public bool UseTls {get;set;}
        public string Mode {get;set;}="";
        public string PasswordBlob {get;set;}="";
    }
}
