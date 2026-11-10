using System.IO;
using NajmCard.Core.Models;
using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Desktop.Tests;

public sealed class VoucherBatchPrintOptionsTests
{
    private static string DirectoryForTest()
    {
        var folder=Path.Combine(Path.GetTempPath(),"najm-printed-batch-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    public void BatchDropdown_OnlyListsConfirmed_SentRows()
    {
        var folder=DirectoryForTest();
        try
        {
            using var store=new VoucherStore(folder);
            var sent=new Voucher("12345","topSecret","gold","batch-1",
                DateTimeOffset.UtcNow);
            var unknown=new Voucher("12346","otherSecret","gold","batch-2",
                DateTimeOffset.UtcNow);
            store.SaveBatch([sent,unknown]);
            store.BeginSend(sent);
            store.FinishSend(sent,"SENT");
            store.BeginSend(unknown);
            store.FinishSend(unknown,"UNKNOWN");
            var batches=store.ListPrintableBatches();
            var only=Assert.Single(batches);
            Assert.Equal("batch-1",only.BatchId);
            Assert.Equal(1,only.Count);
            Assert.Equal("gold",only.Profile);
            Assert.DoesNotContain("topSecret",only.DisplayName);
        }
        finally{Directory.Delete(folder,true);}
    }

    [Fact]
    public void ExtraCsv_UsesOnlyConfirmedBatch_NoPasswords_NoSpreadsheetFormulas()
    {
        var folder=DirectoryForTest();
        try
        {
            var confirmed=new Voucher("=HYPERLINK(\"x\")","privatePassword","gold",
                "batch-3",DateTimeOffset.UtcNow,SentToRouter:true,
                DeliveryStatus:"SENT");
            var destination=Path.Combine(folder,"confirmed.csv");
            VoucherBatchCsvExporter.Export([confirmed],destination);
            var output=File.ReadAllText(destination);
            Assert.Contains("batch-3",output);
            Assert.Contains("'=HYPERLINK",output);
            Assert.DoesNotContain("privatePassword",output);
            Assert.ThrowsAny<ArgumentException>(()=>VoucherBatchCsvExporter.Export(
                [confirmed with{DeliveryStatus="UNKNOWN"}],
                Path.Combine(folder,"unknown.csv")));
            Assert.False(File.Exists(Path.Combine(folder,"unknown.csv")));
        }
        finally{Directory.Delete(folder,true);}
    }
}
