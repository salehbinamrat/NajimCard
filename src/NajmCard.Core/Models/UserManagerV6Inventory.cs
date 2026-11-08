namespace NajmCard.Core.Models;

/// <summary>
/// A single RouterOS v6 User Manager row. Counts are for voucher usage lifecycle,
/// not current session connections. No credentials are copied to this model.
/// </summary>
public sealed record UserManagerV6Voucher(
    string Id, string Username, string Profile, string Status, string Reason,
    string ExpirySource, long DownloadBytes, long UploadBytes,
    long TransferLimit, bool Started, bool Disabled, bool Incomplete,
    string UptimeUsed="",string LastSeen="",int ActiveSessions=0)
{
    public long ConsumedBytes => DownloadBytes > long.MaxValue - UploadBytes
        ? long.MaxValue : DownloadBytes + UploadBytes;
    public long RemainingBytes => TransferLimit<=0 ? 0 :
        Math.Max(0,TransferLimit-ConsumedBytes);
    public string RemainingLabel => TransferLimit<=0 ? "غير محدد" : RemainingBytes.ToString();
}

public sealed record UserManagerV6Inventory(
    IReadOnlyList<UserManagerV6Voucher> Users, bool IsComplete, string Diagnostic)
{
    public int Total => Users.Count;
    public int Valid => Users.Count(x=>x.Status=="valid");
    public int Active => Users.Count(x=>x.Status=="active");
    public int Expired => Users.Count(x=>x.Status=="expired");
    public int Disabled => Users.Count(x=>x.Status=="disabled");
    public int Incomplete => Users.Count(x=>x.Status=="incomplete");
    public int Unclassified => Users.Count(x=>x.Status=="unclassified");
}
