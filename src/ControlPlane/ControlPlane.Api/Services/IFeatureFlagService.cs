namespace ControlPlane.Api.Services;

/// <summary>Evaluated state of a single feature flag for a given context.</summary>
public sealed record FeatureFlagEvaluation
{
    public required string Key { get; init; }
    public required bool Enabled { get; init; }

    /// <summary>
    /// Why the flag evaluated to its value:
    /// "kill_switch", "boolean", "percentage", "per_tenant", "per_plan", or "not_found".
    /// </summary>
    public required string Reason { get; init; }
}

public interface IFeatureFlagService
{
    /// <summary>
    /// Returns whether <paramref name="key"/> is enabled for the given context.
    /// An unknown flag always evaluates to <c>false</c>.
    /// </summary>
    Task<bool> IsEnabledAsync(string key, Guid? tenantId = null, string? planId = null, CancellationToken ct = default);

    /// <summary>Returns the evaluated state of every flag for the given tenant context.</summary>
    Task<IReadOnlyList<FeatureFlagEvaluation>> EvaluateAllAsync(Guid tenantId, string? planId, CancellationToken ct = default);

    /// <summary>Removes the cached entry for <paramref name="key"/> so the next read reflects the latest repository state.</summary>
    void InvalidateCache(string key);
}
