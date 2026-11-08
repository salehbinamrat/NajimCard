using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// RouterOS6 network discovery for the read-only Winbox-style tools view.
/// No network configuration write paths are exposed; all three projected lists
/// must finish successfully before returning a usable inventory.
/// </summary>
public sealed partial class RouterGateway
{
    private static bool? NetworkFlag(IReadOnlyDictionary<string,string> row,string key)
        => Get(row,key).Trim().ToLowerInvariant() switch
        {
            "yes" or "true" or "1" => true,
            "no" or "false" or "0" => false,
            _ => null
        };

    public async Task<RouterNetworkInventory> ReadNetworkInventoryAsync(CancellationToken ct = default)
    {
        var interfaces=await client.CommandAsync("/interface/print",
            new Dictionary<string,string>
            {
                [".proplist"]=".id,name,type,running,disabled,mac-address"
            },ct);
        if(interfaces.Any(row=>string.IsNullOrWhiteSpace(Get(row,"name"))))
            throw new InvalidDataException(
                "استجابة واجهات MikroTik لا تتضمن اسم الواجهة؛ لا يمكن الاعتماد على القائمة.");

        var addresses=await client.CommandAsync("/ip/address/print",
            new Dictionary<string,string>
            {
                [".proplist"]=".id,address,network,interface,disabled"
            },ct);
        if(addresses.Any(row=>string.IsNullOrWhiteSpace(Get(row,"address")) ||
            string.IsNullOrWhiteSpace(Get(row,"interface"))))
            throw new InvalidDataException(
                "استجابة عناوين IP غير مكتملة؛ لم تُعرض بيانات جزئية.");

        // Single bounded discovery path shared with the detailed neighbor
        // tools. Count-only preflight happens before any large neighbor table.
        var neighbors=await ReadNearbyDevicesAsync(ct);
        if(neighbors.Any(row=>string.IsNullOrWhiteSpace(row.Identity) &&
            string.IsNullOrWhiteSpace(row.MacAddress) &&
            string.IsNullOrWhiteSpace(row.PreferredAddress)))
            throw new InvalidDataException(
                "استجابة الأجهزة المجاورة لا تحتوي على معرفات يمكن عرضها بثقة.");

        return new RouterNetworkInventory(
            interfaces.Select(row=>new RouterNetworkInterface(
                Get(row,".id"),Get(row,"name"),Get(row,"type"),
                NetworkFlag(row,"running"),NetworkFlag(row,"disabled"),
                Get(row,"mac-address"))).ToArray(),
            addresses.Select(row=>new RouterIpAddress(
                Get(row,".id"),Get(row,"address"),Get(row,"network"),
                Get(row,"interface"),NetworkFlag(row,"disabled"))).ToArray(),
            neighbors.Select(row=>new RouterNeighbor(
                "",row.Identity,row.Interface,row.PreferredAddress,
                row.MacAddress,row.Platform)).ToArray());
    }
}
