namespace ControlPlane.Api.Services;

public sealed class NoOpPlanChangeNotificationService : IPlanChangeNotificationService
{
    private readonly ILogger<NoOpPlanChangeNotificationService> _logger;

    public NoOpPlanChangeNotificationService(ILogger<NoOpPlanChangeNotificationService> logger)
    {
        _logger = logger;
    }

    public Task NotifyUpgradedAsync(TenantEntity tenant, string previousPlan, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[PLAN NOTIFICATION] Tenant {TenantId} ({Email}) upgraded from {PreviousPlan} to {NewPlan}",
            tenant.Id, tenant.Email, previousPlan, tenant.Plan);
        return Task.CompletedTask;
    }

    public Task NotifyDowngradeScheduledAsync(TenantEntity tenant, string targetPlan, DateTime scheduledAt, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[PLAN NOTIFICATION] Tenant {TenantId} ({Email}) downgrade scheduled to {TargetPlan} at {ScheduledAt:O}",
            tenant.Id, tenant.Email, targetPlan, scheduledAt);
        return Task.CompletedTask;
    }

    public Task NotifyCancelledAsync(TenantEntity tenant, DateTime periodEnd, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[PLAN NOTIFICATION] Tenant {TenantId} ({Email}) subscription cancelled, access until {PeriodEnd:O}",
            tenant.Id, tenant.Email, periodEnd);
        return Task.CompletedTask;
    }
}
