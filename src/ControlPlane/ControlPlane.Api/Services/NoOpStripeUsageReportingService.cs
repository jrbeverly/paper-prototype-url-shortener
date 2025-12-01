namespace ControlPlane.Api.Services;

public sealed class NoOpStripeUsageReportingService(ILogger<NoOpStripeUsageReportingService> logger) : IStripeUsageReportingService
{
    public Task ReportClicksAsync(TenantEntity tenant, long totalClicks, DateTime periodEnd, CancellationToken ct = default)
    {
        if (tenant.StripeSubscriptionId is not null)
        {
            logger.LogInformation(
                "Stripe usage reporting not yet implemented for tenant {TenantId} (subscription {SubscriptionId}): {Clicks} clicks at period end {PeriodEnd:O}",
                tenant.Id, tenant.StripeSubscriptionId, totalClicks, periodEnd);
        }

        return Task.CompletedTask;
    }
}
