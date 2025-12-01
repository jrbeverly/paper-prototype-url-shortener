using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Services;

/// <summary>
/// Fixed-window in-memory rate limiter for link creation.
/// Windows are aligned to UTC clock minutes. Thread-safe via CAS loop on ConcurrentDictionary.
/// Old window entries are evicted by a background timer every two minutes.
/// </summary>
public sealed class InMemoryLinkRateLimiter : ILinkRateLimiter, IDisposable
{
    private readonly RateLimitOptions _options;
    private readonly HashSet<string> _exemptIps;
    private readonly ConcurrentDictionary<string, int> _counters = new(StringComparer.Ordinal);
    private readonly Timer _cleanupTimer;

    public InMemoryLinkRateLimiter(IOptions<RateLimitOptions> options)
    {
        _options = options.Value;
        _exemptIps = new HashSet<string>(_options.ExemptIps, StringComparer.OrdinalIgnoreCase);
        _cleanupTimer = new Timer(Cleanup, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));
    }

    public RateLimitResult CheckTenant(Guid tenantId, int perMinuteLimit)
        => Check($"rl:tenant:{tenantId}", perMinuteLimit);

    public RateLimitResult CheckIp(string ip)
        => Check($"rl:ip:{ip}", _options.IpLimitPerMinute);

    public bool IsExemptIp(string ip) => _exemptIps.Contains(ip);

    internal RateLimitResult Check(string keyPrefix, int limit)
    {
        // Unlimited — skip counting to avoid long-lived counter entries.
        if (limit >= int.MaxValue / 2)
        {
            var unlimitedReset = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60 + 1) * 60;
            return new RateLimitResult
            {
                IsAllowed = true,
                Limit = limit,
                Remaining = limit,
                ResetUnixSeconds = unlimitedReset,
                RetryAfterSeconds = 0
            };
        }

        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var currentMinute = nowSeconds / 60;
        var windowKey = $"{keyPrefix}:{currentMinute}";
        var resetUnixSeconds = (currentMinute + 1) * 60;
        var retryAfterSeconds = (int)(resetUnixSeconds - nowSeconds);

        // CAS loop: increment only if under limit.
        while (true)
        {
            if (_counters.TryGetValue(windowKey, out var current))
            {
                if (current >= limit)
                    return new RateLimitResult
                    {
                        IsAllowed = false,
                        Limit = limit,
                        Remaining = 0,
                        ResetUnixSeconds = resetUnixSeconds,
                        RetryAfterSeconds = retryAfterSeconds
                    };

                if (_counters.TryUpdate(windowKey, current + 1, current))
                    return new RateLimitResult
                    {
                        IsAllowed = true,
                        Limit = limit,
                        Remaining = Math.Max(0, limit - current - 1),
                        ResetUnixSeconds = resetUnixSeconds,
                        RetryAfterSeconds = 0
                    };
                // Another thread updated concurrently — retry.
            }
            else
            {
                if (_counters.TryAdd(windowKey, 1))
                    return new RateLimitResult
                    {
                        IsAllowed = true,
                        Limit = limit,
                        Remaining = Math.Max(0, limit - 1),
                        ResetUnixSeconds = resetUnixSeconds,
                        RetryAfterSeconds = 0
                    };
                // Another thread added first — retry.
            }
        }
    }

    private void Cleanup(object? state)
    {
        var currentMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        foreach (var key in _counters.Keys)
        {
            var lastColon = key.LastIndexOf(':');
            if (lastColon >= 0
                && long.TryParse(key.AsSpan(lastColon + 1), out var minute)
                && minute < currentMinute - 1)
            {
                _counters.TryRemove(key, out _);
            }
        }
    }

    public void Dispose() => _cleanupTimer.Dispose();
}
