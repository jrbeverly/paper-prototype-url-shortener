namespace ControlPlane.Api.Services;

public interface IBillingService
{
    /// <summary>
    /// Upgrades the tenant's plan immediately with proration via Stripe.
    /// Requires the tenant to have an active Stripe subscription.
    /// Returns the updated tenant entity with new plan and limits.
    /// </summary>
    Task<PlanChangeResult> UpgradePlanAsync(
        TenantEntity tenant,
        string targetPlanId,
        CancellationToken ct = default);

    /// <summary>
    /// Schedules a downgrade to take effect at the end of the current billing period.
    /// Updates Stripe subscription to change the price at period end (no proration).
    /// Enforces new plan limits immediately and returns usage warnings if current usage exceeds new limits.
    /// </summary>
    Task<PlanChangeResult> DowngradePlanAsync(
        TenantEntity tenant,
        string targetPlanId,
        CancellationToken ct = default);

    /// <summary>
    /// Cancels the tenant's Stripe subscription at the end of the current billing period.
    /// The tenant retains full access until the period ends, then reverts to Free.
    /// No data is lost during cancellation.
    /// </summary>
    Task<CancelSubscriptionResult> CancelSubscriptionAsync(
        TenantEntity tenant,
        CancellationToken ct = default);
}

public sealed record PlanChangeResult
{
    public required TenantEntity Tenant { get; init; }
    public required bool IsImmediate { get; init; }
    public required IReadOnlyList<UsageWarning> Warnings { get; init; }
}

public sealed record CancelSubscriptionResult
{
    public required TenantEntity Tenant { get; init; }
    public required DateTime PeriodEnd { get; init; }
}

public sealed record UsageWarning
{
    public required string Code { get; init; }
    public required string Message { get; init; }
}
