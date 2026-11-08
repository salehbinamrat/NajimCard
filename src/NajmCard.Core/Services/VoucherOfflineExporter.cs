using System.Text;
using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// Offline exports based on Hadramout core/exporting.py: no RouterOS I/O.
/// Files containing credentials must be requested explicitly by the UI.
/// </summary>
public static class VoucherOfflineExporter
{
    private const int MaximumCards = 10000;

    public static string ExportCredentialsText(IReadOnlyList<Voucher> vouchers, string destination)
    {
        ValidateVouchers(vouchers, requireScriptFields: false);
        return WriteStaged(destination, ".txt", writer =>
        {
            foreach (var card in vouchers)
                writer.Write(Csv(card.Username) + "," + Csv(card.Password) + "\n");
        });
    }

    public static string ExportRouterOs6Script(IReadOnlyList<Voucher> vouchers, string destination)
    {
        ValidateVouchers(vouchers, requireScriptFields: true);
        return WriteStaged(destination, ".rsc", writer =>
        {
            writer.Write("# NajmCard - offline RouterOS 6 voucher import\n");
            writer.Write("# Contains plaintext credentials; review target device before import.\n");
            writer.Write("# Existing usernames are guarded; this file never runs automatically.\n");
            foreach (var card in vouchers)
                WriteGuardedRouterScript(writer, card);
        });
    }

    private static void ValidateVouchers(IReadOnlyList<Voucher> vouchers, bool requireScriptFields)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        if (vouchers.Count > MaximumCards)
            throw new ArgumentOutOfRangeException(nameof(vouchers),
                "لا يمكن تصدير أكثر من 10000 كرت في ملف واحد.");
        var usernames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in vouchers)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Username) ||
                item.Username.Contains('\0') || !usernames.Add(item.Username))
                throw new ArgumentException("أسماء الكروت فارغة أو مكررة؛ لم ينشأ الملف.");
            if (item.Username.Length > 512 || item.Password.Length > 4096)
                throw new ArgumentException("حقل الكرت أكبر من الحد المسموح للتصدير.");
            if (!requireScriptFields) continue;
            if (item.Service is not ("hotspot" or "pppoe" or "userman") ||
                string.IsNullOrWhiteSpace(item.Profile) ||
                item.Profile.Length > 512 ||
                (item.Service == "userman" && string.IsNullOrWhiteSpace(item.Customer)))
                throw new ArgumentException(
                    "تعذّر إنشاء سكربت RouterOS بسبب خدمة أو باقة أو حساب مدير مستخدمين غير معروف.");
            if (item.Customer.Length > 512 || item.Profile.Contains('\0') ||
                item.Customer.Contains('\0') || item.Password.Contains('\0'))
                throw new ArgumentException("بيانات الكرت تحتوي رموزا غير صالحة للاستيراد.");
        }
    }

    private static string Csv(string input) =>
        "\"" + input.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>
    /// RouterOS script quotation: escape variable expansion, quotes, backslashes,
    /// line breaks and control bytes. Never concatenate raw input as script code.
    /// </summary>
    private static string RouterQuote(string raw)
    {
        var output = new StringBuilder(raw.Length + 2).Append('"');
        foreach (var ch in raw)
        {
            switch (ch)
            {
                case '\\': output.Append("\\\\"); break;
                case '"': output.Append("\\\""); break;
                case '$': output.Append("\\$"); break;
                case '\r': output.Append("\\r"); break;
                case '\n': output.Append("\\n"); break;
                case '\t': output.Append("\\t"); break;
                default:
                    if (ch < 32 || ch == 127)
                        output.Append('\\').Append(((int)ch).ToString("X2"));
                    else
                        output.Append(ch);
                    break;
            }
        }
        return output.Append('"').ToString();
    }

    private static void WriteGuardedRouterScript(TextWriter writer, Voucher card)
    {
        var qName = RouterQuote(card.Username);
        var qPass = RouterQuote(card.Password);
        var qProfile = RouterQuote(card.Profile);
        var menu = card.Service switch
        {
            "pppoe" => "/ppp secret",
            "userman" => "/tool user-manager user",
            _ => "/ip hotspot user"
        };
        var field = card.Service == "userman" ? "username" : "name";
        writer.WriteLine($":if ([:len [{menu} find where {field}={qName}]] = 0) do={{");
        switch (card.Service)
        {
            case "pppoe":
                writer.WriteLine($"/ppp secret add service=pppoe name={qName} password={qPass} profile={qProfile}");
                break;
            case "userman":
                var qCustomer = RouterQuote(card.Customer);
                writer.WriteLine($"/tool user-manager user add username={qName} password={qPass} customer={qCustomer}");
                writer.WriteLine($":local voucherId [/tool user-manager user find where username={qName}]");
                writer.WriteLine(":if ([:len $voucherId] != 1) do={ :error \"Cannot identify new voucher; stopped\" }");
                writer.WriteLine($"/tool user-manager user create-and-activate-profile numbers=$voucherId customer={qCustomer} profile={qProfile}");
                break;
            default:
                writer.WriteLine($"/ip hotspot user add name={qName} password={qPass} profile={qProfile}");
                break;
        }
        writer.WriteLine("} else={ :error \"Voucher already exists; stopped\" }");
    }

    private static string WriteStaged(string output, string extension, Action<TextWriter> write)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        if (!string.Equals(Path.GetExtension(output), extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("امتداد ملف التصدير غير مطابق لنوع الملف.", nameof(output));
        var destination = Path.GetFullPath(output);
        var parent = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException("تعذر تحديد مجلد التصدير.", nameof(output));
        Directory.CreateDirectory(parent);
        var staged = Path.Combine(parent, ".najm-offline-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(stream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 4096, leaveOpen: true))
                    write(writer);
                stream.Flush(flushToDisk: true);
            }
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
}
