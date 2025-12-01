namespace ControlPlane.Api.Services;

/// <summary>Sends trial lifecycle notifications to tenants. Implementations may use email, Slack, or other channels.</summary>
public interface ITrialNotificationService
{
    /// <summary>Notifies <paramref name="tenant"/> that their trial expires in <paramref name="daysRemaining"/> days.</summary>
    Task NotifyExpiringAsync(TenantEntity tenant, int daysRemaining, CancellationToken ct = default);

    /// <summary>Notifies <paramref name="tenant"/> that their trial has expired and they have been downgraded to Free.</summary>
    Task NotifyExpiredAsync(TenantEntity tenant, CancellationToken ct = default);
}
