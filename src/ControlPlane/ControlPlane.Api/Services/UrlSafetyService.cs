using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Services;

/// <summary>
/// Configuration for <see cref="InMemoryUrlSafetyService"/>.
/// Blocklist and suspicious patterns can be loaded from appsettings.json or environment variables.
/// </summary>
public sealed class UrlSafetyOptions
{
    public const string SectionName = "UrlSafety";

    /// <summary>Exact hostnames or URLs that will be blocked (malicious verdict).</summary>
    public IReadOnlyList<string> BlockedHosts { get; init; } = [];

    /// <summary>Hostname patterns that will be flagged as suspicious (case-insensitive contains match).</summary>
    public IReadOnlyList<string> SuspiciousPatterns { get; init; } = [];

    /// <summary>How long a scan result is cached before being re-evaluated.</summary>
    public int CacheTtlSeconds { get; init; } = 300;

    /// <summary>Maximum time a scan can take before it returns a timeout result.</summary>
    public int ScanTimeoutMs { get; init; } = 3000;
}

/// <summary>
/// In-memory URL safety service that checks destination URLs against configurable
/// blocklists and suspicious patterns. Results are cached to avoid redundant checks.
///
/// When integrated with an external threat-intelligence API, this service can be
/// swapped out by implementing <see cref="IUrlSafetyService"/> with the API client.
/// </summary>
public sealed class InMemoryUrlSafetyService : IUrlSafetyService
{
    private readonly UrlSafetyOptions _options;
    private readonly ConcurrentDictionary<string, CachedResult> _cache = new(StringComparer.Ordinal);
    private readonly ILogger<InMemoryUrlSafetyService> _logger;
    private readonly TimeSpan _cacheTtl;
    private readonly TimeSpan _scanTimeout;

    public InMemoryUrlSafetyService(
        IOptions<UrlSafetyOptions> options,
        ILogger<InMemoryUrlSafetyService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _cacheTtl = TimeSpan.FromSeconds(_options.CacheTtlSeconds);
        _scanTimeout = TimeSpan.FromMilliseconds(_options.ScanTimeoutMs);
    }

    public async Task<UrlSafetyResult> ScanAsync(string url, CancellationToken ct = default)
    {
        var normalized = NormalizeUrl(url);

        if (_cache.TryGetValue(normalized, out var cached) && !cached.IsExpired)
        {
            _logger.LogDebug("URL safety cache hit: {Url} → {Verdict}", normalized, cached.Result.Verdict);
            return cached.Result with { FromCache = true };
        }

        UrlSafetyResult result;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_scanTimeout);

            result = await Task.Run(() => EvaluateUrl(normalized), timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("URL safety scan timed out after {TimeoutMs}ms for {Url}", _scanTimeout.TotalMilliseconds, normalized);
            result = new UrlSafetyResult
            {
                Verdict = UrlSafetyVerdict.Safe,
                Reason = "Scan timed out — allowing creation with warning",
                Source = "timeout"
            };
        }

        _cache[normalized] = new CachedResult(result, DateTime.UtcNow.Add(_cacheTtl));
        _logger.LogInformation("URL safety scan: {Url} → {Verdict} ({Reason})", normalized, result.Verdict, result.Reason);

        return result;
    }

    private UrlSafetyResult EvaluateUrl(string normalizedUrl)
    {
        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
        {
            return new UrlSafetyResult
            {
                Verdict = UrlSafetyVerdict.Suspicious,
                Reason = "URL could not be parsed as a valid absolute URI",
                Source = "validation"
            };
        }

        var host = uri.Host.ToLowerInvariant();

        // Check blocked hosts first (exact match or suffix match for subdomains)
        foreach (var blocked in _options.BlockedHosts)
        {
            if (string.IsNullOrEmpty(blocked)) continue;
            var blockedLower = blocked.ToLowerInvariant();

            if (host == blockedLower || host.EndsWith("." + blockedLower, StringComparison.Ordinal))
            {
                return new UrlSafetyResult
                {
                    Verdict = UrlSafetyVerdict.Malicious,
                    Reason = $"Host matches blocklist: {blocked}",
                    Source = "blocklist"
                };
            }
        }

        // Check suspicious patterns (contains match)
        foreach (var pattern in _options.SuspiciousPatterns)
        {
            if (string.IsNullOrEmpty(pattern)) continue;
            if (host.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return new UrlSafetyResult
                {
                    Verdict = UrlSafetyVerdict.Suspicious,
                    Reason = $"Host matches suspicious pattern: {pattern}",
                    Source = "suspicious_patterns"
                };
            }
        }

        return new UrlSafetyResult
        {
            Verdict = UrlSafetyVerdict.Safe,
            Reason = "No threats detected",
            Source = "local"
        };
    }

    /// <summary>Normalizes a URL by trimming whitespace and ensuring a scheme is present.</summary>
    private static string NormalizeUrl(string url)
    {
        var trimmed = url.Trim();
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://" + trimmed;
        }
        return trimmed.ToLowerInvariant();
    }

    private sealed record CachedResult(UrlSafetyResult Result, DateTime ExpiresAt)
    {
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    }
}
