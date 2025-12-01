namespace RedirectService.Api.Services;

/// <summary>Rate-limits redirect requests per domain and per IP using a token bucket algorithm.</summary>
public interface IRedirectRateLimiter : IDisposable
{
    /// <summary>Check and record one redirect attempt for the given domain hostname.</summary>
    RedirectRateLimitResult CheckDomain(string hostname);

    /// <summary>Check and record one redirect attempt for the given client IP address.</summary>
    RedirectRateLimitResult CheckIp(string ip);

    /// <summary>Returns true if the IP is on the exempt list and should bypass rate limiting.</summary>
    bool IsExemptIp(string ip);

    /// <summary>
    /// Returns true if the user-agent matches a known crawler or integration prefix
    /// and should bypass rate limiting.
    /// </summary>
    bool IsExemptUserAgent(string userAgent);
}
