namespace NajmCard.Core.Models;

/// <summary>
/// Definitive creation requires a single RouterOS6 /ppp/secret/add !done ret.
/// Missing reply/connection failure is UNKNOWN; do not automatically retry.
/// </summary>
public sealed record PppoeVoucherDeliveryResult(
    bool Success,bool UncertainOutcome,string? UserId,string? Error);
