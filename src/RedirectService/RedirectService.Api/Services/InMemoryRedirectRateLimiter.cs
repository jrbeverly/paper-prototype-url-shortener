using System.Collections.Concurrent;

namespace RedirectService.Api.Services;

/// <summary>
/// Token-bucket rate limiter for redirect requests, scoped per domain hostname and per IP.
///
/// Each key gets an independent bucket. Tokens are consumed one per request and refill
/// continuously at <c>limit / 60</c> tokens per second. The bucket starts full at
/// <c>limit × BurstFactor</c> tokens, so a cold bucket can absorb a burst before the
/// steady rate kicks in — this lets legitimate viral spikes through without triggering 429.
///
/// State is in-process; different Lambda instances do not share counters. This is intentional:
/// CloudFront WAF provides global enforcement; this layer adds per-instance defence-in-depth.
///
/// Thread-safe: each bucket has its own lock, so distinct hostnames and IPs never contend.
/// Inactive buckets are evicted every two minutes to bound memory use.
/// </summary>
public sealed class InMemoryRedirectRateLimiter : IRedirectRateLimiter
{
    private readonly RedirectRateLimitOptions _options;
    private readonly HashSet<string> _exemptIps;
    private readonly ConcurrentDictionary<string, TokenBucket> _domainBuckets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TokenBucket> _ipBuckets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _cleanupTimer;

    public InMemoryRedirectRateLimiter(RedirectRateLimitOptions options)
    {
        _options = options;
        _exemptIps = new HashSet<string>(options.ExemptIps, StringComparer.OrdinalIgnoreCase);
        _cleanupTimer = new Timer(Cleanup, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));
    }

    public RedirectRateLimitResult CheckDomain(string hostname)
        => Check(_domainBuckets, hostname, _options.DomainLimitPerMinute, _options.BurstFactor);

    public RedirectRateLimitResult CheckIp(string ip)
        => Check(_ipBuckets, ip, _options.IpLimitPerMinute, _options.BurstFactor);

    public bool IsExemptIp(string ip) => _exemptIps.Contains(ip);

    public bool IsExemptUserAgent(string userAgent) =>
        _options.ExemptUserAgentPrefixes.Any(prefix =>
            userAgent.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static RedirectRateLimitResult Check(
        ConcurrentDictionary<string, TokenBucket> buckets,
        string key,
        int limitPerMinute,
        double burstFactor)
    {
        var capacity = limitPerMinute * burstFactor;
        var refillPerSecond = limitPerMinute / 60.0;

        var bucket = buckets.GetOrAdd(key, _ => new TokenBucket(capacity));
        var (allowed, remaining, retryAfterSeconds) = bucket.TryConsume(refillPerSecond, capacity);

        // Estimate when the bucket will be completely full again.
        var secondsToFull = (int)Math.Ceiling((capacity - remaining) / refillPerSecond);
        var resetUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + secondsToFull;

        return allowed
            ? RedirectRateLimitResult.Allow(limitPerMinute, remaining, resetUnixSeconds)
            : RedirectRateLimitResult.Block(limitPerMinute, resetUnixSeconds, retryAfterSeconds);
    }

    private void Cleanup(object? state)
    {
        // Evict buckets that have not been accessed in the last 5 minutes.
        var staleThresholdMs = Environment.TickCount64 - (long)TimeSpan.FromMinutes(5).TotalMilliseconds;

        foreach (var kvp in _domainBuckets)
        {
            if (kvp.Value._lastUpdateMs < staleThresholdMs)
                _domainBuckets.TryRemove(kvp.Key, out _);
        }

        foreach (var kvp in _ipBuckets)
        {
            if (kvp.Value._lastUpdateMs < staleThresholdMs)
                _ipBuckets.TryRemove(kvp.Key, out _);
        }
    }

    public void Dispose() => _cleanupTimer.Dispose();

    // ── Token bucket ─────────────────────────────────────────────────────────

    internal sealed class TokenBucket
    {
        private double _tokens;
        // Written under _lock; read without lock only by the cleanup timer (acceptable: 64-bit reads
        // are atomic on ARM64/x64, and a slightly stale value only shifts eviction by one cycle).
        internal long _lastUpdateMs;
        private readonly object _lock = new();

        internal TokenBucket(double initialTokens)
        {
            _tokens = initialTokens;
            _lastUpdateMs = Environment.TickCount64;
        }

        /// <summary>
        /// Attempt to consume one token.
        /// Returns (allowed, remaining tokens, retryAfterSeconds).
        /// </summary>
        internal (bool Allowed, int Remaining, int RetryAfterSeconds) TryConsume(
            double refillPerSecond, double capacity)
        {
            lock (_lock)
            {
                var nowMs = Environment.TickCount64;
                var elapsedSeconds = (nowMs - _lastUpdateMs) / 1000.0;
                _tokens = Math.Min(capacity, _tokens + elapsedSeconds * refillPerSecond);
                _lastUpdateMs = nowMs;

                if (_tokens >= 1.0)
                {
                    _tokens -= 1.0;
                    return (true, (int)Math.Floor(_tokens), 0);
                }

                // Time until one token accumulates at the current refill rate.
                var retryAfterSeconds = (int)Math.Ceiling((1.0 - _tokens) / refillPerSecond);
                return (false, 0, Math.Max(1, retryAfterSeconds));
            }
        }
    }
}
