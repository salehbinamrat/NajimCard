namespace NajmCard.Core.Models;

/// <summary>RouterOS6 discovered neighbor, with no active LAN scan.</summary>
public sealed record RouterNeighborDetail(string Interface,string Address,
    string Address4,string MacAddress,string Identity,string Platform,
    string Version,string Board)
{
    public string PreferredAddress => string.IsNullOrWhiteSpace(Address4) ? Address : Address4;
}

/// <summary>Actual /ip/hotspot/print server, not a user profile or binding.</summary>
public sealed record RouterHotspotServerStatus(string Id,string Name,
    string Interface,string Profile,bool? Disabled,bool? Invalid,bool? Running)
{
    public string StateLabel => Disabled is true ? "معطل" :
        Invalid is true ? "غير صالح" : Running is true ? "يعمل" :
        Running is false ? "متوقف" : "غير معروف";
}

/// <summary>Only a verified single RouterOS record may be changed.</summary>
public sealed record RouterHotspotActionResult(bool Success,bool UncertainOutcome,string Diagnostic);
