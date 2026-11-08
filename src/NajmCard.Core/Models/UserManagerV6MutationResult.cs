namespace NajmCard.Core.Models;

/// <summary>Outcome of a single User Manager v6 mutation. Ambiguous writes are never retried.</summary>
public sealed record UserManagerV6MutationResult(
    bool Success, bool UncertainOutcome, string Diagnostic);
