namespace RedirectService.Api.Services;

/// <summary>
/// Configuration for redirect rate limiting. Populated from environment variables so the
/// Lambda function can be tuned per environment without a code deployment.
/// </summary>
public sealed class RedirectRateLimitOptions
{
    /// <summary>Maximum redirects per minute per domain hostname. Default: 3000.</summary>
    public int DomainLimitPerMinute { get; init; } = 3000;

    /// <summary>Maximum redirects per minute per client IP. Default: 60.</summary>
    public int IpLimitPerMinute { get; init; } = 60;

    /// <summary>
    /// Burst multiplier applied to the token bucket capacity.
    /// A value of 2.0 means a cold bucket starts with 2× the per-minute limit in tokens,
    /// allowing legitimate viral spikes before the steady refill rate takes over.
    /// Default: 2.0.
    /// </summary>
    public double BurstFactor { get; init; } = 2.0;

    /// <summary>IP addresses that bypass all rate limiting (e.g., internal monitoring).</summary>
    public IReadOnlyList<string> ExemptIps { get; init; } = [];

    /// <summary>
    /// User-agent prefixes that bypass rate limiting (search crawlers, social previews).
    /// Matched case-insensitively against the start of the User-Agent header value.
    /// </summary>
    public IReadOnlyList<string> ExemptUserAgentPrefixes { get; init; } =
    [
        "Googlebot", "Bingbot", "Slurp", "DuckDuckBot",
        "Baiduspider", "YandexBot", "facebookexternalhit",
        "Twitterbot", "LinkedInBot"
    ];

    /// <summary>
    /// Reads rate limit configuration from environment variables, falling back to defaults.
    /// <list type="bullet">
    ///   <item><c>RATE_LIMIT_DOMAIN_PER_MINUTE</c> — domain limit (default 3000)</item>
    ///   <item><c>RATE_LIMIT_IP_PER_MINUTE</c> — IP limit (default 60)</item>
    ///   <item><c>RATE_LIMIT_BURST_FACTOR</c> — burst multiplier (default 2.0)</item>
    ///   <item><c>RATE_LIMIT_EXEMPT_IPS</c> — comma-separated IP list (default empty)</item>
    /// </list>
    /// </summary>
    public static RedirectRateLimitOptions FromEnvironment() => new()
    {
        DomainLimitPerMinute = ReadInt("RATE_LIMIT_DOMAIN_PER_MINUTE", 3000),
        IpLimitPerMinute = ReadInt("RATE_LIMIT_IP_PER_MINUTE", 60),
        BurstFactor = ReadDouble("RATE_LIMIT_BURST_FACTOR", 2.0),
        ExemptIps = ReadList("RATE_LIMIT_EXEMPT_IPS")
    };

    private static int ReadInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : fallback;

    private static double ReadDouble(string name, double fallback) =>
        double.TryParse(Environment.GetEnvironmentVariable(name),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static IReadOnlyList<string> ReadList(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v
            ? [.. v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : [];
}
