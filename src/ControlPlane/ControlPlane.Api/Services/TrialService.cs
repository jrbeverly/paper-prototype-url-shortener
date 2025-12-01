using ControlPlane.Api.Models.Responses;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Services;

public sealed class TrialService : ITrialService
{
    private readonly ITenantRepository _tenants;
    private readonly TrialOptions _options;
    private readonly ILogger<TrialService> _logger;

    public TrialService(ITenantRepository tenants, IOptions<TrialOptions> options, ILogger<TrialService> logger)
    {
        _tenants = tenants;
        _options = options.Value;
        _logger = logger;
    }

    public TrialStatusResponse GetStatus(TenantEntity tenant)
    {
        if (tenant.TrialEndsAt is null)
            return new TrialStatusResponse { IsOnTrial = false, HasUsedTrial = false };

        var now = DateTime.UtcNow;
        var isOnTrial = tenant.Status == "trialing" && tenant.TrialEndsAt.Value > now;
        var daysRemaining = isOnTrial
            ? (int?)Math.Ceiling((tenant.TrialEndsAt.Value - now).TotalDays)
            : null;

        return new TrialStatusResponse
        {
            IsOnTrial = isOnTrial,
            HasUsedTrial = true,
            TrialEndsAt = tenant.TrialEndsAt,
            DaysRemaining = daysRemaining,
            TrialPlan = isOnTrial ? tenant.TrialPlan : null
        };
    }

    public TenantEntity StartTrial(TenantEntity tenant)
    {
        // One trial per tenant — TrialEndsAt being set means a trial was already issued.
        if (tenant.TrialEndsAt.HasValue)
        {
            _logger.LogDebug(
                "Trial not started for tenant {TenantId}: trial already used",
                tenant.Id);
            return tenant;
        }

        var plan = PlanCatalog.Get(_options.TrialPlanId);
        var endsAt = DateTime.UtcNow.AddDays(_options.DurationDays);

        _logger.LogInformation(
            "Trial started for tenant {TenantId}: plan={TrialPlan}, ends={TrialEndsAt:O}",
            tenant.Id, plan.Id, endsAt);

        return tenant with
        {
            Plan = plan.Id,
            Status = "trialing",
            MaxDomains = plan.MaxDomains,
            MaxLinksPerDomain = plan.MaxLinksPerDomain,
            TrialEndsAt = endsAt,
            TrialPlan = plan.Id
        };
    }

    public async Task<TenantEntity> ExpireAsync(TenantEntity tenant, CancellationToken ct = default)
    {
        if (tenant.Status != "trialing")
            return tenant;

        var freePlan = PlanCatalog.Get("free");

        var updated = await _tenants.UpdateAsync(tenant with
        {
            Plan = freePlan.Id,
            Status = "active",
            MaxDomains = freePlan.MaxDomains,
            MaxLinksPerDomain = freePlan.MaxLinksPerDomain,
            TrialPlan = null,
            UpdatedAt = DateTime.UtcNow
        });

        _logger.LogInformation(
            "Trial expired for tenant {TenantId}: downgraded from {OldPlan} to free plan",
            tenant.Id, tenant.Plan);

        return updated;
    }

    public async Task<TenantEntity> ExtendAsync(TenantEntity tenant, int additionalDays, CancellationToken ct = default)
    {
        if (tenant.Status != "trialing" || tenant.TrialEndsAt is null)
            throw new InvalidOperationException($"Tenant {tenant.Id} is not currently on trial.");

        var newEnd = tenant.TrialEndsAt.Value.AddDays(additionalDays);

        var updated = await _tenants.UpdateAsync(tenant with
        {
            TrialEndsAt = newEnd,
            UpdatedAt = DateTime.UtcNow
        });

        _logger.LogInformation(
            "Trial extended for tenant {TenantId} by {AdditionalDays} days, new end={TrialEndsAt:O}",
            tenant.Id, additionalDays, newEnd);

        return updated;
    }
}
