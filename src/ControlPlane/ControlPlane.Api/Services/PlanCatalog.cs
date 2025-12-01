namespace ControlPlane.Api.Services;

/// <summary>
/// Single source of truth for all subscription plan definitions.
/// Plans are defined in code; Stripe Product/Price IDs are populated after a Stripe sync
/// (see <see cref="IStripePlanService"/>).
/// </summary>
public static class PlanCatalog
{
    private static readonly Dictionary<string, PlanDefinition> _plans =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["free"] = new PlanDefinition
            {
                Id = "free",
                Name = "Free",
                MonthlyPriceCents = 0,
                IsCustomPricing = false,
                MaxDomains = 3,
                MaxLinksPerDomain = 100,
                MaxTrackedClicksPerMonth = 1_000,
                AnalyticsRetentionDays = 30,
                Features = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                TrialAvailable = false,
                LinkCreationPerMinute = 20
            },
            ["starter"] = new PlanDefinition
            {
                Id = "starter",
                Name = "Starter",
                MonthlyPriceCents = 900,
                IsCustomPricing = false,
                MaxDomains = 10,
                MaxLinksPerDomain = 1_000,
                MaxTrackedClicksPerMonth = 10_000,
                AnalyticsRetentionDays = 90,
                Features = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    PlanFeatures.ApiAccess,
                    PlanFeatures.AnalyticsExport
                },
                TrialAvailable = true,
                LinkCreationPerMinute = 60
            },
            ["pro"] = new PlanDefinition
            {
                Id = "pro",
                Name = "Pro",
                MonthlyPriceCents = 2_900,
                IsCustomPricing = false,
                MaxDomains = 50,
                MaxLinksPerDomain = 10_000,
                MaxTrackedClicksPerMonth = 100_000,
                AnalyticsRetentionDays = 180,
                Features = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    PlanFeatures.ApiAccess,
                    PlanFeatures.AnalyticsExport,
                    PlanFeatures.AdvancedAnalytics,
                    PlanFeatures.BulkImport,
                    PlanFeatures.CustomQrCodes,
                    PlanFeatures.PasswordLinks
                },
                TrialAvailable = true,
                LinkCreationPerMinute = 200
            },
            ["team"] = new PlanDefinition
            {
                Id = "team",
                Name = "Team",
                MonthlyPriceCents = 7_900,
                IsCustomPricing = false,
                MaxDomains = 100,
                MaxLinksPerDomain = 50_000,
                MaxTrackedClicksPerMonth = 500_000,
                AnalyticsRetentionDays = 365,
                Features = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    PlanFeatures.ApiAccess,
                    PlanFeatures.AnalyticsExport,
                    PlanFeatures.AdvancedAnalytics,
                    PlanFeatures.BulkImport,
                    PlanFeatures.CustomQrCodes,
                    PlanFeatures.PasswordLinks,
                    PlanFeatures.TeamSeats
                },
                TrialAvailable = true,
                LinkCreationPerMinute = 500
            },
            ["business"] = new PlanDefinition
            {
                Id = "business",
                Name = "Business",
                MonthlyPriceCents = 24_900,
                IsCustomPricing = false,
                MaxDomains = 500,
                MaxLinksPerDomain = 200_000,
                MaxTrackedClicksPerMonth = 2_000_000,
                AnalyticsRetentionDays = 730,
                Features = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    PlanFeatures.ApiAccess,
                    PlanFeatures.AnalyticsExport,
                    PlanFeatures.AdvancedAnalytics,
                    PlanFeatures.BulkImport,
                    PlanFeatures.CustomQrCodes,
                    PlanFeatures.PasswordLinks,
                    PlanFeatures.TeamSeats,
                    PlanFeatures.Sso,
                    PlanFeatures.WhiteLabel,
                    PlanFeatures.PrioritySupport
                },
                TrialAvailable = false,
                LinkCreationPerMinute = 1000
            },
            ["enterprise"] = new PlanDefinition
            {
                Id = "enterprise",
                Name = "Enterprise",
                MonthlyPriceCents = 0,
                IsCustomPricing = true,
                MaxDomains = 1_000,
                MaxLinksPerDomain = 1_000_000,
                MaxTrackedClicksPerMonth = int.MaxValue,
                AnalyticsRetentionDays = 1_095,
                Features = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    PlanFeatures.ApiAccess,
                    PlanFeatures.AnalyticsExport,
                    PlanFeatures.AdvancedAnalytics,
                    PlanFeatures.BulkImport,
                    PlanFeatures.CustomQrCodes,
                    PlanFeatures.PasswordLinks,
                    PlanFeatures.TeamSeats,
                    PlanFeatures.Sso,
                    PlanFeatures.WhiteLabel,
                    PlanFeatures.PrioritySupport
                },
                TrialAvailable = false,
                LinkCreationPerMinute = int.MaxValue
            }
        };

    /// <summary>All plans in display order (Free → Enterprise).</summary>
    public static IReadOnlyList<PlanDefinition> All { get; } = [
        _plans["free"],
        _plans["starter"],
        _plans["pro"],
        _plans["team"],
        _plans["business"],
        _plans["enterprise"]
    ];

    /// <summary>Set of all valid plan ID strings (case-insensitive).</summary>
    public static IReadOnlySet<string> ValidIds { get; } =
        new HashSet<string>(_plans.Keys, StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the plan definition, or <c>null</c> if the ID is unknown.</summary>
    public static PlanDefinition? TryGet(string planId) =>
        _plans.TryGetValue(planId, out var plan) ? plan : null;

    /// <summary>Returns the plan definition or throws if the ID is unknown.</summary>
    public static PlanDefinition Get(string planId) =>
        _plans.TryGetValue(planId, out var plan)
            ? plan
            : throw new InvalidOperationException($"Unknown plan: '{planId}'");

    /// <summary>Returns (MaxDomains, MaxLinksPerDomain) for <paramref name="planId"/>, falling back to Free limits for unknown IDs.</summary>
    public static (int MaxDomains, int MaxLinksPerDomain) GetLimits(string planId)
    {
        var def = TryGet(planId) ?? _plans["free"];
        return (def.MaxDomains, def.MaxLinksPerDomain);
    }
}
