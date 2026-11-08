using System.Globalization;
using System.Text.RegularExpressions;
using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// Strict RouterOS 6 usage classification. The only proof of expiry is an
/// exhausted strictly positive TOTAL Transfer Limit, never "Total Time Left".
/// Mirrors Hadramout core/network.py::_userman_lifecycle_v6.
/// </summary>
public static class UserManagerV6Usage
{
    private static string Get(IReadOnlyDictionary<string,string> row, string key) =>
        row.TryGetValue(key,out var result) ? result : "";

    public static long ParseBytes(string? value)
    {
        var raw = (value??"").Trim().Replace(" ","").ToLowerInvariant();
        if (raw.Length==0 || raw.Length>128 || raw is "unlimited" or "none")
            return 0;
        if (long.TryParse(raw,NumberStyles.Integer,CultureInfo.InvariantCulture,out var rawBytes))
            return Math.Max(0,rawBytes);
        var match=Regex.Match(raw,@"^([0-9]+(?:\.[0-9]+)?)([kmgt]?i?b?)$",
            RegexOptions.CultureInvariant);
        if (!match.Success || !double.TryParse(match.Groups[1].Value,
                NumberStyles.Float,CultureInfo.InvariantCulture,out var amount))
            return 0;
        var unit=match.Groups[2].Value;
        var power=unit.Length==0 ? 0 : unit[0] switch
        {
            'k'=>1,'m'=>2,'g'=>3,'t'=>4,'b'=>0,_=>-1
        };
        if (power<0) return 0;
        var bytes=amount*Math.Pow(1024,power);
        return double.IsFinite(bytes)&&bytes>=0&&bytes<long.MaxValue
            ? (long)bytes : 0;
    }

    private static bool Truth(string value) =>
        value.Equals("true",StringComparison.OrdinalIgnoreCase)
        || value.Equals("yes",StringComparison.OrdinalIgnoreCase)
        || value=="1";

    private static bool HasPositiveNumber(string value)
    {
        if (value.Length>128) return false;
        foreach (Match match in Regex.Matches(value,@"\d+(?:\.\d+)?"))
            if (double.TryParse(match.Value,NumberStyles.Float,
                CultureInfo.InvariantCulture,out var n) && n>0)
                return true;
        return false;
    }

    public static UserManagerV6Voucher Classify(
        IReadOnlyDictionary<string,string> row, long profileTransferLimit)
    {
        ArgumentNullException.ThrowIfNull(row);
        var id=Get(row,".id");
        var username=Get(row,"username");
        if (string.IsNullOrEmpty(username)) username=Get(row,"name");
        var profile=Get(row,"actual-profile");
        long down=ParseBytes(Get(row,"total-download-used"));
        long up=ParseBytes(Get(row,"total-upload-used"));
        if (!row.ContainsKey("total-download-used"))
            down=ParseBytes(Get(row,"download-used"));
        if (!row.ContainsKey("total-upload-used"))
            up=ParseBytes(Get(row,"upload-used"));
        long directLimit=ParseBytes(Get(row,"transfer-limit"));
        long transfer=directLimit>0 ? directLimit : Math.Max(0,profileTransferLimit);
        var consumed=down>long.MaxValue-up?long.MaxValue:down+up;
        var disabled=Truth(Get(row,"disabled"));
        var incomplete=Truth(Get(row,"incomplete"));
        var lastSeen=Get(row,"last-seen").Trim();
        var started=consumed>0 || HasPositiveNumber(Get(row,"money-used"))
            || HasPositiveNumber(Get(row,"uptime-used"))
            || Truth(Get(row,"active"))
            || HasPositiveNumber(Get(row,"active-sessions"))
            || !string.IsNullOrWhiteSpace(Get(row,"last-used-ip"))
            || !string.IsNullOrWhiteSpace(Get(row,"last-used-mac"))
            || (lastSeen.Length>0 && !lastSeen.Equals("never",StringComparison.OrdinalIgnoreCase)
                && !lastSeen.Equals("unknown",StringComparison.OrdinalIgnoreCase));
        string status,reason,source="";
        if (disabled) { status="disabled";reason="معطل إدارياً"; }
        else if (incomplete) { status="incomplete";reason="حساب غير مكتمل"; }
        else if (started && transfer>0 && consumed>=transfer)
        {
            status="expired";
            reason="استنفد حد إجمالي نقل البيانات";
            source="transfer-limit";
        }
        else if (started && profile.Length>0) { status="active";reason="بدأ الاستخدام"; }
        else if (!started && profile.Length>0) { status="valid";reason="لم يبدأ الاستخدام"; }
        else { status="unclassified";reason="لا يوجد دليل كافٍ على حالة الكرت"; }
        var rawSessions=Get(row,"active-sessions");
        int.TryParse(rawSessions,NumberStyles.Integer,CultureInfo.InvariantCulture,out int sessions);
        return new UserManagerV6Voucher(id,username,profile,status,reason,source,
            down,up,transfer,started,disabled,incomplete,
            Get(row,"uptime-used"),Get(row,"last-seen"),
            Math.Clamp(sessions,0,1000000));
    }
}
