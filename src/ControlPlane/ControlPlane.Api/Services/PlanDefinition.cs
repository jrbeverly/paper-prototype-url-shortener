namespace ControlPlane.Api.Services;

/// <summary>Immutable definition of a subscription plan including limits, features, and pricing metadata.</summary>
public sealed record PlanDefinition
{
    /// <summary>Lowercase slug used as the canonical plan identifier (e.g. "free", "starter").</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable plan name shown in UI (e.g. "Starter").</summary>
    public required string Name { get; init; }

    /// <summary>Monthly price in cents. 0 for Free; -1 means custom/contact-us pricing.</summary>
    public required int MonthlyPriceCents { get; init; }

    /// <summary>True for Enterprise where price is negotiated rather than listed.</summary>
    public required bool IsCustomPricing { get; init; }

    /// <summary>Maximum number of custom branded domains the tenant may add.</summary>
    public required int MaxDomains { get; init; }

    /// <summary>Maximum number of short links allowed per domain.</summary>
    public required int MaxLinksPerDomain { get; init; }

    /// <summary>Maximum number of clicks tracked per calendar month.</summary>
    public required int MaxTrackedClicksPerMonth { get; init; }

    /// <summary>How many days of click analytics are retained.</summary>
    public required int AnalyticsRetentionDays { get; init; }

    /// <summary>Feature flags included in this plan. Use <see cref="PlanFeatures"/> constants.</summary>
    public required IReadOnlySet<string> Features { get; init; }

    /// <summary>Whether a free trial period is offered when first subscribing to this plan.</summary>
    public required bool TrialAvailable { get; init; }

    /// <summary>Maximum number of links the tenant may create per minute via the API. Use int.MaxValue for unlimited.</summary>
    public int LinkCreationPerMinute { get; init; } = 60;

    /// <summary>Stripe Product ID. Populated after Stripe sync; null until first sync.</summary>
    public string? StripeProductId { get; init; }

    /// <summary>Stripe Price ID for the monthly recurring price. Null for Free and custom-priced plans, or until synced.</summary>
    public string? StripePriceId { get; init; }
}
