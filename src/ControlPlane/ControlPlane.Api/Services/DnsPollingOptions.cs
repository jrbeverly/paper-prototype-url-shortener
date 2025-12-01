namespace ControlPlane.Api.Services;

/// <summary>
/// Configuration for <see cref="DnsPollingService"/>. Controls the polling tick interval,
/// per-domain backoff tiers, and the maximum polling window before a domain times out.
/// </summary>
public sealed class DnsPollingOptions
{
    public const string SectionName = "DnsPolling";

    /// <summary>Maximum hours a domain will be polled before being marked verification_timeout. Default: 48.</summary>
    public const int MaxPollingHours = 48;

    /// <summary>How often the background service wakes up to check all eligible domains, in minutes. Default: 5.</summary>
    public int TickIntervalMinutes { get; init; } = 5;

    // ── Per-domain backoff tiers ──────────────────────────────────────────────
    // Each tier defines a window (minutes since creation) and the minimum gap
    // required since the last check before the domain is polled again.

    /// <summary>Minutes since creation below which the per-domain re-check interval is <see cref="EarlyPollIntervalMinutes"/>. Default: 30.</summary>
    public int EarlyWindowMinutes { get; init; } = 30;

    /// <summary>Per-domain re-check interval (minutes) during the early window. Default: 5.</summary>
    public int EarlyPollIntervalMinutes { get; init; } = 5;

    /// <summary>Minutes since creation below which the per-domain re-check interval is <see cref="MidPollIntervalMinutes"/>. Default: 120 (2h).</summary>
    public int MidWindowMinutes { get; init; } = 120;

    /// <summary>Per-domain re-check interval (minutes) during the mid window. Default: 15.</summary>
    public int MidPollIntervalMinutes { get; init; } = 15;

    /// <summary>Minutes since creation below which the per-domain re-check interval is <see cref="LatePollIntervalMinutes"/>. Default: 720 (12h).</summary>
    public int LateWindowMinutes { get; init; } = 720;

    /// <summary>Per-domain re-check interval (minutes) during the late window. Default: 30.</summary>
    public int LatePollIntervalMinutes { get; init; } = 30;

    /// <summary>Per-domain re-check interval (minutes) beyond the late window. Default: 60.</summary>
    public int FinalPollIntervalMinutes { get; init; } = 60;
}
