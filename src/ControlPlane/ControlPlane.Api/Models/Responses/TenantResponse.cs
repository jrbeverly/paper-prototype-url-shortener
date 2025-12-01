namespace ControlPlane.Api.Models.Responses;

public sealed record CreateTenantResponse
{
    /// <summary>The unique identifier of the new tenant.</summary>
    public required Guid Id { get; init; }

    /// <summary>The display name of the tenant workspace.</summary>
    public required string Name { get; init; }

    /// <summary>The billing contact email address.</summary>
    public required string Email { get; init; }

    /// <summary>The active subscription plan: free, starter, pro, or enterprise.</summary>
    public required string Plan { get; init; }

    /// <summary>The tenant status: active, trialing, past_due, or inactive.</summary>
    public required string Status { get; init; }

    /// <summary>Plan-based resource limits for this tenant.</summary>
    public required TenantLimits Limits { get; init; }

    /// <summary>When the tenant was created.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>Trial information. Non-null when the tenant started on a free trial.</summary>
    public TrialStatusResponse? Trial { get; init; }
}

public sealed record TenantLimits
{
    /// <summary>Maximum number of custom domains allowed.</summary>
    public required int MaxDomains { get; init; }

    /// <summary>Maximum number of links per domain.</summary>
    public required int MaxLinksPerDomain { get; init; }

    /// <summary>Maximum number of clicks tracked per calendar month.</summary>
    public required int MaxTrackedClicksPerMonth { get; init; }

    /// <summary>How many days of click analytics are retained.</summary>
    public required int AnalyticsRetentionDays { get; init; }
}
