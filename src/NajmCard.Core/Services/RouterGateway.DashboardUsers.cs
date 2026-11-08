using System.Globalization;
using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// Lightweight per-router dashboard inventory: only data used by KPI/chart
/// computations is projected. Never ship the password to the WPF dashboard.
/// A router that drops projected fields is retried unfiltered once, then
/// marked incompatible for the rest of this authenticated gateway session.
/// </summary>
public sealed partial class RouterGateway
{
    private bool _dashboardProjectionUnsupported;
    private const string DashboardProplist =
        ".id,name,profile,disabled,bytes-in,bytes-out,limit-bytes-total";

    public async Task<IReadOnlyList<HotspotUser>> ReadDashboardUsersAsync(
        CancellationToken ct = default)
    {
        IReadOnlyList<IReadOnlyDictionary<string,string>> rows;
        if (_dashboardProjectionUnsupported)
        {
            rows=await client.CommandAsync("/ip/hotspot/user/print",cancellationToken:ct);
        }
        else
        {
            rows=await client.CommandAsync("/ip/hotspot/user/print",
                new Dictionary<string,string> {[".proplist"]=DashboardProplist},ct);
            bool incomplete=rows.Any(row =>
                string.IsNullOrWhiteSpace(Get(row,"name")) ||
                !row.ContainsKey("profile") ||
                !row.ContainsKey("disabled") ||
                !row.ContainsKey("bytes-in") ||
                !row.ContainsKey("bytes-out") ||
                !row.ContainsKey("limit-bytes-total") ||
                !ValidCounter(row,"bytes-in") ||
                !ValidCounter(row,"bytes-out") ||
                !ValidCounter(row,"limit-bytes-total"));
            if(incomplete)
            {
                // Never mistake missing / silently ignored projected columns for
                // an authoritative zero. Use a full read, only if permissions allow.
                _dashboardProjectionUnsupported=true;
                rows=await client.CommandAsync("/ip/hotspot/user/print",cancellationToken:ct);
            }
        }
        if(rows.Any(row=>string.IsNullOrWhiteSpace(Get(row,"name"))))
            throw new InvalidDataException("لم يُعد الراوتر أسماء مستخدمي Hotspot اللازمة للوحة الرئيسية.");
        // Full RouterOS read is the legacy fallback, but the DTO still masks passwords.
        return rows.Select(row=>new HotspotUser(
            Get(row,".id"),Get(row,"name"),"",Get(row,"profile"),
            Disabled(row),Long(row,"bytes-in"),Long(row,"bytes-out"),
            Long(row,"limit-bytes-total"),"")).ToArray();
    }

    private static bool ValidCounter(IReadOnlyDictionary<string,string> row,string key)
        => long.TryParse(Get(row,key),NumberStyles.Integer,
            CultureInfo.InvariantCulture,out var value) && value>=0;
}
