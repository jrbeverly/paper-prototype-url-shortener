namespace ControlPlane.Api.Services;

/// <summary>Stub notification service that logs intent without sending actual messages. Replace with a real implementation once an email/webhook provider is wired up.</summary>
public sealed class NoOpDomainStatusNotificationService : IDomainStatusNotificationService
{
    private readonly ILogger<NoOpDomainStatusNotificationService> _logger;

    public NoOpDomainStatusNotificationService(ILogger<NoOpDomainStatusNotificationService> logger)
    {
        _logger = logger;
    }

    public Task NotifyVerifiedAsync(DomainEntity domain, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[DOMAIN NOTIFICATION] Domain {DomainId} ({Hostname}) for tenant {TenantId} has been verified and is now active",
            domain.Id, domain.Hostname, domain.TenantId);
        return Task.CompletedTask;
    }

    public Task NotifyVerificationTimedOutAsync(DomainEntity domain, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[DOMAIN NOTIFICATION] Domain {DomainId} ({Hostname}) for tenant {TenantId} verification timed out after {Hours}h — DNS records not found",
            domain.Id, domain.Hostname, domain.TenantId, DnsPollingOptions.MaxPollingHours);
        return Task.CompletedTask;
    }
}
