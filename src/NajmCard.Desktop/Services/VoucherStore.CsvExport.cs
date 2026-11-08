using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace NajmCard.Desktop.Services;

/// <summary>
/// Streaming CSV reports, with no password ciphertext read or DPAPI call for
/// the default non-secret report. Same-directory staging and atomic replace
/// preserve existing reports if a query or filesystem write fails.
/// </summary>
public sealed partial class VoucherStore
{
    public string ExportCsv(string output, bool includePasswords)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        if(!string.Equals(Path.GetExtension(output),".csv",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("ملفات تقارير الكروت يجب أن تستخدم امتداد CSV.",nameof(output));
        var destination=Path.GetFullPath(output);
        var parent=Path.GetDirectoryName(destination) ??
            throw new ArgumentException("مجلد التصدير غير صالح.",nameof(output));

        // Important: NULL projection avoids selecting (and decrypting) the
        // password_protected column for password-free CSV reports.
        string secret=includePasswords?"password_protected":"NULL";
        using var db=Open();
        using var cmd=db.CreateCommand();
        cmd.CommandText=$"""
            SELECT username,{secret} AS selected_secret,profile,batch_id,
                created_at,sent,service,customer
            FROM vouchers ORDER BY created_at DESC LIMIT 10000
            """;
        using var reader=cmd.ExecuteReader();

        Directory.CreateDirectory(parent);
        var staged=Path.Combine(parent,".najm-csv-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using(var stream=new FileStream(staged,FileMode.CreateNew,FileAccess.Write,
                FileShare.None,8192,FileOptions.SequentialScan))
            {
                using(var writer=new StreamWriter(stream,new UTF8Encoding(true),
                    bufferSize:8192,leaveOpen:true))
                {
                    writer.WriteLine(includePasswords
                        ? "Username,Password,Profile,Batch,CreatedAt,Sent,Service,Customer"
                        : "Username,Profile,Batch,CreatedAt,Sent,Service,Customer");
                    while(reader.Read())
                    {
                        var fields=new List<string>(includePasswords?8:7)
                        {
                            reader.GetString(0)
                        };
                        if(includePasswords)
                            fields.Add(Unprotect(reader.GetString(1)));
                        fields.Add(reader.GetString(2));
                        fields.Add(reader.GetString(3));
                        fields.Add(reader.GetString(4));
                        fields.Add(reader.GetInt64(5)==1?"yes":"no");
                        fields.Add(reader.GetString(6));
                        fields.Add(reader.GetString(7));
                        writer.WriteLine(string.Join(",",fields.Select(Csv)));
                    }
                    writer.Flush();
                }
                stream.Flush(flushToDisk:true);
            }
            if(File.Exists(destination))File.Replace(staged,destination,null);
            else File.Move(staged,destination);
            return output;
        }
        finally
        {
            if(File.Exists(staged))File.Delete(staged);
        }
    }

    private static string Csv(string value) => "\"" +
        value.Replace("\"","\"\"",StringComparison.Ordinal)
            .Replace("\r"," ",StringComparison.Ordinal)
            .Replace("\n"," ",StringComparison.Ordinal) + "\"";
}
