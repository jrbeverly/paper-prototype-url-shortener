namespace ControlPlane.Api.Services;

/// <summary>Stub notification service that logs intent without sending actual emails. Replace with a real implementation once an email provider is wired up.</summary>
public sealed class NoOpTrialNotificationService : ITrialNotificationService
{
    private readonly ILogger<NoOpTrialNotificationService> _logger;

    public NoOpTrialNotificationService(ILogger<NoOpTrialNotificationService> logger)
    {
        _logger = logger;
    }

    public Task NotifyExpiringAsync(TenantEntity tenant, int daysRemaining, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[TRIAL NOTIFICATION] Tenant {TenantId} ({Email}) trial expires in {DaysRemaining} day(s) on {TrialEndsAt:yyyy-MM-dd}",
            tenant.Id, tenant.Email, daysRemaining, tenant.TrialEndsAt);
        return Task.CompletedTask;
    }

    public Task NotifyExpiredAsync(TenantEntity tenant, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[TRIAL NOTIFICATION] Tenant {TenantId} ({Email}) trial expired on {TrialEndsAt:yyyy-MM-dd}, downgraded to Free",
            tenant.Id, tenant.Email, tenant.TrialEndsAt);
        return Task.CompletedTask;
    }
}
