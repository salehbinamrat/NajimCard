using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NajmCard.Core.Models;

namespace NajmCard.Desktop.Services;

/// <summary>
/// Per-batch historical print template, as in Hadramout's batches.print_settings_json.
/// The profile template may be edited later without changing issued cards.
/// </summary>
public sealed partial class VoucherStore
{
    private static void EnsureBatchPrintSchema(SqliteConnection db,SqliteTransaction? tx=null)
    {
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS card_batch_print_snapshots(
                service TEXT NOT NULL COLLATE NOCASE,
                batch_id TEXT NOT NULL,
                json TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                PRIMARY KEY(service,batch_id)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static void WriteBatchPrintSnapshot(SqliteConnection db,
        SqliteTransaction tx,VoucherBatchPrintSnapshot snapshot)
    {
        snapshot.Validate();
        var json=JsonSerializer.Serialize(snapshot);
        if(json.Length>16000)
            throw new ArgumentException("قالب طباعة الدفعة أكبر من الحد الآمن.");
        EnsureBatchPrintSchema(db,tx);
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            INSERT INTO card_batch_print_snapshots(service,batch_id,json,updated_utc)
            VALUES($service,$batch,$json,$now)
            ON CONFLICT(service,batch_id) DO UPDATE SET
                json=excluded.json,updated_utc=excluded.updated_utc
            """;
        cmd.Parameters.AddWithValue("$service",snapshot.Service);
        cmd.Parameters.AddWithValue("$batch",snapshot.BatchId);
        cmd.Parameters.AddWithValue("$json",json);
        cmd.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public VoucherBatchPrintSnapshot? LoadBatchPrintSnapshot(string service,string batchId)
    {
        if(service is not ("hotspot" or "userman" or "pppoe") ||
           string.IsNullOrWhiteSpace(batchId) || batchId.Length>256)
            throw new ArgumentException("معرّف دفعة الطباعة غير صالح.");
        using var db=Open();
        EnsureBatchPrintSchema(db);
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT json FROM card_batch_print_snapshots
            WHERE service=$service AND batch_id=$batch LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$service",service);
        cmd.Parameters.AddWithValue("$batch",batchId);
        var raw=cmd.ExecuteScalar() as string;
        if(raw is null)return null; // Legacy batches have no snapshot.
        try
        {
            if(raw.Length>16000)
                throw new InvalidDataException("حجم قالب الدفعة المحفوظ غير صالح.");
            var snapshot=(JsonSerializer.Deserialize<VoucherBatchPrintSnapshot>(raw)
                ??throw new InvalidDataException("قالب الدفعة المحفوظ فارغ.")).Validate();
            if(snapshot.Service!=service || snapshot.BatchId!=batchId)
                throw new InvalidDataException("مفتاح قالب الدفعة لا يطابق السجل.");
            return snapshot;
        }
        catch(JsonException ex)
        {
            throw new InvalidDataException(
                "تعذر قراءة قالب الدفعة المحفوظ؛ لن يتم استبداله بصمت.",ex);
        }
    }

    /// <summary>
    /// Explicit print/export action may update the historical batch layout,
    /// never its voucher credentials or router delivery status.
    /// </summary>
    public void SaveBatchPrintSnapshot(VoucherBatchPrintSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Validate();
        using var db=Open();
        using var tx=db.BeginTransaction();
        using(var check=db.CreateCommand())
        {
            check.Transaction=tx;
            check.CommandText="""
                SELECT COUNT(*),
                       SUM(CASE WHEN profile=$profile AND customer=$customer
                           THEN 1 ELSE 0 END)
                FROM vouchers WHERE service=$service AND batch_id=$batch
                """;
            check.Parameters.AddWithValue("$service",snapshot.Service);
            check.Parameters.AddWithValue("$batch",snapshot.BatchId);
            check.Parameters.AddWithValue("$profile",snapshot.Profile);
            check.Parameters.AddWithValue("$customer",snapshot.Customer);
            using var reader=check.ExecuteReader();
            if(!reader.Read() || reader.IsDBNull(0) || reader.GetInt64(0)<1 ||
               reader.IsDBNull(1) || reader.GetInt64(0)!=reader.GetInt64(1))
                throw new InvalidOperationException(
                    "تغيرت باقة أو حساب مدير دفعة الكروت؛ لم تُحفظ إعدادات طباعة خاطئة.");
        }
        WriteBatchPrintSnapshot(db,tx,snapshot);
        tx.Commit();
    }
}
