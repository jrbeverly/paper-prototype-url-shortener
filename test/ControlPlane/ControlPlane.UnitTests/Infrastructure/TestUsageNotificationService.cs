using System.Collections.Concurrent;
using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Infrastructure;

/// <summary>
/// Spy implementation of <see cref="IUsageNotificationService"/> that records all
/// notifications sent during a test run for assertion.
/// </summary>
public sealed class TestUsageNotificationService : IUsageNotificationService
{
    private readonly ConcurrentBag<SentAlert> _alerts = [];

    public IReadOnlyCollection<SentAlert> SentAlerts => _alerts;

    public Task NotifyUsageAlertAsync(
        TenantEntity tenant,
        UsageAlertLevel level,
        long currentClicks,
        int limitClicks,
        CancellationToken ct = default)
    {
        _alerts.Add(new SentAlert(tenant.Id, level, currentClicks, limitClicks));
        return Task.CompletedTask;
    }

    public sealed record SentAlert(Guid TenantId, UsageAlertLevel Level, long CurrentClicks, int LimitClicks);
}
