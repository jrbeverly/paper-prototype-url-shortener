using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Infrastructure;

public sealed class TestTrialNotificationService : ITrialNotificationService
{
    public sealed record ExpiringNotification(TenantEntity Tenant, int DaysRemaining);

    public List<ExpiringNotification> ExpiringNotifications { get; } = [];
    public List<TenantEntity> ExpiredNotifications { get; } = [];

    public Task NotifyExpiringAsync(TenantEntity tenant, int daysRemaining, CancellationToken ct = default)
    {
        ExpiringNotifications.Add(new ExpiringNotification(tenant, daysRemaining));
        return Task.CompletedTask;
    }

    public Task NotifyExpiredAsync(TenantEntity tenant, CancellationToken ct = default)
    {
        ExpiredNotifications.Add(tenant);
        return Task.CompletedTask;
    }

    public void Reset()
    {
        ExpiringNotifications.Clear();
        ExpiredNotifications.Clear();
    }
}
