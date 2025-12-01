using Common.ErrorHandling;

namespace ControlPlane.Api.Services;

public sealed class UsageService : IUsageService
{
    private readonly ITenantRepository _tenants;
    private readonly IUsageRepository _usageRepo;
    private readonly IDomainRepository _domains;
    private readonly ILinkRepository _links;
    private readonly IUsageAlertService _alertService;
    private readonly IUsageNotificationService _notifications;
    private readonly ILogger<UsageService> _logger;

    public UsageService(
        ITenantRepository tenants,
        IUsageRepository usageRepo,
        IDomainRepository domains,
        ILinkRepository links,
        IUsageAlertService alertService,
        IUsageNotificationService notifications,
        ILogger<UsageService> logger)
    {
        _tenants = tenants;
        _usageRepo = usageRepo;
        _domains = domains;
        _links = links;
        _alertService = alertService;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<TenantUsageSummary> GetUsageAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _tenants.GetByIdAsync(tenantId)
            ?? throw new NotFoundException("Tenant", tenantId.ToString());

        var (periodStart, periodEnd) = GetCurrentBillingPeriod(DateTime.UtcNow);

        var usageRecord = await _usageRepo.GetAsync(tenantId, periodStart, ct);
        var trackedClicks = usageRecord?.TrackedClicks ?? 0;

        var activeDomains = await _domains.GetCountByTenantAsync(tenantId);
        var activeLinks = await _links.GetCountByTenantAsync(tenantId);

        var plan = PlanCatalog.Get(tenant.Plan);
        var clicksOverage = CalculateOverage(trackedClicks, plan.MaxTrackedClicksPerMonth);
        var alertLevel = CalculateAlertLevel(trackedClicks, plan.MaxTrackedClicksPerMonth);

        await MaybeSendAlertAsync(tenant, periodStart, alertLevel, trackedClicks, plan.MaxTrackedClicksPerMonth, ct);

        return new TenantUsageSummary
        {
            TenantId = tenantId,
            Plan = tenant.Plan,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            TrackedClicks = trackedClicks,
            ActiveDomains = activeDomains,
            ActiveLinks = activeLinks,
            MaxTrackedClicksPerMonth = plan.MaxTrackedClicksPerMonth,
            MaxDomains = plan.MaxDomains,
            MaxLinksPerDomain = plan.MaxLinksPerDomain,
            ClicksOverage = clicksOverage,
            AlertLevel = alertLevel
        };
    }

    public async Task TrackClickAsync(Guid tenantId, DateTime timestamp, CancellationToken ct = default)
    {
        var (periodStart, periodEnd) = GetCurrentBillingPeriod(timestamp);
        var newTotal = await _usageRepo.IncrementClicksAsync(tenantId, periodStart, periodEnd, 1, ct);

        var tenant = await _tenants.GetByIdAsync(tenantId);
        if (tenant is null)
        {
            _logger.LogWarning("Click tracked for unknown tenant {TenantId}", tenantId);
            return;
        }

        var plan = PlanCatalog.Get(tenant.Plan);
        var alertLevel = CalculateAlertLevel(newTotal, plan.MaxTrackedClicksPerMonth);

        if (alertLevel != UsageAlertLevel.None)
        {
            await MaybeSendAlertAsync(tenant, periodStart, alertLevel, newTotal, plan.MaxTrackedClicksPerMonth, ct);
        }

        _logger.LogDebug(
            "Click tracked for tenant {TenantId}: period={PeriodStart:yyyy-MM}, total={Total}",
            tenantId, periodStart, newTotal);
    }

    private async Task MaybeSendAlertAsync(
        TenantEntity tenant,
        DateTime periodStart,
        UsageAlertLevel level,
        long currentClicks,
        int limitClicks,
        CancellationToken ct)
    {
        if (level == UsageAlertLevel.None) return;

        var alreadySent = await _alertService.HasBeenSentAsync(tenant.Id, periodStart, level, ct);
        if (alreadySent) return;

        await _alertService.MarkSentAsync(tenant.Id, periodStart, level, ct);
        await _notifications.NotifyUsageAlertAsync(tenant, level, currentClicks, limitClicks, ct);

        _logger.LogInformation(
            "Usage alert {Level} sent for tenant {TenantId}: {CurrentClicks}/{LimitClicks} clicks",
            level, tenant.Id, currentClicks, limitClicks);
    }

    /// <summary>
    /// Returns the UTC start and end of the calendar month containing <paramref name="utcNow"/>.
    /// In production this should be replaced with the Stripe subscription period anchored to the subscription creation date.
    /// </summary>
    public static (DateTime Start, DateTime End) GetCurrentBillingPeriod(DateTime utcNow)
    {
        var start = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1).AddTicks(-1);
        return (start, end);
    }

    internal static long CalculateOverage(long trackedClicks, int maxClicks)
    {
        if (maxClicks == int.MaxValue) return 0;
        return Math.Max(0, trackedClicks - maxClicks);
    }

    internal static UsageAlertLevel CalculateAlertLevel(long trackedClicks, int maxClicks)
    {
        if (maxClicks == int.MaxValue) return UsageAlertLevel.None;
        var pct = (double)trackedClicks / maxClicks;
        return pct >= 1.0 ? UsageAlertLevel.LimitReached
             : pct >= 0.9 ? UsageAlertLevel.Warning90
             : pct >= 0.8 ? UsageAlertLevel.Warning80
             : UsageAlertLevel.None;
    }
}
