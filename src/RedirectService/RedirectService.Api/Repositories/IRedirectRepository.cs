namespace RedirectService.Api.Repositories;

/// <summary>
/// Data access contract for redirect lookups.
/// Implementations must be thread-safe; a single instance is shared across
/// Lambda invocations within the same warm container.
/// </summary>
public interface IRedirectRepository
{
    /// <summary>
    /// Resolves a redirect by hostname and slug.
    /// Returns <see langword="null"/> when no active, non-expired redirect exists for the pair.
    /// </summary>
    Task<RedirectRecord?> GetAsync(string hostname, string slug, CancellationToken ct = default);

    /// <summary>
    /// Atomically increments the click counter for a link.
    /// Returns <see langword="true"/> if the increment succeeded (under click limit or no limit),
    /// <see langword="false"/> if the click limit has been reached.
    /// </summary>
    Task<bool> TryIncrementClickAsync(string hostname, string slug, CancellationToken ct = default);
}
