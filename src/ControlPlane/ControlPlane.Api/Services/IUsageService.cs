namespace ControlPlane.Api.Services;

public interface IUsageService
{
    /// <summary>
    /// Returns the current billing period usage for a tenant, including click counts, domain/link counts,
    /// plan limits, overage, and the current alert level.
    /// Throws <see cref="Common.ErrorHandling.NotFoundException"/> if the tenant does not exist.
    /// </summary>
    Task<TenantUsageSummary> GetUsageAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Records a single click for the tenant in the billing period that contains <paramref name="timestamp"/>.
    /// Triggers usage alerts if a threshold (80%, 90%, 100%) is crossed for the first time this period.
    /// </summary>
    Task TrackClickAsync(Guid tenantId, DateTime timestamp, CancellationToken ct = default);
}

public sealed record TenantUsageSummary
{
    public required Guid TenantId { get; init; }
    public required string Plan { get; init; }
    public required DateTime PeriodStart { get; init; }
    public required DateTime PeriodEnd { get; init; }
    public required long TrackedClicks { get; init; }
    public required int ActiveDomains { get; init; }
    public required int ActiveLinks { get; init; }
    public required int MaxTrackedClicksPerMonth { get; init; }
    public required int MaxDomains { get; init; }
    public required int MaxLinksPerDomain { get; init; }
    public required long ClicksOverage { get; init; }
    public required UsageAlertLevel AlertLevel { get; init; }
}
