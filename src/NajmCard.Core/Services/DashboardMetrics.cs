using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>One consistent, testable snapshot for every dashboard KPI and chart.</summary>
public sealed record DashboardSnapshot(int Total, int Sessions, int Enabled,
    int Available, int ExpiredByTransfer, int DisabledNotExpired,
    IReadOnlyList<ProfileCount> Profiles);

public sealed record ProfileCount(string Name, int Count);

public static class DashboardMetrics
{
    public static DashboardSnapshot Calculate(IReadOnlyList<HotspotUser> users,
        IReadOnlyList<HotspotSession> sessions)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(sessions);
        var expired = users.Count(u => u.IsExpiredByTransferLimit);
        var available = users.Count(u => !u.Disabled && !u.IsExpiredByTransferLimit);
        var disabled = users.Count(u => u.Disabled && !u.IsExpiredByTransferLimit);
        var profiles = users.GroupBy(x => string.IsNullOrWhiteSpace(x.Profile) ? "default" : x.Profile)
            .Select(x => new ProfileCount(x.Key, x.Count()))
            .OrderByDescending(x => x.Count).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (available + expired + disabled != users.Count)
            throw new InvalidOperationException("Dashboard categories must be mutually exclusive and exhaustive.");
        return new DashboardSnapshot(users.Count, sessions.Count,
            users.Count(u => !u.Disabled), available, expired, disabled, profiles);
    }
}
