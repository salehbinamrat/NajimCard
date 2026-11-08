namespace NajmCard.Core.Models;

/// <summary>Read-only RouterOS 6 network records. Null flags mean that the router did not return a trustworthy state.</summary>
public sealed record RouterNetworkInterface(string Id, string Name, string Type,
    bool? Running, bool? Disabled, string MacAddress)
{
    public string RunningLabel => Running is true ? "متصلة" : Running is false ? "غير متصلة" : "غير معروف";
    public string DisabledLabel => Disabled is true ? "معطلة" : Disabled is false ? "مفعلة" : "غير معروف";
}

public sealed record RouterIpAddress(string Id, string Address, string Network,
    string Interface, bool? Disabled)
{
    public string DisabledLabel => Disabled is true ? "معطلة" : Disabled is false ? "مفعلة" : "غير معروف";
}

public sealed record RouterNeighbor(string Id, string Identity, string Interface,
    string IpAddress, string MacAddress, string Platform);

public sealed record RouterNetworkInventory(
    IReadOnlyList<RouterNetworkInterface> Interfaces,
    IReadOnlyList<RouterIpAddress> Addresses,
    IReadOnlyList<RouterNeighbor> Neighbors);
