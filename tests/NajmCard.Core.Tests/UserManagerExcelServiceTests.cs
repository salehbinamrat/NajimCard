using System.IO.Compression;
using System.Xml.Linq;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class UserManagerExcelServiceTests
{
    [Fact]
    public void Export_Preserves_Actual_Voucher_Service_In_Existing_13_Columns()
    {
        var folder = Path.Combine(Path.GetTempPath(), "najmcard-um-excel", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var xlsx = Path.Combine(folder, "um.xlsx");
            var card = new Voucher("00112", "secret", "gold", "batch",
                DateTimeOffset.UtcNow, Service: "userman", Customer: "admin");
            VoucherExcelExporter.Export([card], xlsx);
            using var zip = ZipFile.OpenRead(xlsx);
            using var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            var sheet = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var cell = sheet.Descendants(ns + "c").Single(x => (string?)x.Attribute("r") == "M2");
            // ClosedXML may store shared strings or inline text, so test the semantic
            // stored value through the library's real reader, never a string index.
            using var wb = new ClosedXML.Excel.XLWorkbook(xlsx);
            Assert.Equal("userman", wb.Worksheet(1).Cell("M2").GetString());
            Assert.Equal("00112", wb.Worksheet(1).Cell("B2").GetString());
            Assert.Equal("", wb.Worksheet(1).Cell("C2").GetString());
        }
        finally { Directory.Delete(folder, true); }
    }
}
