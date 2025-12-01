using RedirectService.Api.Models;

namespace RedirectService.Api.Services;

/// <summary>
/// Orchestrates redirect resolution: lookup, rule evaluation, destination URL construction, and click recording.
/// </summary>
public interface IRedirectService
{
    /// <summary>
    /// Resolves a redirect for the given request.
    /// Returns a result describing whether to redirect, and if so, where.
    /// </summary>
    Task<RedirectResult> ResolveAsync(RedirectRequest request, CancellationToken ct = default);
}
