using System.Collections.Concurrent;
using RedirectService.Api.Models;

namespace RedirectService.Api.Services;

/// <summary>
/// Thread-safe in-memory cache for domain-level branding configuration.
/// Entries expire after the configured TTL. Null values are cached to avoid
/// repeated DynamoDB calls for hostnames that have no stored configuration
/// (negative caching).
/// </summary>
public sealed class DomainConfigCache
{
    private readonly TimeSpan _ttl;
    private readonly int _maxEntries;
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

    public int Count => _entries.Count;

    public DomainConfigCache(TimeSpan? ttl = null, int maxEntries = 500)
    {
        _ttl = ttl ?? TimeSpan.FromMinutes(15);
        _maxEntries = maxEntries;
    }

    /// <summary>
    /// Tries to get a cached result for the given hostname.
    /// Returns <c>true</c> when a prior lookup was cached — <paramref name="config"/> may still
    /// be <c>null</c>, meaning no configuration exists for that hostname.
    /// Returns <c>false</c> when no cached result is available and a DynamoDB call is needed.
    /// </summary>
    public bool TryGet(string hostname, out DomainConfig? config)
    {
        if (!_entries.TryGetValue(hostname, out var entry))
        {
            config = null;
            return false;
        }

        if (DateTime.UtcNow - entry.CachedAt >= _ttl)
        {
            _entries.TryRemove(hostname, out _);
            config = null;
            return false;
        }

        config = entry.Config;
        return true;
    }

    /// <summary>
    /// Caches the result of a domain config lookup.
    /// Pass <c>null</c> to record that no configuration exists for the hostname so that
    /// further DynamoDB calls are avoided within the TTL (negative caching).
    /// </summary>
    public void Set(string hostname, DomainConfig? config)
    {
        if (_entries.Count >= _maxEntries && !_entries.ContainsKey(hostname))
            return;

        _entries[hostname] = new CacheEntry(config, DateTime.UtcNow);
        CleanupExpired();
    }

    private void CleanupExpired()
    {
        if (_entries.Count <= _maxEntries / 2)
            return;

        var cutoff = DateTime.UtcNow - _ttl;
        foreach (var (key, entry) in _entries)
        {
            if (entry.CachedAt < cutoff)
                _entries.TryRemove(key, out _);
        }
    }

    private sealed record CacheEntry(DomainConfig? Config, DateTime CachedAt);
}
