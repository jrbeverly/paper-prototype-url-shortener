namespace ControlPlane.Api.Models.Responses;

public sealed record PlanListResponse
{
    public required IReadOnlyList<PlanResponse> Plans { get; init; }
}

public sealed record PlanResponse
{
    /// <summary>Lowercase plan slug (e.g. "starter").</summary>
    public required string Id { get; init; }

    /// <summary>Display name (e.g. "Starter").</summary>
    public required string Name { get; init; }

    /// <summary>Monthly price in cents. 0 for Free; null for custom-priced plans.</summary>
    public int? MonthlyPriceCents { get; init; }

    /// <summary>True if price is custom / contact-sales (Enterprise).</summary>
    public required bool IsCustomPricing { get; init; }

    /// <summary>Resource limits enforced for tenants on this plan.</summary>
    public required PlanLimitsResponse Limits { get; init; }

    /// <summary>Feature flag strings included in this plan. See PlanFeatures constants.</summary>
    public required IReadOnlyList<string> Features { get; init; }

    /// <summary>True if a free trial period is offered when first subscribing.</summary>
    public required bool TrialAvailable { get; init; }

    /// <summary>Stripe Price ID for monthly billing. Null for Free, custom-priced plans, or when Stripe sync has not yet run.</summary>
    public string? StripePriceId { get; init; }
}

public sealed record PlanLimitsResponse
{
    public required int MaxDomains { get; init; }
    public required int MaxLinksPerDomain { get; init; }
    public required int MaxTrackedClicksPerMonth { get; init; }
    public required int AnalyticsRetentionDays { get; init; }
}
