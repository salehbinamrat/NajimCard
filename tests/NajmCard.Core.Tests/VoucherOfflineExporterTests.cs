using System.Text;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>
/// Hadramout core/exporting.py parity for TXT and RouterOS v6 .rsc,
/// tested against actual filesystem bytes rather than static string checks.
/// </summary>
public sealed class VoucherOfflineExporterTests : IDisposable
{
    private readonly string folder=Path.Combine(Path.GetTempPath(),
        "najm-offline-export-tests-"+Guid.NewGuid().ToString("N"));

    public VoucherOfflineExporterTests() => Directory.CreateDirectory(folder);
    public void Dispose() => Directory.Delete(folder,true);

    private static Voucher Card(string name,string password="P123",
        string service="hotspot",string customer="") =>
        new(name,password,"gold","batch",DateTimeOffset.Parse("2026-11-07T08:00:00+03:00"),
            false,service,customer);

    [Fact]
    public void Credentials_Text_Is_UTF8_BOM_Quoted_CSV_And_Retains_Leading_Zeroes()
    {
        var dest=Path.Combine(folder,"cards.txt");
        VoucherOfflineExporter.ExportCredentialsText(
            [Card("00123","a,\"b\"\r\nc"),Card("00004","p")],dest);
        var bytes=File.ReadAllBytes(dest);
        Assert.Equal([0xEF,0xBB,0xBF],bytes.Take(3));
        var contents=Encoding.UTF8.GetString(bytes,3,bytes.Length-3);
        Assert.Equal("\"00123\",\"a,\"\"b\"\"\r\nc\"\n\"00004\",\"p\"\n",contents);
    }

    [Fact]
    public void Script_Quotes_RouterOS_Control_Characters_And_Protects_Duplicates()
    {
        var dest=Path.Combine(folder,"cards.rsc");
        VoucherOfflineExporter.ExportRouterOs6Script(
            [Card("00123","x\"$\\\n"),Card("00999","pw","pppoe")],dest);
        var data=File.ReadAllText(dest);
        Assert.Contains("/ip hotspot user find where name=\"00123\"",data);
        Assert.Contains("password=\"x\\\"\\$\\\\\\n\"",data);
        Assert.Contains("/ip hotspot user add name=\"00123\"",data);
        Assert.Contains("/ppp secret find where name=\"00999\"",data);
        Assert.Contains("/ppp secret add service=pppoe",data);
        Assert.DoesNotContain("x\"$\\\n",data);
        Assert.Contains("Voucher already exists; stopped",data);
    }

    [Fact]
    public void UserManagerV6_Script_Requires_Known_Customer_And_Guards_Activation()
    {
        var dest=Path.Combine(folder,"cards.rsc");
        VoucherOfflineExporter.ExportRouterOs6Script(
            [Card("00053","pass","userman","site$owner")],dest);
        var data=File.ReadAllText(dest);
        Assert.Contains("/tool user-manager user add username=\"00053\"",data);
        Assert.Contains("customer=\"site\\$owner\"",data);
        Assert.Contains(":local voucherId [/tool user-manager user find where username=\"00053\"]",data);
        Assert.Contains("numbers=$voucherId",data);
        Assert.Contains("profile=\"gold\"",data);
        Assert.Throws<ArgumentException>(()=>VoucherOfflineExporter.ExportRouterOs6Script(
            [Card("00054","pass","userman")],dest));
    }

    [Theory]
    [InlineData("hotspot")]
    [InlineData("pppoe")]
    [InlineData("userman")]
    public void Duplicate_Usernames_Do_Not_Overwrite_Existing_Export(string service)
    {
        var dest=Path.Combine(folder,service+".rsc");
        File.WriteAllText(dest,"IMPORTANT EXISTING EXPORT");
        Assert.Throws<ArgumentException>(()=>VoucherOfflineExporter.ExportRouterOs6Script(
            [Card("00123"),Card("00123","x",service,"customer")],dest));
        Assert.Equal("IMPORTANT EXISTING EXPORT",File.ReadAllText(dest));
        Assert.Single(Directory.GetFiles(folder));
    }

    [Fact]
    public void Empty_Username_And_Unknown_Service_Fail_Before_File_Write()
    {
        var dest=Path.Combine(folder,"invalid.rsc");
        Assert.Throws<ArgumentException>(()=>VoucherOfflineExporter.ExportRouterOs6Script(
            [Card("  ")],dest));
        Assert.Throws<ArgumentException>(()=>VoucherOfflineExporter.ExportRouterOs6Script(
            [Card("good","pass","unsupported")],dest));
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public void Wrong_Extension_Is_Rejected_And_Text_Export_Never_Executes_RouterOS()
    {
        var text=Path.Combine(folder,"cards.txt");
        var script=Path.Combine(folder,"cards.rsc");
        Assert.Throws<ArgumentException>(()=>VoucherOfflineExporter.ExportCredentialsText(
            [Card("00123")],script));
        Assert.Throws<ArgumentException>(()=>VoucherOfflineExporter.ExportRouterOs6Script(
            [Card("00123")],text));
        Assert.False(File.Exists(text));
        Assert.False(File.Exists(script));
    }
}
