using System.IO;
using NajmCard.Core.Models;
using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Desktop.Tests;

/// <summary>
/// Mirrors Hadramout core/delivery.py durable SENDING/SENT/FAILED/UNKNOWN
/// protocol on an actual Windows SQLite + DPAPI store.
/// </summary>
public sealed class VoucherDurableDeliveryTests
{
    private static string Root()
    {
        var root = Path.Combine(Path.GetTempPath(), "najm-durable", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
    private static Voucher Card() =>
        new("001234", "secret", "gold", "batch-7", DateTimeOffset.UtcNow,
            Service: "userman", Customer: "admin");

    [Fact]
    public void Persisted_Sending_Lease_Remains_Locked_After_Process_Restart()
    {
        string root = Root();
        try
        {
            var card = Card();
            using (var store = new VoucherStore(root))
            {
                store.SaveBatch([card]);
                store.BeginSend(card);
                Assert.Equal("SENDING", Assert.Single(store.GetVouchers()).DeliveryStatus);
            }
            using (var reopened = new VoucherStore(root))
            {
                var current = Assert.Single(reopened.GetVouchers());
                Assert.Equal("SENDING", current.DeliveryStatus);
                Assert.False(current.SentToRouter);
                Assert.ThrowsAny<InvalidOperationException>(() => reopened.BeginSend(card));
                reopened.FinishSend(card, "UNKNOWN", "انقطع الاتصال");
                Assert.Equal("UNKNOWN", Assert.Single(reopened.GetVouchers()).DeliveryStatus);
                Assert.ThrowsAny<InvalidOperationException>(() => reopened.BeginSend(card));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Explicit_Trap_Records_Failed_And_Manual_Retry_Is_Possible()
    {
        string root = Root();
        try
        {
            var card = Card();
            using var store = new VoucherStore(root);
            store.SaveBatch([card]);
            store.BeginSend(card);
            store.FinishSend(card, "FAILED", "profile rejected");
            Assert.Equal("FAILED", Assert.Single(store.GetVouchers()).DeliveryStatus);
            store.BeginSend(card);
            store.FinishSend(card, "SENT");
            var saved = Assert.Single(store.GetVouchers());
            Assert.Equal("SENT", saved.DeliveryStatus);
            Assert.True(saved.SentToRouter);
            Assert.ThrowsAny<InvalidOperationException>(() => store.BeginSend(card));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Result_Cannot_Be_Recorded_Without_A_Preexisting_Sending_Lease()
    {
        string root = Root();
        try
        {
            var card = Card();
            using var store = new VoucherStore(root);
            store.SaveBatch([card]);
            Assert.ThrowsAny<InvalidOperationException>(() => store.FinishSend(card, "SENT"));
            Assert.Equal("CREATED", Assert.Single(store.GetVouchers()).DeliveryStatus);
            store.BeginSend(card);
            Assert.ThrowsAny<ArgumentException>(() =>
                store.FinishSend(card, "CREATED"));
            Assert.Equal("SENDING", Assert.Single(store.GetVouchers()).DeliveryStatus);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Existing_Hotspot_Sent_Rows_Migrate_To_Sent_State()
    {
        string root = Root();
        try
        {
            using var store = new VoucherStore(root);
            var card = new Voucher("9001", "p", "normal",
                "batch", DateTimeOffset.UtcNow, SentToRouter: true);
            store.SaveBatch([card]);
            var saved = Assert.Single(store.GetVouchers());
            Assert.Equal("SENT", saved.DeliveryStatus);
            Assert.ThrowsAny<InvalidOperationException>(() => store.BeginSend(card));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Batch_Send_Session_Keeps_Each_Lease_And_Receipt_Durable_Across_Restarts()
    {
        var root=Root();
        try
        {
            var first=Card();
            var second=Card() with { Username="001235" };
            using(var store=new VoucherStore(root))
            {
                store.SaveBatch([first,second]);
                using(var session=store.OpenSendSession())
                {
                    session.BeginSend(first);
                    session.FinishSend(first,"SENT");
                    session.BeginSend(second);
                    // Simulate a crash after submitting the second card:
                    // no definitive reply may be recorded before restart.
                }
            }
            using(var reopened=new VoucherStore(root))
            {
                var cards=reopened.GetBatchVouchers("userman","batch-7");
                Assert.Equal("SENT",cards.Single(x=>x.Username==first.Username).DeliveryStatus);
                Assert.Equal("SENDING",cards.Single(x=>x.Username==second.Username).DeliveryStatus);
                Assert.ThrowsAny<InvalidOperationException>(()=>reopened.BeginSend(first));
                Assert.ThrowsAny<InvalidOperationException>(()=>reopened.BeginSend(second));
                reopened.FinishSend(second,"UNKNOWN","Uncertain router outcome");
                Assert.ThrowsAny<InvalidOperationException>(()=>reopened.BeginSend(second));
            }
        }
        finally {Directory.Delete(root,true);}
    }
}
