using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using NajmCard.Core.Models;
using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Desktop.Tests;

public sealed class VoucherStoreServiceTests
{
    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "najmcard-service-test",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static Voucher Card(string name, string service, string customer) =>
        new(name, "same-pass", "gold", "batch-1", DateTimeOffset.UtcNow,
            Service: service, Customer: customer);

    [Fact]
    public void Stores_And_Reads_UserManager_Owner_And_Service_Without_Confusing_Hotspot()
    {
        string root = NewRoot();
        try
        {
            using var store = new VoucherStore(root);
            store.SaveBatch([
                Card("00102", "hotspot", ""),
                Card("00102", "userman", "admin")
            ]);
            var saved = store.GetVouchers();
            Assert.Equal(2, saved.Count);
            var hotspot = Assert.Single(saved.Where(x => x.Service == "hotspot"));
            var um = Assert.Single(saved.Where(x => x.Service == "userman"));
            Assert.Equal("00102", hotspot.Username);
            Assert.Equal("00102", um.Username);
            Assert.Equal("admin", um.Customer);
            Assert.Equal("same-pass", um.Password);
            store.MarkSent(["00102"], "userman");
            saved = store.GetVouchers();
            Assert.True(Assert.Single(saved.Where(x => x.Service == "userman")).SentToRouter);
            Assert.False(Assert.Single(saved.Where(x => x.Service == "hotspot")).SentToRouter);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Migrates_Legacy_Hotspot_Database_Without_Losing_DPAPI_Protected_Passwords()
    {
        string root = NewRoot();
        try
        {
            var file = Path.Combine(root, "najmcard.db");
            using (var db = new SqliteConnection($"Data Source={file};Pooling=False"))
            {
                db.Open();
                using var cmd = db.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE vouchers(
                        username TEXT PRIMARY KEY COLLATE NOCASE,
                        password_protected TEXT NOT NULL,
                        profile TEXT NOT NULL,
                        batch_id TEXT NOT NULL,
                        created_at TEXT NOT NULL,
                        sent INTEGER NOT NULL DEFAULT 0
                    );
                    CREATE INDEX idx_vouchers_batch ON vouchers(batch_id);
                    """;
                cmd.ExecuteNonQuery();
                cmd.CommandText = """
                    INSERT INTO vouchers(username,password_protected,profile,batch_id,created_at,sent)
                    VALUES($name,$pass,'gold','old-batch','2026-11-07T12:00:00+03:00',1);
                    """;
                cmd.Parameters.AddWithValue("$name", "000124");
                var protectedPassword = Convert.ToBase64String(ProtectedData.Protect(
                    Encoding.UTF8.GetBytes("oldSecret"), null, DataProtectionScope.CurrentUser));
                cmd.Parameters.AddWithValue("$pass", protectedPassword);
                cmd.ExecuteNonQuery();
            }

            using var migrated = new VoucherStore(root);
            var old = Assert.Single(migrated.GetVouchers());
            Assert.Equal("000124", old.Username);
            Assert.Equal("oldSecret", old.Password);
            Assert.Equal("hotspot", old.Service);
            Assert.Equal("", old.Customer);
            Assert.True(old.SentToRouter);
            migrated.SaveBatch([Card("000124", "userman", "admin")]);
            Assert.Equal(2, migrated.GetVouchers().Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Rejects_Missing_UserManager_Customer_Before_Storage_Write()
    {
        string root = NewRoot();
        try
        {
            using var store = new VoucherStore(root);
            Assert.ThrowsAny<ArgumentException>(() =>
                store.SaveBatch([Card("9001", "userman", "")]));
            Assert.Empty(store.GetVouchers());
        }
        finally { Directory.Delete(root, true); }
    }
}
