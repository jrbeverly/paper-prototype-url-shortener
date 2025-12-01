using System.Text.Json;
using Common.ErrorHandling;
using ControlPlane.Api.Services;

namespace ControlPlane.Tests.Infrastructure;

/// <summary>
/// Test double for IBillingService that performs plan hierarchy validation,
/// tenant updates, and usage checks without calling Stripe.
/// </summary>
public sealed class TestBillingService : IBillingService
{
    public ITenantRepository? TenantRepository { get; set; }
    public IDomainRepository? DomainRepository { get; set; }
    public ILinkRepository? LinkRepository { get; set; }
    public IPlanChangeNotificationService? NotificationService { get; set; }

    /// <summary>When set, simulates a Stripe API failure on the next call.</summary>
    public bool SimulateStripeFailure { get; set; }

    private static readonly IReadOnlyList<string> _planTiers = new[]
        { "free", "starter", "pro", "team", "business", "enterprise" };

    public Task<PlanChangeResult> UpgradePlanAsync(
        TenantEntity tenant, string targetPlanId, CancellationToken ct = default)
    {
        if (SimulateStripeFailure)
            throw new BadRequestException("Failed to upgrade subscription: Stripe API error (simulated).");

        var currentTier = TierOf(tenant.Plan);
        var targetTier = TierOf(targetPlanId);

        if (targetTier <= currentTier)
            throw new BadRequestException(
                $"'{targetPlanId}' is not a higher plan than current plan '{tenant.Plan}'.");

        if (string.IsNullOrEmpty(tenant.StripeSubscriptionId))
            throw new BadRequestException(
                "Tenant does not have an active Stripe subscription.");

        var targetPlan = PlanCatalog.Get(targetPlanId);
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

        if (TenantRepository is not null)
            TenantRepository.UpdateAsync(updated);

        if (NotificationService is not null)
            NotificationService.NotifyUpgradedAsync(updated, previousPlan, ct);

        return Task.FromResult(new PlanChangeResult
        {
            Tenant = updated,
            IsImmediate = true,
            Warnings = []
        });
    }

    public async Task<PlanChangeResult> DowngradePlanAsync(
        TenantEntity tenant, string targetPlanId, CancellationToken ct = default)
    {
        if (SimulateStripeFailure)
            throw new BadRequestException("Failed to schedule subscription downgrade: Stripe API error (simulated).");

        var currentTier = TierOf(tenant.Plan);
        var targetTier = TierOf(targetPlanId);

        if (targetTier >= currentTier)
            throw new BadRequestException(
                $"'{targetPlanId}' is not a lower plan than current plan '{tenant.Plan}'.");

        if (string.IsNullOrEmpty(tenant.StripeSubscriptionId))
            throw new BadRequestException(
                "Tenant does not have an active Stripe subscription.");

        var scheduledAt = DateTime.UtcNow.AddDays(15); // mock end of billing period
        var targetPlan = PlanCatalog.Get(targetPlanId);
        var updated = tenant with
        {
            MaxDomains = targetPlan.MaxDomains,
            MaxLinksPerDomain = targetPlan.MaxLinksPerDomain,
            ScheduledPlan = targetPlanId,
            ScheduledPlanChangeAt = scheduledAt,
            UpdatedAt = DateTime.UtcNow
        };

        if (TenantRepository is not null)
            await TenantRepository.UpdateAsync(updated);

        var warnings = new List<UsageWarning>();
        if (DomainRepository is not null)
        {
            var domainCount = await DomainRepository.GetCountByTenantAsync(tenant.Id);
            if (domainCount > targetPlan.MaxDomains)
            {
                warnings.Add(new UsageWarning
                {
                    Code = "domain_limit_exceeded",
                    Message = $"You have {domainCount} domains, but the {targetPlan.Name} plan allows only {targetPlan.MaxDomains}."
                });
            }
        }

        if (LinkRepository is not null)
        {
            var totalLinks = await LinkRepository.GetCountByTenantAsync(tenant.Id);
            if (totalLinks > 0 && targetPlan.MaxLinksPerDomain < int.MaxValue)
            {
                warnings.Add(new UsageWarning
                {
                    Code = "link_limit_changed",
                    Message = $"The {targetPlan.Name} plan allows {targetPlan.MaxLinksPerDomain} links per domain."
                });
            }
        }

        if (NotificationService is not null)
            await NotificationService.NotifyDowngradeScheduledAsync(updated, targetPlanId, scheduledAt, ct);

        return new PlanChangeResult
        {
            Tenant = updated,
            IsImmediate = false,
            Warnings = warnings
        };
    }

    public Task<CancelSubscriptionResult> CancelSubscriptionAsync(
        TenantEntity tenant, CancellationToken ct = default)
    {
        if (SimulateStripeFailure)
            throw new BadRequestException("Failed to cancel subscription: Stripe API error (simulated).");

        if (string.IsNullOrEmpty(tenant.StripeSubscriptionId))
            throw new BadRequestException(
                "Tenant does not have an active Stripe subscription to cancel.");

        var periodEnd = DateTime.UtcNow.AddDays(15);
        var updated = tenant with { UpdatedAt = DateTime.UtcNow };

        if (TenantRepository is not null)
            TenantRepository.UpdateAsync(updated);

        if (NotificationService is not null)
            NotificationService.NotifyCancelledAsync(updated, periodEnd, ct);

        return Task.FromResult(new CancelSubscriptionResult
        {
            Tenant = updated,
            PeriodEnd = periodEnd
        });
    }

    private static int TierOf(string planId)
    {
        var key = planId.ToLowerInvariant();
        for (int i = 0; i < _planTiers.Count; i++)
            if (string.Equals(_planTiers[i], key, StringComparison.OrdinalIgnoreCase))
                return i;
        throw new BadRequestException($"Unknown plan: '{planId}'");
    }
}
