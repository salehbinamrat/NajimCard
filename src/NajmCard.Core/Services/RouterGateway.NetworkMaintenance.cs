using System.Globalization;
using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// Source-grounded RouterOS6 read-only maintenance: strict neighbor count
/// preflight from !done =ret=, then only an explicitly projected subset.
/// Fail closed if a count cannot be trusted; no LAN probing or router writes.
/// </summary>
public sealed partial class RouterGateway
{
    public async Task<IReadOnlyList<RouterNeighborDetail>> ReadNearbyDevicesAsync(
        CancellationToken ct = default)
    {
        if(client is not IRouterReceiptClient receipts)
            throw new InvalidOperationException(
                "القراءة الآمنة للأجهزة تتطلب واجهة RouterOS تعيد قيمة count-only المؤكدة.");

        var count=await receipts.CommandWithReceiptAsync("/ip/neighbor/print",
            new Dictionary<string,string> {["count-only"]=""},ct);
        string? raw = count.Done.TryGetValue("ret",out var ret) ? ret :
            count.Done.TryGetValue("count",out var other) ? other : null;
        if(raw is null || !int.TryParse(raw,NumberStyles.None,
            CultureInfo.InvariantCulture,out var number) || number>5000)
            throw new InvalidDataException(
                "عدد الأجهزة المجاورة غير معروف أو تجاوز 5000؛ أُلغيت القراءة لحماية الأداء.");

        if(number==0)return Array.Empty<RouterNeighborDetail>();
        var rows=await client.CommandAsync("/ip/neighbor/print",
            new Dictionary<string,string>
            {
                [".proplist"]="interface,address,address4,mac-address,identity,platform,version,board"
            },ct);
        if(rows.Count>5000)
            throw new InvalidDataException(
                "زاد عدد الأجهزة المكتشفة عن 5000 أثناء القراءة؛ أُلغيت النتائج.");

        // Deliberately omit arbitrary RouterOS properties. Truncation also
        // bounds displayed untrusted device text without altering router data.
        static string Field(IReadOnlyDictionary<string,string> row,string key)
        {
            var value=Get(row,key);
            return value.Length<=120?value:value[..120];
        }
        return rows.Select(row=>new RouterNeighborDetail(
            Field(row,"interface"),
            Field(row,"address"),Field(row,"address4"),
            Field(row,"mac-address"),Field(row,"identity"),
            Field(row,"platform"),Field(row,"version"),
            Field(row,"board"))).ToArray();
    }

    public async Task<IReadOnlyList<RouterHotspotServerStatus>> ReadHotspotServerStatusAsync(
        CancellationToken ct = default)
    {
        var rows=await client.CommandAsync("/ip/hotspot/print",
            new Dictionary<string,string>
            {
                [".proplist"]=".id,name,interface,profile,disabled,invalid,running"
            },ct);
        if(rows.Count>5000)
            throw new InvalidDataException(
                "قائمة سيرفرات Hotspot تتجاوز الحد الآمن للقراءة.");

        static bool ValidId(string id) =>
            id.Length is >=2 and <=40 && id[0]=='*' &&
            id.Skip(1).All(char.IsAsciiHexDigit);

        var valid=rows.Where(row=>ValidId(Get(row,".id"))).ToArray();
        if(valid.Any(row=>string.IsNullOrWhiteSpace(Get(row,"name"))))
            throw new InvalidDataException(
                "أعاد الراوتر سيرفر Hotspot بدون اسم؛ لم تُعرض قائمة قد تكون غير موثوقة.");

        return valid.Select(row=>new RouterHotspotServerStatus(
            Get(row,".id"),Get(row,"name"),Get(row,"interface"),
            Get(row,"profile"),
            NetworkFlag(row,"disabled"),
            NetworkFlag(row,"invalid"),
            NetworkFlag(row,"running")))
            .OrderBy(x=>x.Name,StringComparer.OrdinalIgnoreCase)
            .ThenBy(x=>x.Interface,StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
