namespace ControlPlane.Api.Services;

public interface IUsageRepository
{
    /// <summary>Returns the usage record for the tenant's billing period, or null if no clicks have been recorded yet.</summary>
    Task<UsageRecord?> GetAsync(Guid tenantId, DateTime periodStart, CancellationToken ct = default);

    /// <summary>
    /// Atomically increments the click count for the tenant in the given billing period, creating the record if needed.
    /// Returns the new total click count.
    /// </summary>
    Task<long> IncrementClicksAsync(Guid tenantId, DateTime periodStart, DateTime periodEnd, long amount = 1, CancellationToken ct = default);
}

public sealed record UsageRecord
{
    public required Guid TenantId { get; init; }

    /// <summary>UTC start of the billing period (first moment of the month for calendar-month billing).</summary>
    public required DateTime PeriodStart { get; init; }

    /// <summary>UTC end of the billing period (last moment of the month).</summary>
    public required DateTime PeriodEnd { get; init; }

    /// <summary>Total tracked clicks within this billing period.</summary>
    public long TrackedClicks { get; init; }

    public DateTime? UpdatedAt { get; init; }
}
