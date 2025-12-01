namespace ControlPlane.Api.Services;

/// <summary>Sends domain verification status change notifications to tenants. Implementations may use email, webhooks, or other channels.</summary>
public interface IDomainStatusNotificationService
{
    /// <summary>Notifies the tenant that their domain has been verified and is now active.</summary>
    Task NotifyVerifiedAsync(DomainEntity domain, CancellationToken ct = default);

    /// <summary>Notifies the tenant that their domain verification has timed out after the maximum polling period.</summary>
    Task NotifyVerificationTimedOutAsync(DomainEntity domain, CancellationToken ct = default);
}
