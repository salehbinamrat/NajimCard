using System.IO.Compression;
using System.Xml.Linq;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>Executable XLSX integration tests: inspect the actual Office Open XML package.</summary>
public sealed class VoucherExcelExportTests
{
    private static Voucher Sample(string name = "001234", string password = "=2+2") =>
        new(name, password, "gold", "batch-7",
            DateTimeOffset.Parse("2026-11-07T12:30:00+03:00"), true);

    private static string TemporaryFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "najmcard-xlsx-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ReadCell(ZipArchive archive, XDocument sheet, string cellRef)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cell = sheet.Descendants(ns + "c")
            .Single(x => string.Equals((string?)x.Attribute("r"), cellRef, StringComparison.Ordinal));
        if ((string?)cell.Attribute("t") == "s")
        {
            var sstEntry = archive.GetEntry("xl/sharedStrings.xml")
                ?? throw new InvalidDataException("Missing sharedStrings.xml");
            using var stream = sstEntry.Open();
            var shared = XDocument.Load(stream).Descendants(ns + "si")
                .Select(si => string.Concat(si.Descendants(ns + "t").Select(t => t.Value))).ToArray();
            return shared[int.Parse(cell.Element(ns + "v")!.Value,
                System.Globalization.CultureInfo.InvariantCulture)];
        }
        if ((string?)cell.Attribute("t") == "inlineStr")
            return string.Concat(cell.Descendants(ns + "t").Select(t => t.Value));
        return cell.Element(ns + "v")?.Value ?? "";
    }

    [Fact]
    public void Exports_Hadramout_Thirteen_Columns_And_Preserves_Leading_Zeroes_And_Formula_Looking_Secrets()
    {
        var folder = TemporaryFolder();
        try
        {
            var output = Path.Combine(folder, "موقع-الشبكة.xlsx");
            var result = VoucherExcelExporter.Export([Sample()], output, "موقع:/الشبكة", true);
            Assert.Equal(output, result);
            Assert.True(File.Exists(output));
            using var zip = ZipFile.OpenRead(output);
            Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
            Assert.NotNull(zip.GetEntry("xl/workbook.xml"));
            using var sheetStream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            var sheet = XDocument.Load(sheetStream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            Assert.True(sheet.Descendants(ns + "sheetView").Any(x =>
                (string?)x.Attribute("rightToLeft") == "1"));
            Assert.True(sheet.Descendants(ns + "pane").Any(x =>
                (string?)x.Attribute("topLeftCell") == "A2"));
            var expected = new[]
            {
                "sequence", "username", "password", "profile", "server", "limit_uptime",
                "limit_bytes", "validity_days", "price", "status", "created_at", "comment", "service"
            };
            Assert.Equal(expected, VoucherExcelExporter.Headers);
            for (var col = 0; col < expected.Length; col++)
                Assert.Equal(expected[col], ReadCell(zip, sheet, $"{(char)('A' + col)}1"));
            Assert.Equal("001234", ReadCell(zip, sheet, "B2"));
            Assert.Equal("=2+2", ReadCell(zip, sheet, "C2"));
            Assert.Equal("gold", ReadCell(zip, sheet, "D2"));
            Assert.Equal("hotspot", ReadCell(zip, sheet, "M2"));
            Assert.Empty(sheet.Descendants(ns + "f")); // no executable spreadsheet formula
            using var workbookStream = zip.GetEntry("xl/workbook.xml")!.Open();
            var workbook = XDocument.Load(workbookStream);
            var title = workbook.Descendants(ns + "sheet").Single().Attribute("name")!.Value;
            Assert.DoesNotContain('/', title);
            Assert.DoesNotContain(':', title);
            Assert.True(title.Length <= 31);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Default_Export_Does_Not_Leak_Passwords_And_Does_Not_Change_Schema()
    {
        var folder = TemporaryFolder();
        try
        {
            var output = Path.Combine(folder, "public.xlsx");
            VoucherExcelExporter.Export([Sample()], output);
            using var zip = ZipFile.OpenRead(output);
            using var sheetStream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            var sheet = XDocument.Load(sheetStream);
            var passwordCell = sheet.Descendants(XName.Get("c",
                "http://schemas.openxmlformats.org/spreadsheetml/2006/main"))
                .FirstOrDefault(x => (string?)x.Attribute("r") == "C2");
            // Shared-string cells store a numeric index in <v>; decode that
            // index before checking for leaked credentials.
            var passwordText = passwordCell is null ? "" : ReadCell(zip, sheet, "C2");
            Assert.Equal("", passwordText);
            Assert.Equal("001234", ReadCell(zip, sheet, "B2"));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Failed_Export_Does_Not_Overwrite_Existing_File_Or_Leave_Temp_File()
    {
        var folder = TemporaryFolder();
        try
        {
            var output = Path.Combine(folder, "old.xlsx");
            var oldBytes = new byte[] { 7, 9, 11, 13 };
            File.WriteAllBytes(output, oldBytes);
            Assert.ThrowsAny<Exception>(() =>
                VoucherExcelExporter.Export([null!], output, "Cards", true));
            Assert.Equal(oldBytes, File.ReadAllBytes(output));
            Assert.Single(Directory.GetFiles(folder));
        }
        finally { Directory.Delete(folder, true); }
    }
}
