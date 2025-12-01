using System.Collections.Concurrent;

namespace ControlPlane.Api.Services;

public sealed class InMemoryUsageAlertService : IUsageAlertService
{
    private readonly ConcurrentDictionary<(Guid TenantId, DateTime PeriodStart, UsageAlertLevel Level), bool> _sent = new();

    public Task<bool> HasBeenSentAsync(Guid tenantId, DateTime periodStart, UsageAlertLevel level, CancellationToken ct = default)
        => Task.FromResult(_sent.ContainsKey((tenantId, periodStart, level)));

    public Task MarkSentAsync(Guid tenantId, DateTime periodStart, UsageAlertLevel level, CancellationToken ct = default)
    {
        _sent[(tenantId, periodStart, level)] = true;
        return Task.CompletedTask;
    }
}
