namespace ControlPlane.Api.Services;

/// <summary>
/// Configuration for <see cref="LinkReScanService"/>. Controls scan frequency,
/// priority thresholds for high-traffic links, and batch size per cycle.
/// </summary>
public sealed class LinkReScanOptions
{
    public const string SectionName = "LinkReScan";

    /// <summary>How often the full re-scan cycle runs, in hours. Default: 168 (7 days).</summary>
    public int ReScanIntervalHours { get; init; } = 168;

    /// <summary>Click count above which a link is considered high-traffic and scanned every cycle.</summary>
    public int HighTrafficThreshold { get; init; } = 1000;

    /// <summary>Maximum number of links scanned in a single cycle. Links beyond the batch are deferred.</summary>
    public int ScanBatchSize { get; init; } = 50;
}
