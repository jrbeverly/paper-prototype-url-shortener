using System.Text.Json;
using Common.ErrorHandling;
using Stripe;

namespace ControlPlane.Api.Services;

public sealed class StripeBillingService : IBillingService
{
    private readonly ITenantRepository _tenants;
    private readonly IDomainRepository _domains;
    private readonly ILinkRepository _links;
    private readonly IStripePlanService _planService;
    private readonly IPlanChangeNotificationService _notifications;
    private readonly ILogger<StripeBillingService> _logger;

    // Internal for test seam
    internal SubscriptionService SubscriptionService { get; }

    public StripeBillingService(
        ITenantRepository tenants,
        IDomainRepository domains,
        ILinkRepository links,
        IStripePlanService planService,
        IPlanChangeNotificationService notifications,
        ILogger<StripeBillingService> logger)
    {
        _tenants = tenants;
        _domains = domains;
        _links = links;
        _planService = planService;
        _notifications = notifications;
        _logger = logger;
        SubscriptionService = new SubscriptionService();
    }

    // Test constructor
    internal StripeBillingService(
        ITenantRepository tenants,
        IDomainRepository domains,
        ILinkRepository links,
        IStripePlanService planService,
        IPlanChangeNotificationService notifications,
        ILogger<StripeBillingService> logger,
        SubscriptionService subscriptionService)
    {
        _tenants = tenants;
        _domains = domains;
        _links = links;
        _planService = planService;
        _notifications = notifications;
        _logger = logger;
        SubscriptionService = subscriptionService;
    }

    private static readonly IReadOnlyList<string> _planTiers = new[]
        { "free", "starter", "pro", "team", "business", "enterprise" };

    internal static int TierOf(string planId)
    {
        var key = planId.ToLowerInvariant();
        for (int i = 0; i < _planTiers.Count; i++)
            if (string.Equals(_planTiers[i], key, StringComparison.OrdinalIgnoreCase))
                return i;
        throw new BadRequestException($"Unknown plan: '{planId}'");
    }

    public async Task<PlanChangeResult> UpgradePlanAsync(
        TenantEntity tenant, string targetPlanId, CancellationToken ct = default)
    {
        var currentPlan = PlanCatalog.Get(tenant.Plan);
        var targetPlan = PlanCatalog.Get(targetPlanId);
        var currentTier = TierOf(tenant.Plan);
        var targetTier = TierOf(targetPlanId);

        if (targetTier <= currentTier)
            throw new BadRequestException(
                $"'{targetPlanId}' is not a higher plan than current plan '{tenant.Plan}'. Use the downgrade endpoint to move to a lower plan.");

        if (string.IsNullOrEmpty(tenant.StripeSubscriptionId))
            throw new BadRequestException(
                "Tenant does not have an active Stripe subscription. Use the checkout flow to subscribe first.");

        var pricing = await _planService.GetPricingAsync(ct);
        if (!pricing.TryGetValue(targetPlanId, out var targetPricing) || targetPricing.PriceId is null)
            throw new BadRequestException($"Plan '{targetPlanId}' does not have a Stripe Price configured.");

        // Update Stripe subscription: new price with prorated charge.
        Subscription subscription;
        try
        {
            var updateOptions = new SubscriptionUpdateOptions
            {
                Items =
                [
                    new SubscriptionItemOptions
                    {
                        Price = targetPricing.PriceId,
                        Quantity = 1
                    }
                ],
                ProrationBehavior = "create_prorations",
                Metadata = new Dictionary<string, string> { ["plan"] = targetPlanId }
            };
            subscription = await SubscriptionService.UpdateAsync(
                tenant.StripeSubscriptionId, updateOptions, cancellationToken: ct);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe subscription upgrade failed for tenant {TenantId}: {Error}",
                tenant.Id, ex.StripeError?.Message ?? ex.Message);
            throw new BadRequestException(
                $"Failed to upgrade subscription: {ex.StripeError?.Message ?? ex.Message}");
        }

        var previousPlan = tenant.Plan;
        var updated = tenant with
        {
            Plan = targetPlanId,
            MaxDomains = targetPlan.MaxDomains,
            MaxLinksPerDomain = targetPlan.MaxLinksPerDomain,
            ScheduledPlan = null,
            ScheduledPlanChangeAt = null,
            UpdatedAt = DateTime.UtcNow
        };

        await _tenants.UpdateAsync(updated);
        await _notifications.NotifyUpgradedAsync(updated, previousPlan, ct);

        _logger.LogInformation(
            "Tenant {TenantId} upgraded from {PreviousPlan} to {NewPlan} (immediate, prorated)",
            tenant.Id, previousPlan, targetPlanId);

