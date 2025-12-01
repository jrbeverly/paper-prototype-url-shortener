using RedirectService.Api.Models;

namespace RedirectService.Api.Repositories;

/// <summary>
/// Data access contract for domain-level error page configuration.
/// Used only on the error path; never called during a successful redirect.
/// Implementations must be thread-safe; a single instance is shared across Lambda invocations.
/// </summary>
public interface IDomainConfigRepository
{
    /// <summary>
    /// Returns the configuration for the given hostname, or <see langword="null"/> if no domain
    /// configuration record exists for that host.
    /// </summary>
    Task<DomainConfig?> GetAsync(string hostname, CancellationToken ct = default);
}
