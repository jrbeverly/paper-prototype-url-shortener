namespace ControlPlane.Api.Services;

/// <summary>Sends plan change confirmation notifications to tenants.</summary>
public interface IPlanChangeNotificationService
{
    /// <summary>Notifies <paramref name="tenant"/> that their plan was upgraded immediately.</summary>
    Task NotifyUpgradedAsync(TenantEntity tenant, string previousPlan, CancellationToken ct = default);

    /// <summary>Notifies <paramref name="tenant"/> that their plan downgrade is scheduled for period end.</summary>
    Task NotifyDowngradeScheduledAsync(TenantEntity tenant, string targetPlan, DateTime scheduledAt, CancellationToken ct = default);

    /// <summary>Notifies <paramref name="tenant"/> that their subscription has been cancelled and will end at period end.</summary>
    Task NotifyCancelledAsync(TenantEntity tenant, DateTime periodEnd, CancellationToken ct = default);
}
