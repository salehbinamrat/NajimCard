namespace NajmCard.Core.Models;

public sealed record RouterEndpoint(string Host, int Port, string Username, string Password, bool UseTls = false)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host) || Host.Length > 253 || Host.Any(char.IsWhiteSpace))
            throw new ArgumentException("عنوان الراوتر غير صالح.");
        if (Port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(Port));
        if (string.IsNullOrWhiteSpace(Username) || Username.Length > 100)
            throw new ArgumentException("اسم مستخدم الراوتر مطلوب.");
        if (string.IsNullOrEmpty(Password)) throw new ArgumentException("كلمة مرور الراوتر مطلوبة.");
        if (Host.Contains(':') && !System.Net.IPAddress.TryParse(Host, out _))
            throw new ArgumentException("عنوان IPv6 غير صالح.");
    }
}

public sealed record RouterResource(string Identity, string Board, string Version,
    string CpuLoad, string FreeMemory, string TotalMemory, string Uptime);

public sealed record HotspotUser(string Id, string Name, string Password, string Profile,
    bool Disabled, long BytesIn, long BytesOut, long TransferLimit, string Comment)
{
    public long ConsumedBytes => checked(BytesIn + BytesOut);
    public bool IsExpiredByTransferLimit => TransferLimit > 0 && ConsumedBytes >= TransferLimit;
}

public sealed record HotspotSession(string Id, string User, string Address, string MacAddress, string Uptime);

public sealed record RouterCardProfile(string Id, string Name, string RateLimit,
    string SessionTimeout = "", string SharedUsers = "");

/// <summary>Only mutable Hotspot profile fields; no login scripts, firewall or routing.</summary>
public sealed record HotspotProfileDraft(string Name,string RateLimit,
    string SessionTimeout,string SharedUsers);

public sealed record HotspotProfileMutationResult(
    bool Success,bool UncertainOutcome,string Diagnostic,string ProfileId = "");

public sealed record RouterHotspotServer(string Id, string Name, string Interface, bool Disabled);

/// <summary>PPP secret without revealing its configured password to the UI.</summary>
public sealed record PppAccount(string Id, string Name, string Profile, string Service,
    bool Disabled, bool Online, string Comment);

public sealed record PppProfile(string Id, string Name, string RateLimit,
    string LocalAddress, string RemoteAddress);

public sealed record PppActiveSession(string Id, string Name, string Service,
    string Address, string CallerId, string Uptime);

public sealed record PppInventory(
    IReadOnlyList<PppAccount> Accounts,
    IReadOnlyList<PppProfile> Profiles,
    IReadOnlyList<PppActiveSession> ActiveSessions);

/// <summary>Outcome of a verified single PPP account state change.</summary>
public sealed record PppAccountMutationResult(bool Success, bool UncertainOutcome,
    string Diagnostic);


public sealed record UserManagerV6Profile(string Id, string Name, string Owner,
    string Validity, string Price, string StartsAt);

public sealed record UserManagerV6Customer(string Id, string Login, string Name);

