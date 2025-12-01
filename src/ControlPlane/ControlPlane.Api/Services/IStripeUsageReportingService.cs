namespace ControlPlane.Api.Services;

/// <summary>
/// Reports per-tenant click usage to Stripe for metered billing.
/// Only applicable for paid plans that have a Stripe subscription with a metered price component.
/// </summary>
public interface IStripeUsageReportingService
{
    /// <summary>
    /// Reports the total click count for a billing period to Stripe.
    /// Called at period end (billing cycle boundary).
    /// No-op for free plans or tenants without a Stripe subscription.
    /// </summary>
    Task ReportClicksAsync(TenantEntity tenant, long totalClicks, DateTime periodEnd, CancellationToken ct = default);
}
