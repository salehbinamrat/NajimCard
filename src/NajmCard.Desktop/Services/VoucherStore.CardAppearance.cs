using System.IO;
using System.Text.Json;
using NajmCard.Core.Models;

namespace NajmCard.Desktop.Services;

/// <summary>
/// Separate NajmCard app preference in the same independent SQLite backup.
/// It does not change voucher schema or any RouterOS/Hadramout state.
/// </summary>
public sealed partial class VoucherStore
{
    public VoucherCardAppearance LoadCardAppearance()
    {
        using var db=Open();
        EnsureAppearanceTable(db);
        using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT value FROM app_preferences WHERE key='voucher_card_appearance'";
        var raw=cmd.ExecuteScalar() as string;
        if(raw is null)return new VoucherCardAppearance(NetworkTitle:"نجم نت");
        try
        {
            if(raw.Length > 4096)
                throw new InvalidDataException("حجم إعدادات مظهر الكرت غير صالح.");
            return (JsonSerializer.Deserialize<VoucherCardAppearance>(raw)
                ?? throw new InvalidDataException("إعدادات مظهر الكرت فارغة.")).Validate();
        }
        catch(JsonException ex)
        {
            throw new InvalidDataException(
                "إعدادات مظهر الكرت غير قابلة للقراءة؛ لم تُعدّل النسخة المحفوظة.",ex);
        }
    }

    public void SaveCardAppearance(VoucherCardAppearance style)
    {
        ArgumentNullException.ThrowIfNull(style);
        style.Validate(); // Validate before opening/altering SQLite.
        // Global card appearance must be as portable as package presets.
        // Keep the original template untouched; persist an app-owned copy.
        style=EnsureStoredCardArtwork(style);
        var value=JsonSerializer.Serialize(style);
        if(value.Length>4096)
            throw new ArgumentException("إعدادات مظهر الكرت أكبر من الحد المسموح.");
        using var db=Open();
        EnsureAppearanceTable(db);
        using var tx=db.BeginTransaction();
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            INSERT INTO app_preferences(key,value) VALUES('voucher_card_appearance',$value)
            ON CONFLICT(key) DO UPDATE SET value=excluded.value
            """;
        cmd.Parameters.AddWithValue("$value",value);
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    private static void EnsureAppearanceTable(Microsoft.Data.Sqlite.SqliteConnection db)
    {
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS app_preferences(
                key TEXT PRIMARY KEY NOT NULL,
                value TEXT NOT NULL
            )
            """;
        cmd.ExecuteNonQuery();
    }
}
