namespace ControlPlane.Api.Services;

/// <summary>
/// Tracks which usage alerts have already been sent for a given tenant+period+level,
/// preventing duplicate notifications within the same billing period.
/// </summary>
public interface IUsageAlertService
{
    Task<bool> HasBeenSentAsync(Guid tenantId, DateTime periodStart, UsageAlertLevel level, CancellationToken ct = default);
    Task MarkSentAsync(Guid tenantId, DateTime periodStart, UsageAlertLevel level, CancellationToken ct = default);
}

public enum UsageAlertLevel
{
    None,
    Warning80,
    Warning90,
    LimitReached
}
