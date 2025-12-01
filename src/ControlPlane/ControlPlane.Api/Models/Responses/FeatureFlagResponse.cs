namespace ControlPlane.Api.Models.Responses;

public sealed record FeatureFlagResponse
{
    /// <summary>Unique key, e.g. <c>feature.links.csv_export</c>.</summary>
    public required string Key { get; init; }

    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Flag type: "boolean", "percentage", "per_tenant", or "per_plan".</summary>
    public required string FlagType { get; init; }

    /// <summary>Global kill switch. When <c>false</c> the flag is always disabled.</summary>
    public required bool Enabled { get; init; }

    public int? RolloutPercentage { get; init; }
    public IReadOnlyList<Guid>? EnabledTenantIds { get; init; }
    public IReadOnlyList<string>? EnabledPlanIds { get; init; }

    public required string CreatedBy { get; init; }
    public string? UpdatedBy { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }

    /// <summary>Full audit trail, oldest entry first.</summary>
    public required IReadOnlyList<FlagAuditEntryResponse> AuditTrail { get; init; }
}

public sealed record FlagAuditEntryResponse
{
    /// <summary>Action token: "created", "enabled", "disabled", "updated", or "deleted".</summary>
    public required string Action { get; init; }

    public required string PerformedBy { get; init; }
    public string? Details { get; init; }
    public required DateTime Timestamp { get; init; }
}

public sealed record FeatureFlagListResponse
{
    public required IReadOnlyList<FeatureFlagResponse> Flags { get; init; }
    public required int TotalCount { get; init; }
}

/// <summary>Evaluated state of a feature flag for a specific tenant context.</summary>
public sealed record FeatureFlagEvaluationResponse
{
    public required string Key { get; init; }
    public required bool Enabled { get; init; }

    /// <summary>
    /// Why the flag evaluated to its value:
    /// "kill_switch", "boolean", "percentage", "per_tenant", "per_plan", or "not_found".
    /// </summary>
    public required string Reason { get; init; }
}

public sealed record FeatureFlagEvaluationsResponse
{
    public required IReadOnlyList<FeatureFlagEvaluationResponse> Flags { get; init; }
    public required int TotalCount { get; init; }
}
