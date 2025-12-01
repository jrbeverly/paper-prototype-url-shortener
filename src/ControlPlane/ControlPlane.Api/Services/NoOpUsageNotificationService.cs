namespace ControlPlane.Api.Services;

public sealed class NoOpUsageNotificationService(ILogger<NoOpUsageNotificationService> logger) : IUsageNotificationService
{
    public Task NotifyUsageAlertAsync(
        TenantEntity tenant,
        UsageAlertLevel level,
        long currentClicks,
        int limitClicks,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "Usage alert {Level} for tenant {TenantId}: {CurrentClicks}/{LimitClicks} clicks (notification not configured)",
            level, tenant.Id, currentClicks, limitClicks);
        return Task.CompletedTask;
    }
}
