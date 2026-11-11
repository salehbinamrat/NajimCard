using System.Globalization;

namespace NajmCard.Desktop.Services;

public sealed record VoucherDeliveryBatch(string Service, string BatchId, string Profile,
    DateTimeOffset CreatedAt, int Total, int Sent, int Pending, int Failed, int NeedsReview)
{
    public string ServiceLabel => Service switch
    {
        "hotspot" => "Hotspot",
        "userman" => "User Manager",
        "pppoe" => "PPPoE",
        _ => Service
    };

    public string DisplayName =>
        $"{Profile} · {ServiceLabel} · {CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm} · {BatchId}";
}

public sealed record VoucherDeliveryEntry(string Number, string State)
{
    public string Status => State switch
    {
        "SENT" => "مؤكد الإرسال",
        "CREATED" => "لم يرسل",
        "FAILED" => "فشل الإرسال",
        _ => "يحتاج مراجعة"
    };
}

public sealed partial class VoucherStore
{
    public IReadOnlyList<VoucherDeliveryBatch> ListDeliveryBatches()
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            WITH recent AS (
                SELECT service, batch_id, MIN(profile) AS profile, MAX(created_at) AS created_at,
                    COUNT(*) AS total,
                    SUM(CASE WHEN sent=1 AND delivery_status='SENT' THEN 1 ELSE 0 END) AS confirmed,
                    SUM(CASE WHEN sent=0 AND delivery_status='CREATED' THEN 1 ELSE 0 END) AS pending,
                    SUM(CASE WHEN sent=0 AND delivery_status='FAILED' THEN 1 ELSE 0 END) AS failed
                FROM vouchers
                WHERE batch_id <> ''
                GROUP BY service,batch_id
                ORDER BY created_at DESC,service,batch_id LIMIT 60
            )
            SELECT service,batch_id,profile,created_at,total,confirmed,pending,failed
            FROM recent ORDER BY created_at DESC,service,batch_id
            """;
        using var reader = command.ExecuteReader();
        var result = new List<VoucherDeliveryBatch>();
        while (reader.Read())
        {
            int total = reader.GetInt32(4), sent = reader.GetInt32(5);
            int pending = reader.GetInt32(6), failed = reader.GetInt32(7);
            result.Add(new VoucherDeliveryBatch(reader.GetString(0), reader.GetString(1),
                reader.GetString(2), DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                total, sent, pending, failed, total - sent - pending - failed));
        }
        return result;
    }

    public IReadOnlyList<VoucherDeliveryEntry> ReadDeliveryEntries(string service, string batchId)
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            SELECT username, CASE
                WHEN sent=1 AND delivery_status='SENT' THEN 'SENT'
                WHEN sent=0 AND delivery_status IN ('CREATED','FAILED') THEN delivery_status
                ELSE 'REVIEW' END
            FROM vouchers
            WHERE service=$service AND batch_id=$batch
            ORDER BY created_at,username
            """;
        command.Parameters.AddWithValue("$service", service);
        command.Parameters.AddWithValue("$batch", batchId);
        using var reader = command.ExecuteReader();
        var result = new List<VoucherDeliveryEntry>();
        while (reader.Read())
            result.Add(new VoucherDeliveryEntry(reader.GetString(0), reader.GetString(1)));
        return result;
    }
}
