namespace ControlPlane.Api.Services;

/// <summary>Result of a URL safety scan.</summary>
public enum UrlSafetyVerdict
{
    /// <summary>URL is safe — no action needed.</summary>
    Safe,

    /// <summary>URL is suspicious but not confirmed malicious — allow creation with quarantine status.</summary>
    Suspicious,

    /// <summary>URL is confirmed malicious — block creation entirely.</summary>
    Malicious
}

/// <summary>Detailed result from a URL safety scan, including verdict, reason, and metadata.</summary>
public sealed record UrlSafetyResult
{
    public required UrlSafetyVerdict Verdict { get; init; }

    /// <summary>Human-readable reason for the verdict (e.g. "matched blocklist: phishing").</summary>
    public required string Reason { get; init; }

    /// <summary>Source of the verdict (e.g. "blocklist", "suspicious_patterns", "threat_intelligence").</summary>
    public required string Source { get; init; }

    /// <summary>Whether the result came from cache rather than a fresh scan.</summary>
    public bool FromCache { get; init; }
}

public interface IUrlSafetyService
{
    /// <summary>
    /// Scans a destination URL for safety. Returns a verdict indicating whether the URL
    /// is safe, suspicious, or malicious. Results are cached to avoid redundant scans.
    /// </summary>
    Task<UrlSafetyResult> ScanAsync(string url, CancellationToken ct = default);
}
