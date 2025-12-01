using RedirectService.Api.Repositories;

namespace RedirectService.Api.Services;

/// <summary>
/// Decorator for <see cref="IRedirectRepository"/> that adds resilience:
/// <list type="bullet">
///   <item>Circuit breaker: after N consecutive DynamoDB failures, fail fast for 30s.</item>
///   <item>Hot-link cache: successful lookups populate the cache; circuit-open lookups read from it.</item>
///   <item>Timeouts: each DynamoDB call is limited to the configured duration.</item>
/// </list>
/// </summary>
public sealed class ResilientRedirectRepository : IRedirectRepository
{
    private readonly IRedirectRepository _inner;
    private readonly CircuitBreaker _circuitBreaker;
    private readonly HotLinkCache _cache;
    private readonly TimeSpan _timeout;

    public CircuitBreaker CircuitBreaker => _circuitBreaker;
    public HotLinkCache Cache => _cache;

    public ResilientRedirectRepository(
        IRedirectRepository inner,
        CircuitBreaker circuitBreaker,
        HotLinkCache cache,
        TimeSpan? timeout = null)
    {
        _inner = inner;
        _circuitBreaker = circuitBreaker;
        _cache = cache;
        _timeout = timeout ?? TimeSpan.FromMilliseconds(500);
    }

    public Task<RedirectRecord?> GetAsync(string hostname, string slug, CancellationToken ct = default)
    {
        var cacheKey = HotLinkCache.BuildKey(hostname, slug);

        return _circuitBreaker.ExecuteAsync(
            action: async () =>
            {
                using var cts = new CancellationTokenSource(_timeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
                var record = await _inner.GetAsync(hostname, slug, linked.Token);

                if (record is not null)
                    _cache.Set(cacheKey, record);

                return record;
            },
            fallback: () => Task.FromResult(_cache.Get(cacheKey)));
    }

    public async Task<bool> TryIncrementClickAsync(string hostname, string slug, CancellationToken ct = default)
    {
        return await _circuitBreaker.ExecuteAsync(
            action: async () =>
            {
                using var cts = new CancellationTokenSource(_timeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
                return await _inner.TryIncrementClickAsync(hostname, slug, linked.Token);
            },
            fallback: () => Task.FromResult(false));
    }
}
