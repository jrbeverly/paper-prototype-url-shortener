using System.Collections.Concurrent;

namespace ControlPlane.Api.Services;

public sealed class InMemoryUsageRepository : IUsageRepository
{
    private readonly ConcurrentDictionary<(Guid TenantId, DateTime PeriodStart), UsageRecord> _records = new();

    public Task<UsageRecord?> GetAsync(Guid tenantId, DateTime periodStart, CancellationToken ct = default)
    {
        _records.TryGetValue((tenantId, periodStart), out var record);
        return Task.FromResult(record);
    }

    public Task<long> IncrementClicksAsync(Guid tenantId, DateTime periodStart, DateTime periodEnd, long amount = 1, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var key = (tenantId, periodStart);

        var updated = _records.AddOrUpdate(
            key,
            _ => new UsageRecord
            {
                TenantId = tenantId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                TrackedClicks = amount,
                UpdatedAt = now
            },
            (_, existing) => existing with
            {
                TrackedClicks = existing.TrackedClicks + amount,
                UpdatedAt = now
            });

        return Task.FromResult(updated.TrackedClicks);
    }
}
