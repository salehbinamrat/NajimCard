using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using NajmCard.Core.Models;

namespace NajmCard.Desktop.Services;

/// <summary>
/// Separate NajmCard-only SQLite store. Windows DPAPI protects voucher passwords;
/// migration uses a single SQLite transaction and never alters Hadramout databases.
/// </summary>
public sealed partial class VoucherStore : IDisposable
{
    private readonly string _filename;
    public string DirectoryPath { get; }

    public VoucherStore(string? rootDirectory = null)
    {
        DirectoryPath = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NajmCard", "Data");
        Directory.CreateDirectory(DirectoryPath);
        _filename = Path.Combine(DirectoryPath, "najmcard.db");
        using var db = Open();
        EnsureSchema(db);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_filename};Mode=ReadWriteCreate;Pooling=False");
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static void EnsureSchema(SqliteConnection db)
    {
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS vouchers (
                    service TEXT NOT NULL COLLATE NOCASE DEFAULT 'hotspot',
                    username TEXT NOT NULL COLLATE NOCASE,
                    customer TEXT NOT NULL DEFAULT '',
                    password_protected TEXT NOT NULL,
                    profile TEXT NOT NULL,
                    batch_id TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    sent INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY(service, username)
                );
                """;
            cmd.ExecuteNonQuery();
        }

        var primaryKeys = new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
        using (var pragma = db.CreateCommand())
        {
            pragma.CommandText = "PRAGMA table_info(vouchers)";
            using var reader = pragma.ExecuteReader();
            while (reader.Read())
                primaryKeys[reader.GetString(1)] = reader.GetInt64(5);
        }
        if (!primaryKeys.ContainsKey("username") || !primaryKeys.ContainsKey("password_protected"))
            throw new InvalidDataException("مخطط قاعدة كروت نجم كارد غير معروف؛ لم تُعدّل البيانات.");

        bool currentSchema = primaryKeys.TryGetValue("service", out long sPk) && sPk == 1
            && primaryKeys.TryGetValue("username", out long uPk) && uPk == 2
            && primaryKeys.ContainsKey("customer");
        if (!currentSchema)
        {
            // DDL and data copy are transactional; preserve the original file on any failure.
            using var tx = db.BeginTransaction();
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                CREATE TABLE vouchers_migrated (
                    service TEXT NOT NULL COLLATE NOCASE DEFAULT 'hotspot',
                    username TEXT NOT NULL COLLATE NOCASE,
                    customer TEXT NOT NULL DEFAULT '',
                    password_protected TEXT NOT NULL,
                    profile TEXT NOT NULL,
                    batch_id TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    sent INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY(service, username)
                );
                """;
            cmd.ExecuteNonQuery();
            var serviceSql = primaryKeys.ContainsKey("service") ? "COALESCE(service, 'hotspot')" : "'hotspot'";
            var customerSql = primaryKeys.ContainsKey("customer") ? "COALESCE(customer, '')" : "''";
            cmd.CommandText = $"""
                INSERT INTO vouchers_migrated(service,username,customer,
                    password_protected,profile,batch_id,created_at,sent)
                SELECT {serviceSql},username,{customerSql},
                    password_protected,profile,batch_id,created_at,sent FROM vouchers;
                """;
            cmd.ExecuteNonQuery();
            cmd.CommandText = """
                DROP TABLE vouchers;
                ALTER TABLE vouchers_migrated RENAME TO vouchers;
                CREATE INDEX idx_vouchers_batch ON vouchers(batch_id);
                """;
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
        else
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_vouchers_batch ON vouchers(batch_id);";
            cmd.ExecuteNonQuery();
        }
        EnsureDeliveryColumns(db);
        EnsureIndexedQuerySchema(db);
        EnsureActivitySchema(db);
    }

    private static void EnsureDeliveryColumns(SqliteConnection db)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var table = db.CreateCommand())
        {
            table.CommandText = "PRAGMA table_info(vouchers)";
            using var reader = table.ExecuteReader();
            while (reader.Read()) columns.Add(reader.GetString(1));
        }

        if (columns.Contains("delivery_status") && columns.Contains("last_error"))
            return;
        using var tx = db.BeginTransaction();
        using var command = db.CreateCommand();
        command.Transaction = tx;
        if (!columns.Contains("delivery_status"))
        {
            command.CommandText =
                "ALTER TABLE vouchers ADD COLUMN delivery_status TEXT NOT NULL DEFAULT 'CREATED'";
            command.ExecuteNonQuery();
            command.CommandText =
                "UPDATE vouchers SET delivery_status=CASE WHEN sent=1 THEN 'SENT' ELSE 'CREATED' END";
            command.ExecuteNonQuery();
        }
        if (!columns.Contains("last_error"))
        {
            command.CommandText =
                "ALTER TABLE vouchers ADD COLUMN last_error TEXT NOT NULL DEFAULT ''";
            command.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static string Protect(string raw) => Convert.ToBase64String(ProtectedData.Protect(
        Encoding.UTF8.GetBytes(raw), null, DataProtectionScope.CurrentUser));

    private static string Unprotect(string coded) => Encoding.UTF8.GetString(ProtectedData.Unprotect(
        Convert.FromBase64String(coded), null, DataProtectionScope.CurrentUser));

    private static void Validate(Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(voucher);
        if (voucher.Service is not ("hotspot" or "userman" or "pppoe"))
            throw new ArgumentException("خدمة الكرت غير مدعومة.", nameof(voucher));
        if (string.IsNullOrWhiteSpace(voucher.Username)
            || string.IsNullOrWhiteSpace(voucher.Profile)
            || string.IsNullOrEmpty(voucher.Password))
            throw new ArgumentException("بيانات الكرت غير مكتملة.", nameof(voucher));
        if (voucher.Service == "userman" && string.IsNullOrWhiteSpace(voucher.Customer))
            throw new ArgumentException("حساب مدير User Manager v6 مطلوب.", nameof(voucher));
    }

    public void SaveBatch(IReadOnlyList<Voucher> vouchers,
        VoucherBatchPrintSnapshot? printSnapshot=null)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        if(vouchers.Count==0)return;
        if(vouchers.Count>10000)
            throw new ArgumentOutOfRangeException(nameof(vouchers),
                "لا يمكن حفظ أكثر من 10000 كرت في دفعة واحدة.");
        foreach(var voucher in vouchers)Validate(voucher);
        if(printSnapshot is not null)
        {
            printSnapshot.Validate();
            if(vouchers.Any(v=>v.Service!=printSnapshot.Service ||
                v.BatchId!=printSnapshot.BatchId ||
                v.Profile!=printSnapshot.Profile ||
                v.Customer!=printSnapshot.Customer))
                throw new ArgumentException(
                    "قالب الطباعة لا يطابق خدمة أو دفعة أو باقة الكروت.");
        }
        using var db=Open();
        using var tx=db.BeginTransaction();
        // One prepared INSERT and its parameter objects are reused across the
        // entire SQLite transaction. This eliminates thousands of command and
        // SQLite compilation allocations on large RouterOS batches.
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            INSERT INTO vouchers(service,username,customer,password_protected,
                profile,batch_id,created_at,sent,delivery_status)
            VALUES($svc,$u,$customer,$p,$profile,$b,$c,$s,$state)
            """;
        var svc=cmd.Parameters.Add("$svc",SqliteType.Text);
        var username=cmd.Parameters.Add("$u",SqliteType.Text);
        var customer=cmd.Parameters.Add("$customer",SqliteType.Text);
        var password=cmd.Parameters.Add("$p",SqliteType.Text);
        var profile=cmd.Parameters.Add("$profile",SqliteType.Text);
        var batch=cmd.Parameters.Add("$b",SqliteType.Text);
        var created=cmd.Parameters.Add("$c",SqliteType.Text);
        var sent=cmd.Parameters.Add("$s",SqliteType.Integer);
        var state=cmd.Parameters.Add("$state",SqliteType.Text);
        cmd.Prepare();
        foreach(var voucher in vouchers)
        {
            svc.Value=voucher.Service;
            username.Value=voucher.Username;
            customer.Value=voucher.Customer;
            password.Value=Protect(voucher.Password);
            profile.Value=voucher.Profile;
            batch.Value=voucher.BatchId;
            created.Value=voucher.CreatedAt.ToString("O",CultureInfo.InvariantCulture);
            sent.Value=voucher.SentToRouter?1:0;
            state.Value=voucher.SentToRouter?"SENT":"CREATED";
            cmd.ExecuteNonQuery();
        }
        // Same transaction: a batch is never committed without its activity.
        var first=vouchers[0];
        WriteActivity(db,tx,ActivityBatchKey(first.Service,first.BatchId),
            "CARDS","إنشاء دفعة كروت","INFO",
            "خدمة "+first.Service+" | عدد الكروت "+vouchers.Count.ToString(CultureInfo.InvariantCulture));
        // Credential rows and their print-template snapshot must commit as one
        // durable SQLite transaction. Do not leave a printable but unstyled batch.
        if(printSnapshot is not null)
            WriteBatchPrintSnapshot(db,tx,printSnapshot);
        tx.Commit();
    }

    public IReadOnlyList<Voucher> GetVouchers(int limit = 10000)
    {
        using var db=Open();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT username,password_protected,profile,batch_id,created_at,
                sent,service,customer,delivery_status FROM vouchers
            ORDER BY created_at DESC LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$limit",Math.Clamp(limit,1,10000));
        return ReadVoucherRows(cmd);
    }

    public IReadOnlySet<string> ReservedNames(string service = "hotspot")
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT username FROM vouchers WHERE service=$svc";
        cmd.Parameters.AddWithValue("$svc", service);
        using var reader = cmd.ExecuteReader();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    public void MarkSent(IEnumerable<string> usernames, string service = "hotspot")
    {
        ArgumentNullException.ThrowIfNull(usernames);
        if (service is not ("hotspot" or "userman" or "pppoe"))
            throw new ArgumentException("نوع خدمة غير صالح.", nameof(service));
        using var db = Open();
        using var tx = db.BeginTransaction();
        foreach (var name in usernames)
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE vouchers SET sent=1, delivery_status='SENT' WHERE username=$name AND service=$svc";
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$svc", service);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>
    /// Persist an exclusive pre-send lease before network I/O. A crash must
    /// leave a SENDING row that cannot be silently reissued at next launch.
    /// Only CREATED / explicitly failed requests can enter SENDING.
    /// </summary>
    public void BeginSend(Voucher voucher)
    {
        Validate(voucher);
        using var db = Open();
        using var tx = db.BeginTransaction();

        using (var read = db.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = """
                SELECT password_protected FROM vouchers
                WHERE service=$service AND username=$name AND batch_id=$batch
                  AND customer=$customer AND profile=$profile
                """;
            read.Parameters.AddWithValue("$service", voucher.Service);
            read.Parameters.AddWithValue("$name", voucher.Username);
            read.Parameters.AddWithValue("$batch", voucher.BatchId);
            read.Parameters.AddWithValue("$customer", voucher.Customer);
            read.Parameters.AddWithValue("$profile", voucher.Profile);
            var coded = read.ExecuteScalar() as string;
            if (coded is null || !string.Equals(Unprotect(coded), voucher.Password,
                    StringComparison.Ordinal))
                throw new InvalidOperationException("بيانات الكرت تغيرت أو ليست محفوظة في هذه الدفعة.");
        }

        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE vouchers SET delivery_status='SENDING',
                last_error='بدأ الإرسال؛ افحص الراوتر إذا توقف البرنامج'
            WHERE service=$service AND username=$name AND batch_id=$batch
              AND customer=$customer AND profile=$profile AND sent=0
              AND delivery_status IN ('CREATED','FAILED')
            """;
        cmd.Parameters.AddWithValue("$service", voucher.Service);
        cmd.Parameters.AddWithValue("$name", voucher.Username);
        cmd.Parameters.AddWithValue("$batch", voucher.BatchId);
        cmd.Parameters.AddWithValue("$customer", voucher.Customer);
        cmd.Parameters.AddWithValue("$profile", voucher.Profile);
        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException(
                "الكرت مُرسل أو قيد الإرسال أو نتيجته غير مؤكدة؛ لا يمكن إعادة إرساله.");
        tx.Commit();
    }

    /// <summary>Record the authoritative result; never convert UNKNOWN into retryable FAILED.</summary>
    public void FinishSend(Voucher voucher, string state, string error = "")
    {
        ArgumentNullException.ThrowIfNull(voucher);
        if (state is not ("SENT" or "FAILED" or "UNKNOWN"))
            throw new ArgumentException("حالة تسليم الكرت غير صالحة.", nameof(state));
        using var db = Open();
        using var tx = db.BeginTransaction();
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE vouchers SET delivery_status=$state, last_error=$error,
                sent=CASE WHEN $state='SENT' THEN 1 ELSE 0 END
            WHERE service=$service AND username=$name AND batch_id=$batch
              AND customer=$customer AND profile=$profile
              AND delivery_status='SENDING'
            """;
        cmd.Parameters.AddWithValue("$state", state);
        cmd.Parameters.AddWithValue("$error", error.Length > 1000 ? error[..1000] : error);
        cmd.Parameters.AddWithValue("$service", voucher.Service);
        cmd.Parameters.AddWithValue("$name", voucher.Username);
        cmd.Parameters.AddWithValue("$batch", voucher.BatchId);
        cmd.Parameters.AddWithValue("$customer", voucher.Customer);
        cmd.Parameters.AddWithValue("$profile", voucher.Profile);
        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException(
                "تعذر مطابقة كرت قيد الإرسال؛ توقفت العملية لحماية سجل الراوتر.");
        // Aggregate the batch from authoritative durable states: no per-card
        // activity event explosion for large batches and no credential data.
        using var counts=db.CreateCommand();
        counts.Transaction=tx;
        counts.CommandText="""
            SELECT COUNT(*),
                COALESCE(SUM(CASE WHEN delivery_status='SENT' THEN 1 ELSE 0 END),0),
                COALESCE(SUM(CASE WHEN delivery_status='UNKNOWN' THEN 1 ELSE 0 END),0),
                COALESCE(SUM(CASE WHEN delivery_status='FAILED' THEN 1 ELSE 0 END),0)
            FROM vouchers WHERE service=$svc AND batch_id=$batch
            """;
        counts.Parameters.AddWithValue("$svc",voucher.Service);
        counts.Parameters.AddWithValue("$batch",voucher.BatchId);
        using(var reader=counts.ExecuteReader())
        {
            if(!reader.Read())throw new InvalidDataException("تعذر قراءة حالة دفعة الكروت.");
            var total=reader.GetInt64(0);
            var successful=reader.GetInt64(1);
            var uncertain=reader.GetInt64(2);
            var failed=reader.GetInt64(3);
            string outcome=uncertain>0?"ERROR":successful==total?"SUCCESS":
                failed>0?"PARTIAL":"INFO";
            WriteActivity(db,tx,ActivityBatchKey(voucher.Service,voucher.BatchId),
                "ROUTER","إرسال دفعة كروت",outcome,
                "خدمة "+voucher.Service+" | ناجح "+successful+
                " من "+total+" | فاشل "+failed+" | غير مؤكد "+uncertain);
        }
        tx.Commit();
    }

    public string Backup()
    {
        string directory = Path.Combine(DirectoryPath, "Backups");
        Directory.CreateDirectory(directory);
        // Never overwrite an earlier backup when the button is clicked twice
        // in the same second. Keep the filename sortable and unique.
        string backup = Path.Combine(directory, "NajmCard-" +
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) +
            "-" + Guid.NewGuid().ToString("N")[..8] + ".db");
        using var original = Open();
        using var snapshot = new SqliteConnection($"Data Source={backup};Pooling=False");
        snapshot.Open();
        original.BackupDatabase(snapshot);
        return backup;
    }

    public void Dispose() { }
}
