using ControlPlane.Api.Services;

namespace ControlPlane.Tests.Infrastructure;

public sealed class TestPlanChangeNotificationService : IPlanChangeNotificationService
{
    public List<(string Type, TenantEntity Tenant, string? Detail)> Notifications { get; } = [];

    public Task NotifyUpgradedAsync(TenantEntity tenant, string previousPlan, CancellationToken ct = default)
    {
        Notifications.Add(("upgraded", tenant, previousPlan));
        return Task.CompletedTask;
    }

    public Task NotifyDowngradeScheduledAsync(TenantEntity tenant, string targetPlan, DateTime scheduledAt, CancellationToken ct = default)
    {
        Notifications.Add(("downgrade_scheduled", tenant, targetPlan));
        return Task.CompletedTask;
    }

    public Task NotifyCancelledAsync(TenantEntity tenant, DateTime periodEnd, CancellationToken ct = default)
    {
        Notifications.Add(("cancelled", tenant, null));
        return Task.CompletedTask;
    }
}
