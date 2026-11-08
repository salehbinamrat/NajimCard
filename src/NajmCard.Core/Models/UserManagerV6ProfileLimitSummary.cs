namespace NajmCard.Core.Models;

/// <summary>
/// Read-only summary of legacy User Manager v6 profile/limitation links.
/// Zero means no positive Transfer Limit was found, not an expired voucher.
/// </summary>
public sealed record UserManagerV6ProfileLimitSummary(
    string ProfileId, string ProfileName, long EffectiveTransferLimitBytes,
    int LinkedLimitationCount, string LimitationNames);
