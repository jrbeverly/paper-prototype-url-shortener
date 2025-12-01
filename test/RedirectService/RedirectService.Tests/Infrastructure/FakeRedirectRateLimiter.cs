namespace RedirectService.Tests.Infrastructure;

/// <summary>
/// Controllable test double for <see cref="IRedirectRateLimiter"/>.
/// Flags control whether each check is allowed or blocked.
/// </summary>
internal sealed class FakeRedirectRateLimiter : IRedirectRateLimiter
{
    public bool BlockIpCheck { get; set; }
    public bool BlockDomainCheck { get; set; }
    public HashSet<string> ExemptIpSet { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ExemptUserAgentPrefixSet { get; } = new(StringComparer.OrdinalIgnoreCase);

    private static RedirectRateLimitResult Allowed() =>
        RedirectRateLimitResult.Allow(60, 59, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60);

    private static RedirectRateLimitResult Blocked() =>
        RedirectRateLimitResult.Block(60, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 5, 5);

    public RedirectRateLimitResult CheckIp(string ip) => BlockIpCheck ? Blocked() : Allowed();
    public RedirectRateLimitResult CheckDomain(string hostname) => BlockDomainCheck ? Blocked() : Allowed();
    public bool IsExemptIp(string ip) => ExemptIpSet.Contains(ip);
    public bool IsExemptUserAgent(string userAgent) =>
        ExemptUserAgentPrefixSet.Any(p => userAgent.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    public void Dispose() { }
}
