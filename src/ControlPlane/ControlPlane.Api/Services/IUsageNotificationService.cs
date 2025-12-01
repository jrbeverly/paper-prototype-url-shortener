namespace ControlPlane.Api.Services;

/// <summary>Sends usage threshold alerts to tenants. Implementations may use email, Slack, or other channels.</summary>
public interface IUsageNotificationService
{
    /// <summary>
    /// Notifies <paramref name="tenant"/> that they have reached the given usage alert level.
    /// Called at most once per (tenant, billing period, level) — deduplication is handled by <see cref="IUsageAlertService"/>.
    /// </summary>
    Task NotifyUsageAlertAsync(
        TenantEntity tenant,
        UsageAlertLevel level,
        long currentClicks,
        int limitClicks,
        CancellationToken ct = default);
}
