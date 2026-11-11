using System.IO;
using System.Globalization;
using Microsoft.Data.Sqlite;
using NajmCard.Core.Models;
using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Desktop.Tests;

/// <summary>
/// Tests the actual SQLite/DPAPI storage boundary and account isolation;
/// no mocked query output, no performance assertions based on wall clock time.
/// </summary>
public sealed class VoucherIndexedQueriesTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),
        "najm-indexed-"+Guid.NewGuid().ToString("N"));
    public VoucherIndexedQueriesTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root,true);
    private static Voucher Card(string username,string batch,string service="hotspot",
        string customer="",int minute=0) =>
        new(username,"PrivatePW-"+username,"gold",batch,
            new DateTimeOffset(2026,11,7,12,0,0,TimeSpan.Zero).AddMinutes(minute),
            Service:service,Customer:customer);

    [Fact]
    public void Delivery_History_Recovers_Mixed_Outcomes_After_Reopening_Without_Reading_Passwords()
    {
        var sent = Card("00001", "batch");
        var pending = Card("00002", "batch");
        var failed = Card("00003", "batch");
        var unknown = Card("00004", "batch");
        var interrupted = Card("00005", "batch");
        using (var store = new VoucherStore(_root))
        {
            store.SaveBatch([sent, pending, failed, unknown, interrupted,
                Card("00001", "batch", "userman", "admin", minute: 1)]);
            store.BeginSend(sent);
            store.FinishSend(sent, "SENT");
            store.BeginSend(failed);
            store.FinishSend(failed, "FAILED");
            store.BeginSend(unknown);
            store.FinishSend(unknown, "UNKNOWN");
            store.BeginSend(interrupted);
        }

        // The status view can still read records whose credential bytes are unavailable.
        using (var db = new SqliteConnection(
            $"Data Source={Path.Combine(_root, "najmcard.db")};Pooling=False"))
        {
            db.Open();
            using var update = db.CreateCommand();
            update.CommandText = "UPDATE vouchers SET password_protected='unreadable'";
            update.ExecuteNonQuery();
        }
        using var reopened = new VoucherStore(_root);
        var summaries = reopened.ListDeliveryBatches();
        Assert.Equal(["userman", "hotspot"], summaries.Select(x => x.Service));
        var batch = summaries[1];
        Assert.Equal((5, 1, 1, 1, 2), (batch.Total, batch.Sent, batch.Pending, batch.Failed, batch.NeedsReview));
        Assert.Equal([
            new("00001", "SENT"), new("00002", "CREATED"), new("00003", "FAILED"),
            new("00004", "REVIEW"), new("00005", "REVIEW")
        ], reopened.ReadDeliveryEntries(batch.Service, batch.BatchId));
        Assert.Single(reopened.ReadDeliveryEntries("userman", "batch"));
    }

    [Fact]
    public void Batch_Query_Returns_Only_Target_Service_And_Batch_Without_Relying_On_Global_Limit()
    {
        using var store=new VoucherStore(_root);
        store.SaveBatch([
            Card("00001","old","hotspot",minute:1),
            Card("00002","wanted","hotspot",minute:2),
            Card("00003","wanted","hotspot",minute:3),
            Card("00002","wanted","userman","admin",minute:4),
            Card("00004","wanted","pppoe",minute:5),
            Card("00005","new","hotspot",minute:6)]);
        var rows=store.GetBatchVouchers("hotspot","wanted");
        Assert.Equal(["00003","00002"],rows.Select(v=>v.Username).ToArray());
        Assert.Equal("PrivatePW-00003",rows[0].Password);
        Assert.All(rows,v=>Assert.Equal("hotspot",v.Service));
        Assert.All(rows,v=>Assert.Equal("wanted",v.BatchId));
        Assert.Empty(store.GetBatchVouchers("hotspot","not-found"));
        Assert.Single(store.GetBatchVouchers("userman","wanted"));
        Assert.Single(store.GetBatchVouchers("pppoe","wanted"));
    }

    [Fact]
    public void Printable_Query_Only_Decrypts_Confirmed_Sent_And_Applies_Limit_In_Sql()
    {
        using var store=new VoucherStore(_root);
        var a=Card("00001","a",minute:1);
        var b=Card("00002","b",minute:2);
        var c=Card("00003","c",minute:3);
        var d=Card("00004","d",minute:4);
        store.SaveBatch([a,b,c,d]);
        store.MarkSent([a.Username,c.Username,d.Username]);
        store.BeginSend(b);
        store.FinishSend(b,"UNKNOWN","Router replied ambiguously");
        var rows=store.GetPrintableVouchers(2);
        Assert.Equal(["00004","00003"],rows.Select(v=>v.Username).ToArray());
        Assert.All(rows,v=>Assert.Equal("SENT",v.DeliveryStatus));
        Assert.All(rows,v=>Assert.True(v.SentToRouter));
        Assert.Equal("PrivatePW-00004",rows[0].Password);
        Assert.Equal(3,store.GetPrintableVouchers(10).Count);
        Assert.Throws<ArgumentOutOfRangeException>(()=>store.GetPrintableVouchers(0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>store.GetPrintableVouchers(10001));
    }

    [Fact]
    public void CreateSchema_Ensures_Indexes_For_Batch_And_Confirmed_Print_Filter()
    {
        using var store=new VoucherStore(_root);
        using var db=new SqliteConnection(
            $"Data Source={Path.Combine(_root,"najmcard.db")};Pooling=False");
        db.Open();
        using var cmd=db.CreateCommand();
        cmd.CommandText="PRAGMA index_list(vouchers)";
        using var reader=cmd.ExecuteReader();
        var names=new List<string>();
        while(reader.Read())names.Add(reader.GetString(1));
        Assert.Contains("idx_vouchers_service_batch",names);
        Assert.Contains("idx_vouchers_printable",names);
    }

    [Theory]
    [InlineData("unknown","valid")]
    [InlineData("hotspot","")]
    [InlineData("userman","   ")]
    public void Invalid_Batch_Query_Arguments_Are_Rejected_Before_Opening_Database(
        string service,string batch)
    {
        using var store=new VoucherStore(_root);
        Assert.ThrowsAny<ArgumentException>(
            ()=>store.GetBatchVouchers(service,batch));
    }

    [Fact]
    public void Legacy_Schema_Migration_Preserves_Sent_Status_And_Index_Availability()
    {
        var file=Path.Combine(_root,"najmcard.db");
        using(var db=new SqliteConnection($"Data Source={file};Pooling=False"))
        {
            db.Open();
            using var cmd=db.CreateCommand();
            cmd.CommandText="""
                CREATE TABLE vouchers(
                    username TEXT PRIMARY KEY COLLATE NOCASE,
                    password_protected TEXT NOT NULL,
                    profile TEXT NOT NULL,
                    batch_id TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    sent INTEGER NOT NULL DEFAULT 0
                );
                """;
            cmd.ExecuteNonQuery();
        }
        using var migrated=new VoucherStore(_root);
        Assert.Empty(migrated.GetPrintableVouchers(50));
        Assert.Empty(migrated.GetBatchVouchers("hotspot","test"));
        using var reopened=new SqliteConnection($"Data Source={file};Pooling=False");
        reopened.Open();
        using var index=reopened.CreateCommand();
        index.CommandText="PRAGMA index_list(vouchers)";
        using var read=index.ExecuteReader();
        var names=new List<string>();
        while(read.Read())names.Add(read.GetString(1));
        Assert.Contains("idx_vouchers_service_batch",names);
        Assert.Contains("idx_vouchers_printable",names);
    }
}
