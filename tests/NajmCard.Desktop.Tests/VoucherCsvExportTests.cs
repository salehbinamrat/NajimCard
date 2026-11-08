using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using NajmCard.Core.Models;
using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Desktop.Tests;

/// <summary>
/// CSV exports against real SQLite and DPAPI. A metadata-only export must not
/// read or decrypt credentials, even if one encrypted field is unavailable.
/// </summary>
public sealed class VoucherCsvExportTests : IDisposable
{
    private readonly string folder=Path.Combine(Path.GetTempPath(),"najm-csv-"+Guid.NewGuid().ToString("N"));
    public VoucherCsvExportTests()=>Directory.CreateDirectory(folder);
    public void Dispose()=>Directory.Delete(folder,true);

    private static Voucher Card(string name,string pw,int minutes=0) =>
        new(name,pw,"Premium, Gold","batch-1",
            DateTimeOffset.Parse("2026-11-07T12:00:00+00:00").AddMinutes(minutes));

    [Fact]
    public void Metadata_Only_Csv_Streams_Without_Decrypting_Unreadable_Password_Blobs()
    {
        using var store=new VoucherStore(folder);
        store.SaveBatch([Card("001234","mySecret!",2),Card("000002","secret2",1)]);
        using(var db=new SqliteConnection(
            $"Data Source={Path.Combine(folder,"najmcard.db")};Pooling=False"))
        {
            db.Open();
            using var command=db.CreateCommand();
            command.CommandText="""
                UPDATE vouchers SET password_protected='not-base64'
                WHERE username='001234'
                """;
            Assert.Equal(1,command.ExecuteNonQuery());
        }
        var path=Path.Combine(folder,"public.csv");
        store.ExportCsv(path,false);
        var csv=File.ReadAllText(path,Encoding.UTF8);
        Assert.Contains("Username,Profile,Batch,CreatedAt,Sent,Service,Customer",csv);
        Assert.Contains("001234",csv);
        Assert.Contains("000002",csv);
        Assert.DoesNotContain("mySecret!",csv);
        Assert.DoesNotContain("secret2",csv);
        Assert.ThrowsAny<Exception>(()=>store.GetVouchers());
    }

    [Fact]
    public void Password_Export_Stays_OptIn_And_Retains_Leading_Zeroes_And_Quoting()
    {
        using var store=new VoucherStore(folder);
        store.SaveBatch([Card("000017","A,\"quoted\"",4)]);
        var path=Path.Combine(folder,"secret.csv");
        store.ExportCsv(path,true);
        var raw=File.ReadAllBytes(path);
        Assert.Equal(new byte[]{0xEF,0xBB,0xBF},raw.Take(3).ToArray());
        var data=Encoding.UTF8.GetString(raw);
        Assert.Contains("\"000017\",\"A,\"\"quoted\"\"\"",data);
        Assert.Contains("\"Premium, Gold\"",data);
    }

    [Fact]
    public void Existing_Export_Is_Not_Replaced_If_Password_Decrypt_Fails()
    {
        using var store=new VoucherStore(folder);
        store.SaveBatch([Card("000010","secret")]);
        using(var db=new SqliteConnection(
            $"Data Source={Path.Combine(folder,"najmcard.db")};Pooling=False"))
        {
            db.Open();
            using var command=db.CreateCommand();
            command.CommandText="UPDATE vouchers SET password_protected='tampered'";
            command.ExecuteNonQuery();
        }
        var path=Path.Combine(folder,"existing.csv");
        File.WriteAllText(path,"KEEP MY ORIGINAL REPORT");
        Assert.ThrowsAny<Exception>(()=>store.ExportCsv(path,true));
        Assert.Equal("KEEP MY ORIGINAL REPORT",File.ReadAllText(path));
    }

    [Fact]
    public void Invalid_Csv_Extension_Must_Not_Overwrite_Existing_User_File()
    {
        using var store=new VoucherStore(folder);
        store.SaveBatch([Card("000017","pw")]);
        var path=Path.Combine(folder,"important.txt");
        File.WriteAllText(path,"IMPORTANT USER DATA");
        Assert.Throws<ArgumentException>(()=>store.ExportCsv(path,false));
        Assert.Equal("IMPORTANT USER DATA",File.ReadAllText(path));
    }
}
