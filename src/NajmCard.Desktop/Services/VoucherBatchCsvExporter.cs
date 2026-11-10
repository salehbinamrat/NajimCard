using System.IO;
using System.Text;
using NajmCard.Core.Models;

namespace NajmCard.Desktop.Services;

/// <summary>Exports only the confirmed selected batch. Does not expose secrets.</summary>
public static class VoucherBatchCsvExporter
{
    public static string Export(IReadOnlyList<Voucher> vouchers,string destination)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        if(vouchers.Count is <1 or >10000 ||
           vouchers.Any(v=>v is null || v.DeliveryStatus!="SENT" || !v.SentToRouter))
            throw new ArgumentException("يجب تحديد دفعة مؤكدة الإرسال فقط.");
        if(!string.Equals(Path.GetExtension(destination),".csv",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("امتداد CSV مطلوب.");
        var full=Path.GetFullPath(destination);
        var folder=Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(folder);
        var temp=Path.Combine(folder,".najm-batch-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,
                FileShare.None,8192,FileOptions.SequentialScan))
            {
                using(var writer=new StreamWriter(stream,new UTF8Encoding(true),8192,
                    leaveOpen:true))
                {
                    writer.WriteLine("Username,Profile,Batch,CreatedAt,Service,Status");
                    foreach(var voucher in vouchers)
                    {
                        var fields=new[]{voucher.Username,voucher.Profile,voucher.BatchId,
                            voucher.CreatedAt.ToString("O"),voucher.Service,"SENT"};
                        writer.WriteLine(string.Join(",",fields.Select(Csv)));
                    }
                    writer.Flush();
                }
                stream.Flush(flushToDisk:true);
            }
            if(File.Exists(full))File.Replace(temp,full,null);
            else File.Move(temp,full);
            return full;
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }

    private static string Csv(string? raw)
    {
        var value=raw??"";
        // Prefix spreadsheet formula-looking cells with an apostrophe, including
        // whitespace-prefixed formulas, to avoid CSV injection on Excel open.
        var lead=value.TrimStart(' ','\t','\r','\n');
        if(lead.Length>0 && "=+-@".Contains(lead[0]))
            value="'"+value;
        return "\"" + value.Replace("\"","\"\"",StringComparison.Ordinal)
            .Replace("\r"," ",StringComparison.Ordinal)
            .Replace("\n"," ",StringComparison.Ordinal) + "\"";
    }
}
