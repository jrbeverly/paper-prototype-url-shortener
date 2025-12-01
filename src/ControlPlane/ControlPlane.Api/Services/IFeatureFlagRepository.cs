namespace ControlPlane.Api.Services;

/// <summary>Flag type discriminators. Determines how a flag is evaluated against a context.</summary>
public static class FlagTypes
{
    /// <summary>Globally on or off (controlled by the Enabled kill switch).</summary>
    public const string Boolean = "boolean";

    /// <summary>Enabled for a deterministic percentage of tenants based on a stable hash.</summary>
    public const string Percentage = "percentage";

    /// <summary>Enabled only for tenants explicitly listed in <see cref="FeatureFlagEntity.EnabledTenantIds"/>.</summary>
    public const string PerTenant = "per_tenant";

    /// <summary>Enabled only for tenants whose plan is in <see cref="FeatureFlagEntity.EnabledPlanIds"/>.</summary>
    public const string PerPlan = "per_plan";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Boolean, Percentage, PerTenant, PerPlan
    };
}

/// <summary>A single feature flag stored in the repository.</summary>
public sealed record FeatureFlagEntity
{
    /// <summary>Unique identifier in <c>feature.area.name</c> dot-notation, e.g. <c>feature.links.csv_export</c>.</summary>
    public required string Key { get; init; }

    /// <summary>Human-readable display name.</summary>
    public required string Name { get; init; }

    /// <summary>Optional description of the flag's purpose and expected lifetime.</summary>
    public string? Description { get; init; }

    /// <summary>Flag evaluation type: "boolean", "percentage", "per_tenant", or "per_plan".</summary>
    public required string FlagType { get; init; }

    /// <summary>
    /// Global kill switch. When <c>false</c> the flag always evaluates to disabled,
    /// regardless of type-specific targeting. Set to <c>false</c> for an emergency disable.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>For <c>percentage</c> flags: 0–100 inclusive. The deterministic bucket threshold.</summary>
    public int? RolloutPercentage { get; init; }

    /// <summary>For <c>per_tenant</c> flags: the allow-listed tenant IDs.</summary>
    public IReadOnlyList<Guid>? EnabledTenantIds { get; init; }

    /// <summary>For <c>per_plan</c> flags: the allow-listed plan IDs (case-insensitive).</summary>
    public IReadOnlyList<string>? EnabledPlanIds { get; init; }

    public required string CreatedBy { get; init; }
    public string? UpdatedBy { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }

    /// <summary>Immutable audit trail, oldest entry first.</summary>
    public IReadOnlyList<FlagAuditEntry> AuditTrail { get; init; } = [];
}

/// <summary>One record of a flag change for audit purposes.</summary>
public sealed record FlagAuditEntry
{
    /// <summary>Action token, e.g. "created", "enabled", "disabled", "updated", "deleted".</summary>
    public required string Action { get; init; }

    /// <summary>Subject or user ID of the actor.</summary>
    public required string PerformedBy { get; init; }

    /// <summary>Optional human-readable change summary.</summary>
    public string? Details { get; init; }

    public required DateTime Timestamp { get; init; }
}

/// <summary>Represents an update payload applied by <see cref="IFeatureFlagRepository.UpdateAsync"/>.</summary>
public sealed record FeatureFlagUpdate
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public bool? Enabled { get; init; }
    public string? FlagType { get; init; }
    public int? RolloutPercentage { get; init; }
    public IReadOnlyList<Guid>? EnabledTenantIds { get; init; }
    public IReadOnlyList<string>? EnabledPlanIds { get; init; }
}

public interface IFeatureFlagRepository
{
    Task<FeatureFlagEntity?> GetByKeyAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyList<FeatureFlagEntity>> ListAsync(CancellationToken ct = default);
    Task<FeatureFlagEntity> CreateAsync(FeatureFlagEntity entity, CancellationToken ct = default);

    /// <summary>
    /// Applies a partial update to an existing flag and appends the audit entry.
    /// Returns <c>null</c> when no flag with <paramref name="key"/> exists.
    /// </summary>
    Task<FeatureFlagEntity?> UpdateAsync(string key, FeatureFlagUpdate update, FlagAuditEntry auditEntry, CancellationToken ct = default);

    /// <summary>Hard-deletes a flag. Returns <c>false</c> when the key does not exist.</summary>
    Task<bool> DeleteAsync(string key, CancellationToken ct = default);
}
