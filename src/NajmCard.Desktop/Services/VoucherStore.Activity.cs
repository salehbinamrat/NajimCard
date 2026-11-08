using Microsoft.Data.Sqlite;
using NajmCard.Core.Services;

namespace NajmCard.Desktop.Services;

/// <summary>
/// Local operation history for NajmCard only, never RouterOS's logs.
/// No passwords, device fingerprints or raw router error strings are stored.
/// One creation and one continuously updated delivery event per batch.
/// </summary>
public sealed partial class VoucherStore
{
    public IReadOnlyList<VoucherActivityRow> ReadRecentActivities(int limit=6)
    {
        if(limit is <1 or >1000)throw new ArgumentOutOfRangeException(nameof(limit));
        using var db=Open();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT ts,category,action,status,detail FROM activities
            ORDER BY ts DESC,id DESC LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$limit",limit);
        using var reader=cmd.ExecuteReader();
        var list=new List<VoucherActivityRow>();
        while(reader.Read())
            list.Add(new VoucherActivityRow(
                reader.GetString(0),reader.GetString(1),
                reader.GetString(2),reader.GetString(3),reader.GetString(4)));
        return list;
    }
    public VoucherActivityPage ReadActivityPage(string? category=null,string? query=null,
        int page=0,int pageSize=250)
    {
        if(page is <0 or >100000 || pageSize is <1 or >500)
            throw new ArgumentOutOfRangeException(nameof(page));
        category=(category??"").Trim();
        query=(query??"").Trim();
        if(category.Length>40 || query.Length>100)
            throw new ArgumentException("مرشح سجل النشاط طويل جدا.");
        using var db=Open();
        const string filter="""
            WHERE ($category='' OR category=$category)
            AND ($query='' OR instr(lower(action),lower($query))>0
                OR instr(lower(detail),lower($query))>0
                OR instr(lower(status),lower($query))>0)
            """;
        using var count=db.CreateCommand();
        count.CommandText="SELECT COUNT(*) FROM activities "+filter;
        count.Parameters.AddWithValue("$category",category);
        count.Parameters.AddWithValue("$query",query);
        int total=Convert.ToInt32(count.ExecuteScalar(),System.Globalization.CultureInfo.InvariantCulture);
        int pages=Math.Max(1,(int)((total+(long)pageSize-1)/pageSize));
        int current=Math.Min(page,pages-1);
        using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT ts,category,action,status,detail FROM activities "+
            filter+" ORDER BY ts DESC,id DESC LIMIT $size OFFSET $offset";
        cmd.Parameters.AddWithValue("$category",category);
        cmd.Parameters.AddWithValue("$query",query);
        cmd.Parameters.AddWithValue("$size",pageSize);
        cmd.Parameters.AddWithValue("$offset",(long)current*pageSize);
        using var reader=cmd.ExecuteReader();
        var items=new List<VoucherActivityRow>();
        while(reader.Read())
            items.Add(new VoucherActivityRow(reader.GetString(0),reader.GetString(1),
                reader.GetString(2),reader.GetString(3),reader.GetString(4)));
        return new VoucherActivityPage(items,total,current,pageSize);
    }

