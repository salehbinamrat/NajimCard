using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NajmCard.Core.Models;

namespace NajmCard.Desktop.Services;

public sealed partial class VoucherStore
{
    /// <summary>
    /// Local-only stable settings: router endpoint, service, exact profile
    /// and User Manager customer all participate in the primary key.
    /// Legacy global card appearance remains independent for older data.
    /// </summary>
    public void SavePackagePreset(VoucherPackagePreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        preset.Validate();
        var value=JsonSerializer.Serialize(preset);
        if(value.Length>12000)
            throw new ArgumentException("إعدادات الباقة أكبر من الحد الآمن.");
        using var db=Open();
        EnsurePackagePresetsSchema(db);
        using var tx=db.BeginTransaction();
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            INSERT INTO card_package_presets(router_key,service,profile,customer,json,updated_utc)
            VALUES($router,$svc,$profile,$customer,$data,$updated)
            ON CONFLICT(router_key,service,profile,customer) DO UPDATE SET
              json=excluded.json,updated_utc=excluded.updated_utc
            """;
        cmd.Parameters.AddWithValue("$router",preset.RouterKey);
        cmd.Parameters.AddWithValue("$svc",preset.Service);
        cmd.Parameters.AddWithValue("$profile",preset.Profile);
        cmd.Parameters.AddWithValue("$customer",preset.Customer);
        cmd.Parameters.AddWithValue("$data",value);
        cmd.Parameters.AddWithValue("$updated",DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public VoucherPackagePreset? LoadPackagePreset(
        string routerKey,string service,string profile,string customer="")
    {
        if(string.IsNullOrWhiteSpace(routerKey) || routerKey.Length>253 ||
           service is not ("hotspot" or "userman" or "pppoe") ||
           string.IsNullOrWhiteSpace(profile) || customer is null)
            throw new ArgumentException("معرف الباقة أو الخدمة أو الراوتر غير صالح.");
        using var db=Open();
        EnsurePackagePresetsSchema(db);
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT json FROM card_package_presets
            WHERE router_key=$router AND service=$svc AND profile=$profile AND customer=$customer
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$router",routerKey);
        cmd.Parameters.AddWithValue("$svc",service);
        cmd.Parameters.AddWithValue("$profile",profile);
        cmd.Parameters.AddWithValue("$customer",customer);
        var raw=cmd.ExecuteScalar() as string;
        if(raw is null)return null;
        try
        {
            if(raw.Length>12000)throw new InvalidDataException("إعدادات الباقة تتجاوز الحد الآمن.");
            var value=(JsonSerializer.Deserialize<VoucherPackagePreset>(raw) ??
                throw new InvalidDataException("إعدادات الباقة المحفوظة فارغة.")).Validate();
            if(value.RouterKey!=routerKey || value.Service!=service ||
               value.Profile!=profile || value.Customer!=customer)
                throw new InvalidDataException("مفتاح إعدادات الباقة لا يطابق السجل المحفوظ.");
            return value;
        }
        catch(JsonException ex)
        {
            throw new InvalidDataException(
                "إعدادات الباقة تالفة ولم تُعدّل أو تُحذف.",ex);
        }
    }

    private static void EnsurePackagePresetsSchema(SqliteConnection db)
    {
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS card_package_presets (
                router_key TEXT NOT NULL,
                service TEXT NOT NULL,
                profile TEXT NOT NULL,
                customer TEXT NOT NULL,
                json TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                PRIMARY KEY(router_key,service,profile,customer)
            );
            """;
        cmd.ExecuteNonQuery();
    }
}
