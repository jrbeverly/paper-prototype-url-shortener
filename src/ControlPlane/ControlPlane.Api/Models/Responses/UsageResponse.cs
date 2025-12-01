namespace ControlPlane.Api.Models.Responses;

public sealed record UsageResponse
{
    /// <summary>The tenant this usage report belongs to.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>The tenant's active plan slug (e.g. "free", "pro").</summary>
    public required string Plan { get; init; }

    /// <summary>The billing period these metrics cover.</summary>
    public required UsagePeriodResponse Period { get; init; }

    /// <summary>Current consumption within the billing period.</summary>
    public required UsageMetricsResponse Current { get; init; }

    /// <summary>Plan limits that apply to this tenant.</summary>
    public required UsageLimitsResponse Limits { get; init; }

    /// <summary>Usage that exceeds plan limits.</summary>
    public required UsageOverageResponse Overage { get; init; }

    /// <summary>Current alert level: "none", "warning80", "warning90", or "limitReached".</summary>
    public required string AlertLevel { get; init; }
}

public sealed record UsagePeriodResponse
{
    /// <summary>UTC start of the billing period.</summary>
    public required DateTime Start { get; init; }

    /// <summary>UTC end of the billing period.</summary>
    public required DateTime End { get; init; }
}

public sealed record UsageMetricsResponse
{
    /// <summary>Clicks tracked toward the monthly quota within this billing period.</summary>
    public required long TrackedClicks { get; init; }

    /// <summary>Number of active (non-deleted) domains.</summary>
    public required int ActiveDomains { get; init; }

    /// <summary>Number of active (non-deleted) links across all domains.</summary>
    public required int ActiveLinks { get; init; }
}

public sealed record UsageLimitsResponse
{
    /// <summary>Monthly tracked click limit for the current plan. Null means unlimited (Enterprise).</summary>
    public required int? MaxTrackedClicksPerMonth { get; init; }

    /// <summary>Maximum number of domains allowed on the current plan.</summary>
    public required int MaxDomains { get; init; }

    /// <summary>Maximum number of links per domain on the current plan.</summary>
    public required int MaxLinksPerDomain { get; init; }
}

public sealed record UsageOverageResponse
{
    /// <summary>Clicks recorded beyond the monthly limit. Zero when within limits or on Enterprise.</summary>
    public required long Clicks { get; init; }
}
