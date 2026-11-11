using System.IO;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Desktop.Tests;

public sealed class HadramoutBatchPrintSnapshotTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),
        "najm-hadramout-batch-"+Guid.NewGuid().ToString("N"));

    public HadramoutBatchPrintSnapshotTests()=>Directory.CreateDirectory(_root);
    public void Dispose()=>Directory.Delete(_root,true);

    private static Voucher Card(string user,string batch,string profile="200RY",
        string service="userman",string customer="admin")=>
        new(user,"secret"+user,profile,batch,DateTimeOffset.UtcNow,
            Service:service,Customer:customer);

    private static VoucherBatchPrintSnapshot Snapshot(Voucher card,
        string title="مظهر 200 ريال",int rows=15)=>
        new(card.Service,card.BatchId,card.Profile,card.Customer,
            new CardPrintOptions(Rows:rows,Columns:4,Order:"right_to_left"),
            new VoucherCardAppearance(NetworkTitle:title,
                UsernameColor:"#221122",BorderWidth:1.5),
            new VoucherOutputPreferences("طابعة الشبكة",true,true,false));

    [Fact]
    public void Saves_Voucher_And_Exact_Print_Snapshot_Atomically_And_Reloads()
    {
        var first=Card("123456","batch-A");
        var other=Card("654321","batch-B","300RY");
        using(var store=new VoucherStore(_root))
        {
            store.SaveBatch([first],Snapshot(first));
            store.SaveBatch([other],Snapshot(other,"مظهر 300 ريال",rows:8));
            Assert.ThrowsAny<ArgumentException>(()=>store.SaveBatch(
                [Card("999999","batch-C")],Snapshot(first)));
            Assert.Empty(store.GetBatchVouchers("userman","batch-C"));
        }
        using var reopened=new VoucherStore(_root);
        var saved=reopened.LoadBatchPrintSnapshot("userman","batch-A")!;
        Assert.Equal("batch-A",saved.BatchId);
        Assert.Equal("200RY",saved.Profile);
        Assert.Equal("مظهر 200 ريال",saved.Appearance.NetworkTitle);
        Assert.Equal(15,saved.Print.Rows);
        Assert.Equal("طابعة الشبكة",saved.Output!.PrinterName);
        Assert.True(saved.Output.ExportCsv);
        Assert.Equal("مظهر 300 ريال",reopened.LoadBatchPrintSnapshot(
            "userman","batch-B")!.Appearance.NetworkTitle);
    }

    [Fact]
    public void Changing_Package_Cannot_Mutate_Previously_Issued_Batch()
    {
        var card=Card("125541","batch-X");
        using var store=new VoucherStore(_root);
        store.SaveBatch([card],Snapshot(card,"الأصل"));
        store.SavePackagePreset(new VoucherPackagePreset(
            "192.168.220.1:8728","userman","200RY","admin",
            60,"",10,VoucherGenerator.Digits,true,10,VoucherGenerator.Digits,true,
            new CardPrintOptions(Rows:10,Columns:3),
            new VoucherCardAppearance(NetworkTitle:"تم تعديل الباقة"),
            true,new VoucherOutputPreferences("الافتراضية")));
        Assert.Equal("الأصل",store.LoadBatchPrintSnapshot(
            "userman","batch-X")!.Appearance.NetworkTitle);
        Assert.Equal(15,store.LoadBatchPrintSnapshot("userman","batch-X")!.Print.Rows);
    }

    [Fact]
    public void Legacy_And_Corrupted_Batches_Are_Not_Overwritten_Or_Inferred()
    {
        var legacy=Card("128000","legacy");
        using var store=new VoucherStore(_root);
        store.SaveBatch([legacy]);
        Assert.Null(store.LoadBatchPrintSnapshot("userman","legacy"));
        var other=Card("128001","different");
        store.SaveBatch([other],Snapshot(other,"مختلف"));
        Assert.ThrowsAny<InvalidOperationException>(()=>
            store.SaveBatchPrintSnapshot(Snapshot(other) with
                {BatchId="legacy",Customer="wrong-owner"}));
        Assert.Null(store.LoadBatchPrintSnapshot("userman","legacy"));
        Assert.Equal("مختلف",store.LoadBatchPrintSnapshot(
            "userman","different")!.Appearance.NetworkTitle);
    }

    [Fact]
    public void Explicit_Print_Reconfiguration_Only_Updates_Selected_Batch()
    {
        var first=Card("555777","first");
        var second=Card("555888","second");
        using var store=new VoucherStore(_root);
        store.SaveBatch([first],Snapshot(first,"A"));
        store.SaveBatch([second],Snapshot(second,"B"));
        store.SaveBatchPrintSnapshot(Snapshot(first,"مقاس أحدث",rows:10));
        Assert.Equal(10,store.LoadBatchPrintSnapshot(
            "userman","first")!.Print.Rows);
        Assert.Equal("مقاس أحدث",store.LoadBatchPrintSnapshot(
            "userman","first")!.Appearance.NetworkTitle);
        Assert.Equal("B",store.LoadBatchPrintSnapshot(
            "userman","second")!.Appearance.NetworkTitle);
    }
}