    /// <summary>One durable local audit event per package command outcome.
    /// Never store router error text, credentials, or raw external responses.
    /// Failed audit writes must never trigger replay of a network command.</summary>
    public void RecordPackageOperation(string service,string operation,
        string profile,string outcome)
    {
        if(service is not ("hotspot" or "userman" or "pppoe"))
            throw new ArgumentException("خدمة غير معروفة.",nameof(service));
        if(operation is not ("create" or "edit" or "remove" or "rate"))
            throw new ArgumentException("نوع عملية غير معروف.",nameof(operation));
        if(outcome is not ("SUCCESS" or "UNKNOWN" or "ERROR"))
            throw new ArgumentException("نتيجة سجل غير معروفة.",nameof(outcome));
        profile=(profile??"").Trim();
        if(profile.Length is <1 or >64 || profile.Any(char.IsControl)
            || profile.Contains("password=",StringComparison.OrdinalIgnoreCase)
            || profile.Contains("secret=",StringComparison.OrdinalIgnoreCase)
            || profile.Contains("token=",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("اسم باقة غير صالح للتسجيل.",nameof(profile));
        string description=operation switch
        {
            "create"=>"إنشاء باقة",
            "edit"=>"تعديل باقة",
            "remove"=>"حذف باقة",
            _=>"تعديل سرعة باقة"
        };
        using var db=Open();
        using var tx=db.BeginTransaction();
        WriteActivity(db,tx,"package:"+Guid.NewGuid().ToString("N"),
            "NETWORK",description,outcome,"الخدمة "+service+" | الباقة "+profile);
        tx.Commit();
    }

    /// <summary>Stores an aggregate outcome only. Never persist card numbers,
    /// account names, raw RouterOS messages, or the failed-account list.</summary>
    public void RecordExpiredCleanup(string service,ExpiredTransferDeletionResult result)
    {
        if(service is not ("hotspot" or "userman"))
            throw new ArgumentException("خدمة تنظيف الكروت غير صالحة.",nameof(service));
        ArgumentNullException.ThrowIfNull(result);
        if(result.Requested<0 || result.Deleted<0 || result.Skipped<0
            || result.Deleted>result.Requested || result.Skipped>result.Requested)
            throw new ArgumentException("أرقام عملية الحذف غير صحيحة.",nameof(result));
        var state=result.UncertainOutcome?"UNKNOWN":
            result.Failed.Count>0 || result.Skipped>0?"PARTIAL":"SUCCESS";
        var summary="الخدمة "+service+
            " | المطلوب "+result.Requested+
            " | المحذوف "+result.Deleted+
            " | المتخطى "+result.Skipped+
            " | الفشل "+result.Failed.Count+
            " | حالة التأكيد "+state;
        using var db=Open();
        using var tx=db.BeginTransaction();
        WriteActivity(db,tx,"expired-cleanup:"+Guid.NewGuid().ToString("N"),
            "NETWORK","حذف الكروت المنتهية",state,summary);
        tx.Commit();
    }

    internal static void EnsureActivitySchema(SqliteConnection db)
    {
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS activities (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                ts TEXT NOT NULL,
                category TEXT NOT NULL,
                action TEXT NOT NULL,
                status TEXT NOT NULL,
                detail TEXT NOT NULL,
                batch_key TEXT NOT NULL,
                UNIQUE(batch_key,action)
            );
            CREATE INDEX IF NOT EXISTS idx_najm_activity_recent ON activities(ts DESC,id DESC);
            """;
        cmd.ExecuteNonQuery();
    }
    private static void WriteActivity(SqliteConnection db,SqliteTransaction tx,
        string batchKey,string category,string action,string status,string detail)
    {
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            INSERT INTO activities(ts,category,action,status,detail,batch_key)
            VALUES($ts,$category,$action,$status,$detail,$batch)
            ON CONFLICT(batch_key,action) DO UPDATE SET
              ts=excluded.ts,status=excluded.status,detail=excluded.detail
            """;
        cmd.Parameters.AddWithValue("$ts",DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$category",category);
        cmd.Parameters.AddWithValue("$action",action);
        cmd.Parameters.AddWithValue("$status",status);
        cmd.Parameters.AddWithValue("$detail",detail);
        cmd.Parameters.AddWithValue("$batch",batchKey);
        cmd.ExecuteNonQuery();
    }
    private static string ActivityBatchKey(string service,string batch)
        => service+"\u0001"+batch;
}
public sealed record VoucherActivityRow(string Timestamp,string Category,string Action,
    string Status,string Detail);

public sealed record VoucherActivityPage(IReadOnlyList<VoucherActivityRow> Items,int Total,int Page,int PageSize);