        return new PlanChangeResult
        {
            Tenant = updated,
            IsImmediate = true,
            Warnings = []
        };
    }

    public async Task<PlanChangeResult> DowngradePlanAsync(
        TenantEntity tenant, string targetPlanId, CancellationToken ct = default)
    {
        var currentTier = TierOf(tenant.Plan);
        var targetTier = TierOf(targetPlanId);

        if (targetTier >= currentTier)
            throw new BadRequestException(
                $"'{targetPlanId}' is not a lower plan than current plan '{tenant.Plan}'. Use the upgrade endpoint to move to a higher plan.");

        if (string.IsNullOrEmpty(tenant.StripeSubscriptionId))
            throw new BadRequestException(
                "Tenant does not have an active Stripe subscription.");

        var pricing = await _planService.GetPricingAsync(ct);
        if (!pricing.TryGetValue(targetPlanId, out var targetPricing) || targetPricing.PriceId is null)
            throw new BadRequestException($"Plan '{targetPlanId}' does not have a Stripe Price configured.");

        // Update Stripe subscription: new price at period end (no proration).
        Subscription subscription;
        try
        {
            var updateOptions = new SubscriptionUpdateOptions
            {
                Items =
                [
                    new SubscriptionItemOptions
                    {
                        Price = targetPricing.PriceId,
                        Quantity = 1
                    }
                ],
                ProrationBehavior = "none",
                Metadata = new Dictionary<string, string> { ["plan"] = targetPlanId }
            };
            subscription = await SubscriptionService.UpdateAsync(
                tenant.StripeSubscriptionId, updateOptions, cancellationToken: ct);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe subscription downgrade failed for tenant {TenantId}: {Error}",
                tenant.Id, ex.StripeError?.Message ?? ex.Message);
            throw new BadRequestException(
                $"Failed to schedule subscription downgrade: {ex.StripeError?.Message ?? ex.Message}");
        }

        // Stripe.NET v51 does not expose CurrentPeriodEnd as a direct property on Subscription.
        // Access via RawJsonElement instead — the Stripe API always returns this field.
        var scheduledAt = TryGetDateTime(subscription.RawJsonElement, "current_period_end")
            ?? DateTime.UtcNow.AddMonths(1);

        // Enforce new plan limits immediately.
        var targetPlan = PlanCatalog.Get(targetPlanId);
        var updated = tenant with
        {
            MaxDomains = targetPlan.MaxDomains,
            MaxLinksPerDomain = targetPlan.MaxLinksPerDomain,
            ScheduledPlan = targetPlanId,
            ScheduledPlanChangeAt = scheduledAt,
            UpdatedAt = DateTime.UtcNow
        };

        await _tenants.UpdateAsync(updated);

        var warnings = await CheckUsageAgainstLimitsAsync(tenant.Id, targetPlan);

        await _notifications.NotifyDowngradeScheduledAsync(updated, targetPlanId, scheduledAt, ct);

        _logger.LogInformation(
            "Tenant {TenantId} downgrade scheduled: {CurrentPlan} → {TargetPlan}, effective {ScheduledAt:O}",
            tenant.Id, tenant.Plan, targetPlanId, scheduledAt);

        return new PlanChangeResult
        {
            Tenant = updated,
            IsImmediate = false,
            Warnings = warnings
        };
    }

    public async Task<CancelSubscriptionResult> CancelSubscriptionAsync(
        TenantEntity tenant, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(tenant.StripeSubscriptionId))
            throw new BadRequestException(
                "Tenant does not have an active Stripe subscription to cancel.");

        Subscription subscription;
        try
        {
            var updateOptions = new SubscriptionUpdateOptions
            {
                CancelAtPeriodEnd = true
            };
            subscription = await SubscriptionService.UpdateAsync(
                tenant.StripeSubscriptionId, updateOptions, cancellationToken: ct);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe subscription cancellation failed for tenant {TenantId}: {Error}",
                tenant.Id, ex.StripeError?.Message ?? ex.Message);
            throw new BadRequestException(
                $"Failed to cancel subscription: {ex.StripeError?.Message ?? ex.Message}");
        }

        var periodEnd = TryGetDateTime(subscription.RawJsonElement, "current_period_end")
            ?? DateTime.UtcNow.AddMonths(1);

        var updated = tenant with
        {
            UpdatedAt = DateTime.UtcNow
        };

        await _tenants.UpdateAsync(updated);
        await _notifications.NotifyCancelledAsync(updated, periodEnd, ct);

        _logger.LogInformation(
            "Tenant {TenantId} subscription cancelled. Access until {PeriodEnd:O}, then reverts to Free.",
            tenant.Id, periodEnd);

        return new CancelSubscriptionResult
        {
            Tenant = updated,
            PeriodEnd = periodEnd
        };
    }

    private async Task<IReadOnlyList<UsageWarning>> CheckUsageAgainstLimitsAsync(
        Guid tenantId, PlanDefinition targetPlan)
    {
        var warnings = new List<UsageWarning>();

        var domainCount = await _domains.GetCountByTenantAsync(tenantId);
        if (domainCount > targetPlan.MaxDomains)
        {
            warnings.Add(new UsageWarning
            {
                Code = "domain_limit_exceeded",
                Message = $"You have {domainCount} domains, but the {targetPlan.Name} plan allows only {targetPlan.MaxDomains}. " +
                          "Existing domains remain active, but you cannot add new ones until you remove excess domains."
            });
        }

        // Per-domain link limit: check each domain for overages. We warn if any domain exceeds the new limit.
        // The link creation endpoint already enforces per-domain limits, so this is informational.
        var totalLinks = await _links.GetCountByTenantAsync(tenantId);
        if (totalLinks > 0 && targetPlan.MaxLinksPerDomain < int.MaxValue)
        {
            // We can't easily check per-domain counts without iterating all domains,
            // so we issue a general warning when link limits are lower.
            warnings.Add(new UsageWarning
            {
                Code = "link_limit_changed",
                Message = $"The {targetPlan.Name} plan allows {targetPlan.MaxLinksPerDomain} links per domain. " +
                          "Any domain exceeding this limit will not accept new links."
            });
        }

        return warnings;
    }

    private static DateTime? TryGetDateTime(System.Text.Json.JsonElement? element, string propertyName)
    {
        if (element is not { } json) return null;
        if (!json.TryGetProperty(propertyName, out var prop)) return null;
        return prop.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Number => DateTime.UnixEpoch.AddSeconds(prop.GetInt64()),
            _ => prop.TryGetDateTime(out var dt) ? dt : null
        };
    }
}
