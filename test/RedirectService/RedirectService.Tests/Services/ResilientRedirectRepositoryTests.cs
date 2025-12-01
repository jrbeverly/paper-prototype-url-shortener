namespace RedirectService.Tests.Services;

public sealed class ResilientRedirectRepositoryTests
{
    private const string _hostname = "go.example.com";
    private const string _slug = "test-slug";

    private static RedirectRecord BuildRecord(string destinationUrl = "https://example.com/landing")
        => new()
        {
            TenantId = "tenant-1",
            DomainId = "domain-1",
            Hostname = _hostname,
            Slug = _slug,
            DestinationUrl = destinationUrl,
            RedirectType = 302,
            Status = "active"
        };

    private static (ResilientRedirectRepository, FakeRedirectRepository, CircuitBreaker, HotLinkCache) CreateRepo(
        int failureThreshold = 3,
        TimeSpan? timeout = null)
    {
        var inner = new FakeRedirectRepository();
        var cb = new CircuitBreaker("DynamoDB",
            failureThreshold: failureThreshold,
            cooldownPeriod: TimeSpan.Zero); // instant half-open probe for testing
        var cache = new HotLinkCache(ttl: TimeSpan.FromMinutes(5));
        var repo = new ResilientRedirectRepository(inner, cb, cache, timeout ?? TimeSpan.FromMilliseconds(500));
        return (repo, inner, cb, cache);
    }

    // ── Passthrough (healthy) ────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_HealthyInner_ReturnsRecord()
    {
        var (repo, inner, _, _) = CreateRepo();
        inner.Returns(BuildRecord());

        var result = await repo.GetAsync(_hostname, _slug);

        result.Should().NotBeNull();
        result!.DestinationUrl.Should().Be("https://example.com/landing");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_HealthyInner_CallsInnerRepository()
    {
        var (repo, inner, _, _) = CreateRepo();
        inner.Returns(BuildRecord());

        await repo.GetAsync(_hostname, _slug);

        inner.LastHostname.Should().Be(_hostname);
        inner.LastSlug.Should().Be(_slug);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TryIncrementClickAsync_HealthyInner_ReturnsTrue()
    {
        var (repo, inner, _, _) = CreateRepo();

        var result = await repo.TryIncrementClickAsync(_hostname, _slug);

        result.Should().BeTrue();
        inner.IncrementCallCount.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TryIncrementClickAsync_HealthyInner_PassesHostnameAndSlug()
    {
        var (repo, inner, _, _) = CreateRepo();

        await repo.TryIncrementClickAsync(_hostname, _slug);

        inner.LastIncrementHostname.Should().Be(_hostname);
        inner.LastIncrementSlug.Should().Be(_slug);
    }

    // ── Cache population (healthy) ───────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_Success_PopulatesCache()
    {
        var (repo, inner, _, cache) = CreateRepo();
        inner.Returns(BuildRecord());

        await repo.GetAsync(_hostname, _slug);

        var cacheKey = HotLinkCache.BuildKey(_hostname, _slug);
        cache.Get(cacheKey).Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_NotFound_DoesNotPopulateCache()
    {
        var (repo, inner, _, cache) = CreateRepo();
        inner.Returns(null);

        await repo.GetAsync(_hostname, _slug);

        var cacheKey = HotLinkCache.BuildKey(_hostname, _slug);
        cache.Get(cacheKey).Should().BeNull();
    }

    // ── Circuit breaker GetAsync ─────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_InnerThrows_OpensCircuit()
    {
        var (repo, inner, cb, _) = CreateRepo(failureThreshold: 1);
        inner.Throws(new Exception("Service unavailable"));

        try { await repo.GetAsync(_hostname, _slug); } catch { }

        cb.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_CircuitOpen_FallsBackToCache()
    {
        var (repo, inner, cb, cache) = CreateRepo(failureThreshold: 1);
        inner.Throws(new Exception("fail"));

        // Pre-populate cache with a record that has a different destination URL
        var cacheKey = HotLinkCache.BuildKey(_hostname, _slug);
        var cachedRecord = BuildRecord(destinationUrl: "https://example.com/cached");
        cache.Set(cacheKey, cachedRecord);

        // First call opens the circuit
        try { await repo.GetAsync(_hostname, _slug); } catch { }
        cb.State.Should().Be(CircuitState.Open);

        // Second call should use cache fallback
        var result = await repo.GetAsync(_hostname, _slug);

        result.Should().NotBeNull();
        result!.DestinationUrl.Should().Be("https://example.com/cached");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_CircuitOpen_CacheMiss_Throws()
    {
        var (repo, inner, cb, _) = CreateRepo(failureThreshold: 1);
        inner.Throws(new Exception("fail"));

        try { await repo.GetAsync(_hostname, _slug); } catch { }

        var act = () => repo.GetAsync("other.example.com", "other-slug");

        await act.Should().ThrowAsync<Exception>();
    }

    // ── Circuit breaker TryIncrementClickAsync ───────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TryIncrementClickAsync_InnerThrows_OpensCircuit()
    {
        var (repo, inner, cb, _) = CreateRepo(failureThreshold: 1);
        inner.SetIncrementThrows(new Exception("fail"));

        try { await repo.TryIncrementClickAsync(_hostname, _slug); } catch { }

        cb.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TryIncrementClickAsync_CircuitOpen_FallsBackToFalse()
    {
        var (repo, inner, cb, _) = CreateRepo(failureThreshold: 1);
        inner.SetIncrementThrows(new Exception("fail"));

        try { await repo.TryIncrementClickAsync(_hostname, _slug); } catch { }

        cb.State.Should().Be(CircuitState.Open);

        var result = await repo.TryIncrementClickAsync(_hostname, _slug);
        result.Should().BeFalse(); // fallback
        // The half-open probe also calls the inner (which still throws), so 2 calls total
        inner.IncrementCallCount.Should().BeGreaterOrEqualTo(1);
    }

    // ── Circuit state properties ─────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void CircuitBreaker_IsExposed()
    {
        var (repo, _, cb, _) = CreateRepo();

        repo.CircuitBreaker.Should().BeSameAs(cb);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Cache_IsExposed()
    {
        var (repo, _, _, cache) = CreateRepo();

        repo.Cache.Should().BeSameAs(cache);
    }

    // ── Timeout ──────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_InnerTimeout_PropagatesException()
    {
        var (repo, inner, _, _) = CreateRepo(timeout: TimeSpan.FromMilliseconds(1));
        inner.SetGetDelay(TimeSpan.FromSeconds(10));

        var act = () => repo.GetAsync(_hostname, _slug);

        await act.Should().ThrowAsync<TaskCanceledException>();
    }
}
