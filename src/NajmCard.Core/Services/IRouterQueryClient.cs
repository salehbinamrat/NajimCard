namespace NajmCard.Core.Services;

/// <summary>
/// Exact-ID RouterOS API query; avoids downloading 24k+ User Manager accounts
/// for a single action. Requires server-side filter and no unfiltered fallback.
/// </summary>
public interface IRouterQueryClient
{
    Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> QueryByIdAsync(
        string command, string id, IReadOnlyDictionary<string,string>? attributes = null,
        CancellationToken cancellationToken = default);
}
