using System.Globalization;
using ClosedXML.Excel;
using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// Exports the same 13-column shape as Hadramout exporting.py; absent legacy
/// fields remain empty rather than implying data NajmCard does not have.
/// </summary>
public static class VoucherExcelExporter
{
    public static IReadOnlyList<string> Headers { get; } =
    [
        "sequence", "username", "password", "profile", "server",
        "limit_uptime", "limit_bytes", "validity_days", "price",
        "status", "created_at", "comment", "service"
    ];

    public static string Export(IReadOnlyList<Voucher> vouchers, string output,
        string title = "Cards", bool includePasswords = false)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        if (!string.Equals(Path.GetExtension(output), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("يجب اختيار ملف Excel بامتداد .xlsx", nameof(output));

        string destination = Path.GetFullPath(output);
        string parent = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException("مسار الملف غير صالح", nameof(output));
        Directory.CreateDirectory(parent);
        string staged = Path.Combine(parent, ".najmcard-export-" + Guid.NewGuid().ToString("N") + ".tmp.xlsx");

        try
        {
            using (var book = new XLWorkbook())
            {
                var sheet = book.Worksheets.Add(CleanSheetTitle(title));
                sheet.RightToLeft = true;
                sheet.SheetView.FreezeRows(1);
                sheet.ShowGridLines = false;

                for (var c = 0; c < Headers.Count; c++)
                {
                    var cell = sheet.Cell(1, c + 1);
                    cell.SetValue(Headers[c]);
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#692038");
                    cell.Style.Font.FontColor = XLColor.FromHtml("#F1D49B");
                    cell.Style.Font.Bold = true;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                var row = 2;
                foreach (var card in vouchers)
                {
                    if (card is null)
                        throw new ArgumentException("قائمة الكروت تحتوي سجلاً غير صالح.");
                    var values = new[]
                    {
                        (row - 1).ToString(CultureInfo.InvariantCulture),
                        card.Username,
                        includePasswords ? card.Password : "",
                        card.Profile,
                        "", // server: not yet recorded by NajmCard
                        "", // limit_uptime: not yet recorded by NajmCard
                        "", // limit_bytes: not yet recorded by NajmCard
                        "", // validity_days: not yet recorded by NajmCard
                        "", // price: not yet recorded by NajmCard
                        card.SentToRouter ? "sent" : "local",
                        card.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                        "", // comment: not yet recorded by NajmCard
                        card.Service
                    };
                    for (var c = 0; c < values.Length; c++)
                    {
                        var value = values[c] ?? "";
                        if (value.Length > 32767)
                            throw new ArgumentException("تجاوزت قيمة في الكروت الحد المسموح لخلية Excel.");
                        var cell = sheet.Cell(row, c + 1);
                        cell.Style.NumberFormat.Format = "@";
                        cell.SetValue(value); // text value; never set FormulaA1
                    }
                    row++;
                }

                var range = sheet.Range(1, 1, Math.Max(1, row - 1), Headers.Count);
                range.SetAutoFilter();
                for (int c = 1; c <= Headers.Count; c++)
                    sheet.Column(c).Width = c switch
                    {
                        1 => 12,
                        2 => 20,
                        3 or 4 => 18,
                        11 => 30,
                        12 => 28,
                        _ => 16
                    };
                book.SaveAs(staged);
            }

            // Create in the destination folder and replace only after complete save.
            using (var outputStream = new FileStream(staged, FileMode.Open,
                       FileAccess.ReadWrite, FileShare.None))
                outputStream.Flush(flushToDisk: true);
            if (File.Exists(destination))
                File.Replace(staged, destination, null);
            else
                File.Move(staged, destination);
            return destination;
        }
        finally
        {
            if (File.Exists(staged)) File.Delete(staged);
        }
    }

    private static string CleanSheetTitle(string? name)
    {
        string source = string.IsNullOrWhiteSpace(name) ? "Cards" : name.Trim();
        var safe = new string(source.Where(c =>
            c >= 32 && @"\/:*?[]".IndexOf(c) < 0).Take(31).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "Cards" : safe;
    }
}
