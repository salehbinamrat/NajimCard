namespace NajmCard.Core.Services;

/// <summary>
/// RouterOS 6 can legitimately take minutes to enumerate tens of thousands of
/// User Manager accounts. Do not apply its slow-read allowance to writes.
/// </summary>
public sealed record RouterOsCommandTimeoutProfile(
    TimeSpan Total, TimeSpan Idle, int MaxRows, long MaxResponseBytes);

public static class RouterOsCommandTimeoutPolicy
{
    public static RouterOsCommandTimeoutProfile For(string command)
    {
        bool largeInventory = string.Equals(command,
            "/tool/user-manager/user/print", StringComparison.Ordinal);
        return largeInventory
            ? new RouterOsCommandTimeoutProfile(
                TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(2),
                250000, 128L * 1024 * 1024)
            : new RouterOsCommandTimeoutProfile(
                TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20),
                30000, 128L * 1024 * 1024);
    }
}
