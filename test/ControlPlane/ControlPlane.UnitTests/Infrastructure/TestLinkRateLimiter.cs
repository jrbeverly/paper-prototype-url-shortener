using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Infrastructure;

/// <summary>
/// Controllable rate-limiter test double.
/// All checks pass by default; set <see cref="BlockTenantCheck"/> or <see cref="BlockIpCheck"/>
/// to simulate a throttled response. Thread-safe properties so tests can change state between calls.
/// </summary>
internal sealed class TestLinkRateLimiter : ILinkRateLimiter
{
    public bool BlockTenantCheck { get; set; }
    public bool BlockIpCheck { get; set; }

    public HashSet<string> ExemptIpsSet { get; } = [];

    public RateLimitResult CheckTenant(Guid tenantId, int perMinuteLimit)
    {
        var reset = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60 + 1) * 60;
        return BlockTenantCheck
            ? new RateLimitResult { IsAllowed = false, Limit = perMinuteLimit, Remaining = 0, ResetUnixSeconds = reset, RetryAfterSeconds = 60 }
            : new RateLimitResult { IsAllowed = true, Limit = perMinuteLimit, Remaining = perMinuteLimit - 1, ResetUnixSeconds = reset, RetryAfterSeconds = 0 };
    }

    public RateLimitResult CheckIp(string ip)
    {
        var reset = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60 + 1) * 60;
        return BlockIpCheck
            ? new RateLimitResult { IsAllowed = false, Limit = 60, Remaining = 0, ResetUnixSeconds = reset, RetryAfterSeconds = 60 }
            : new RateLimitResult { IsAllowed = true, Limit = 60, Remaining = 59, ResetUnixSeconds = reset, RetryAfterSeconds = 0 };
    }

    public bool IsExemptIp(string ip) => ExemptIpsSet.Contains(ip);
}
