namespace NajmCard.Core.Models;

public sealed record Voucher(string Username, string Password, string Profile,
    string BatchId, DateTimeOffset CreatedAt, bool SentToRouter = false,
    string Service = "hotspot", string Customer = "", string DeliveryStatus = "CREATED");

public sealed record VoucherGenerationOptions(int Count, string Prefix, int Length,
    string Characters, bool PasswordEqualsUsername, string Profile,
    int PasswordLength = 0, string? PasswordCharacters = null,
    bool LengthIncludesPrefix = false);

public sealed record BatchDeliveryResult(int Succeeded, IReadOnlyList<string> Failed,
    bool UncertainOutcome)
{
    public bool Success => Failed.Count == 0 && !UncertainOutcome;
}
