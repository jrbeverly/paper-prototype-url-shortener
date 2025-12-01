using System.Collections.Concurrent;
using RedirectService.Api.Repositories;

namespace RedirectService.Api.Services;

/// <summary>
/// Thread-safe in-memory cache for recently resolved <see cref="RedirectRecord"/> values.
/// Used as a fallback when the primary DynamoDB repository is unavailable.
/// Entries expire after <see cref="_ttl"/> and the cache is bounded to <see cref="_maxEntries"/>.
/// </summary>
public sealed class HotLinkCache
{
    private readonly TimeSpan _ttl;
    private readonly int _maxEntries;
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

    public int Count => _entries.Count;

    public HotLinkCache(TimeSpan? ttl = null, int maxEntries = 1000)
    {
        _ttl = ttl ?? TimeSpan.FromMinutes(5);
        _maxEntries = maxEntries;
    }

    /// <summary>Builds the compound cache key from hostname and slug.</summary>
    public static string BuildKey(string hostname, string slug)
        => $"{hostname}#{slug}";

    /// <summary>Stores a record in the cache. Does nothing if the cache is at capacity.</summary>
    public void Set(string key, RedirectRecord record)
    {
        if (_entries.Count >= _maxEntries && !_entries.ContainsKey(key))
            return;

        _entries[key] = new CacheEntry(record, DateTime.UtcNow);
        CleanupExpired();
    }

    /// <summary>Retrieves a record from the cache. Returns null if absent or expired.</summary>
    public RedirectRecord? Get(string key)
    {
        if (!_entries.TryGetValue(key, out var entry))
            return null;

        if (DateTime.UtcNow - entry.CachedAt >= _ttl)
        {
            _entries.TryRemove(key, out _);
            return null;
        }

        return entry.Record;
    }

    private void CleanupExpired()
    {
        // Best-effort cleanup: remove entries that have exceeded 2x TTL.
        // Called during Set operations to keep the cache from growing unbounded.
        if (_entries.Count <= _maxEntries / 2)
            return;

        var cutoff = DateTime.UtcNow - _ttl;
        var toRemove = new List<string>();
        foreach (var (key, entry) in _entries)
        {
            if (entry.CachedAt < cutoff)
                toRemove.Add(key);
        }

        foreach (var key in toRemove)
            _entries.TryRemove(key, out _);
    }

    private sealed record CacheEntry(RedirectRecord Record, DateTime CachedAt);
}
