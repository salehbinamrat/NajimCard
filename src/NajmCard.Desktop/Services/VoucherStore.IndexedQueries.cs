using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using NajmCard.Core.Models;

namespace NajmCard.Desktop.Services;

/// <summary>
/// Indexed voucher lookups: read only the requested service/batch or confirmed
/// delivery subset. DPAPI decrypts only the returned rows. Also shares a single
/// mapper with the legacy GetVouchers API to prevent contract drift.
/// </summary>
public sealed record PrintableBatchSummary(string Service,string BatchId,string Profile,int Count)
{
    public string DisplayName => $"{BatchId} · {Profile} · {Count} كرت";
}

/// <summary>Metadata only; no decrypting card passwords for the dropdown.</summary>
public sealed partial class VoucherStore
{
    public IReadOnlyList<PrintableBatchSummary> ListPrintableBatches(int limit=40)
    {
        if(limit is <1 or >100)
            throw new ArgumentOutOfRangeException(nameof(limit));
        using var db=Open();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT service,batch_id,MIN(profile),COUNT(*)
            FROM vouchers
            WHERE sent=1 AND delivery_status='SENT'
            GROUP BY service,batch_id
            ORDER BY MAX(created_at) DESC LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$limit",limit);
        using var reader=cmd.ExecuteReader();
        var result=new List<PrintableBatchSummary>();
        while(reader.Read())
            result.Add(new PrintableBatchSummary(reader.GetString(0),reader.GetString(1),
                reader.GetString(2),checked((int)reader.GetInt64(3))));
        return result;
    }

    public IReadOnlyList<Voucher> GetBatchVouchers(string service,string batchId)
    {
        if(service is not ("hotspot" or "userman" or "pppoe"))
            throw new ArgumentException("خدمة الدفعة غير صالحة.",nameof(service));
        if(string.IsNullOrWhiteSpace(batchId) || batchId.Length > 256)
            throw new ArgumentException("معرف الدفعة غير صالح.",nameof(batchId));
        using var db=Open();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT username,password_protected,profile,batch_id,created_at,
                sent,service,customer,delivery_status FROM vouchers
            WHERE service=$service AND batch_id=$batch
            ORDER BY created_at DESC,username
            """;
        cmd.Parameters.AddWithValue("$service",service);
        cmd.Parameters.AddWithValue("$batch",batchId);
        return ReadVoucherRows(cmd);
    }

    public IReadOnlyList<Voucher> GetPrintableVouchers(int maxCount)
    {
        if(maxCount is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(maxCount),
                "عدد الكروت المطلوبة للمعاينة خارج الحدود.");
        using var db=Open();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT username,password_protected,profile,batch_id,created_at,
                sent,service,customer,delivery_status FROM vouchers
            WHERE delivery_status='SENT' AND sent=1
            ORDER BY created_at DESC,service,username
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$limit",maxCount);
        return ReadVoucherRows(cmd);
    }

    private static IReadOnlyList<Voucher> ReadVoucherRows(SqliteCommand command)
    {
        using var reader=command.ExecuteReader();
        var rows=new List<Voucher>();
        while(reader.Read())
        {
            rows.Add(new Voucher(reader.GetString(0),Unprotect(reader.GetString(1)),
                reader.GetString(2),reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4),CultureInfo.InvariantCulture),
                reader.GetInt64(5)==1,reader.GetString(6),reader.GetString(7),
                reader.GetString(8)));
        }
        return rows;
    }

    private static void EnsureIndexedQuerySchema(SqliteConnection db)
    {
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            CREATE INDEX IF NOT EXISTS idx_vouchers_service_batch
                ON vouchers(service,batch_id,created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_vouchers_printable
                ON vouchers(delivery_status,sent,created_at DESC);
            """;
        cmd.ExecuteNonQuery();
    }
}
