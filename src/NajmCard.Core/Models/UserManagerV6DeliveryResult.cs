namespace NajmCard.Core.Models;

/// <summary>
/// A confirmed UM voucher requires both a RouterOS /add receipt and a successful
/// create-and-activate-profile reply. Ambiguous writes may not be retried blindly.
/// </summary>
public sealed record UserManagerV6DeliveryResult(
    bool Success, bool UncertainOutcome, string? UserId, string? Error);
