namespace ControlPlane.Api.Services;

/// <summary>Rate-limits link creation per tenant (plan-based) and per IP (hard cap).</summary>
public interface ILinkRateLimiter
{
    /// <summary>Check and record one link-creation attempt for the given tenant.</summary>
    RateLimitResult CheckTenant(Guid tenantId, int perMinuteLimit);

    /// <summary>Check and record one link-creation attempt for the given IP address.</summary>
    RateLimitResult CheckIp(string ip);

    /// <summary>Returns true if the IP is on the exempt list and should bypass rate limiting.</summary>
    bool IsExemptIp(string ip);
}

/// <summary>Result of a single rate-limit check.</summary>
public sealed record RateLimitResult
{
    public required bool IsAllowed { get; init; }

    /// <summary>The limit that was applied (requests per minute).</summary>
    public required int Limit { get; init; }

    /// <summary>Remaining requests in the current window after this one.</summary>
    public required int Remaining { get; init; }

    /// <summary>Unix timestamp (seconds) when the current window resets.</summary>
    public required long ResetUnixSeconds { get; init; }

    /// <summary>Seconds until the window resets. 0 when IsAllowed is true.</summary>
    public required int RetryAfterSeconds { get; init; }
}
