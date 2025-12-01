namespace RedirectService.PerformanceTests.Benchmarks;

/// <summary>
/// Measures end-to-end warm invocation latency through the full redirect resolution chain:
/// RedirectService.ResolveAsync → ResilientRedirectRepository → DynamoDB (LocalStack).
///
/// "Warm" means the hot-link cache is pre-populated, so reads hit the in-memory cache
/// and only the click-increment UpdateItem goes to DynamoDB — matching the common
/// production pattern after the first access.
///
/// Production targets (set via env vars):
///   PERF_WARM_P99_MS — p99 warm invocation in ms (production target: 50ms)
///
/// LocalStack default: 500ms.  To validate production targets, point DYNAMODB_ENDPOINT
/// at real DynamoDB and set PERF_WARM_P99_MS=50.
/// </summary>
[Collection("DynamoDB")]
public sealed class WarmInvocationLatencyTests(LocalStackFixture fixture, ITestOutputHelper output)
{
    private static readonly double _p99ThresholdMs =
        double.TryParse(Environment.GetEnvironmentVariable("PERF_WARM_P99_MS"), out var v) ? v : 500;

    private const int _iterations = 200;
    private const int _warmupIterations = 10;

    // ── Full resolution chain (cache-warm reads + DynamoDB writes) ────────────

    [Fact]
    [Trait("Category", "Performance")]
    public async Task ResolveAsync_CacheWarm_MeetsLatencyThresholds()
    {
        var hostname = $"perf-warm-{Guid.NewGuid():N}.example.com";
        const string slug = "bench-link";
        await fixture.SeedLinkAsync(hostname, slug);

        var request = new RedirectRequest
        {
            Hostname = hostname,
            Slug = slug,
            Headers = new Dictionary<string, string>(),
            QueryParameters = new Dictionary<string, string>()
        };

        var service = BuildService();
        var recorder = new LatencyRecorder("ResolveAsync (cache-warm)");

        // Populate the hot-link cache so subsequent reads are in-memory.
        for (var i = 0; i < _warmupIterations; i++)
            await service.ResolveAsync(request);

        for (var i = 0; i < _iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            await service.ResolveAsync(request);
            sw.Stop();
            recorder.Record(sw.Elapsed);
        }

        recorder.WriteSummary(output);
        output.WriteLine($"Threshold: p99 < {_p99ThresholdMs:F0}ms  (production target: 50ms)");
        output.WriteLine("Note: reads hit the in-memory hot-link cache; only the UpdateItem " +
                         "(click increment) goes to DynamoDB.");

        recorder.AssertPercentileBelowMs(50, _p99ThresholdMs * 0.4);
        recorder.AssertPercentileBelowMs(95, _p99ThresholdMs * 0.75);
        recorder.AssertPercentileBelowMs(99, _p99ThresholdMs);
    }

    // ── Cold-cache reads (every call hits DynamoDB for both operations) ────────

    [Fact]
    [Trait("Category", "Performance")]
    public async Task ResolveAsync_CacheCold_DualDynamoDbRoundTrip_MeetsLatencyThresholds()
    {
        // Use a unique hostname per-iteration so the hot-link cache never hits.
        // Measures the worst-case scenario: full GetItem + UpdateItem on every call.
        const int coldIterations = 50; // fewer because each call is 2× DynamoDB ops
        var recorder = new LatencyRecorder("ResolveAsync (cache-cold, 2× DynamoDB)");

        // Warm DynamoDB connection pool only (using a throwaway host).
        var warmupHost = $"perf-coldwarm-{Guid.NewGuid():N}.example.com";
        await fixture.SeedLinkAsync(warmupHost, "warmup");
        var warmupService = BuildService();
        var warmupRequest = new RedirectRequest
        {
            Hostname = warmupHost,
            Slug = "warmup",
            Headers = new Dictionary<string, string>(),
            QueryParameters = new Dictionary<string, string>()
        };
        for (var i = 0; i < 5; i++)
            await warmupService.ResolveAsync(warmupRequest);

        // Measure cold-cache scenario: fresh service instance per call.
        for (var i = 0; i < coldIterations; i++)
        {
            var hostname = $"perf-cold-{Guid.NewGuid():N}.example.com";
            const string slug = "bench-link";
            await fixture.SeedLinkAsync(hostname, slug);

            var freshService = BuildService();
            var request = new RedirectRequest
            {
                Hostname = hostname,
                Slug = slug,
                Headers = new Dictionary<string, string>(),
                QueryParameters = new Dictionary<string, string>()
            };

            var sw = Stopwatch.StartNew();
            await freshService.ResolveAsync(request);
            sw.Stop();
            recorder.Record(sw.Elapsed);
        }

        recorder.WriteSummary(output);
        // Cold-cache budget is 2× warm budget (two DynamoDB round-trips).
        var coldThreshold = _p99ThresholdMs * 2;
        output.WriteLine($"Threshold: p99 < {coldThreshold:F0}ms  (production target: 100ms, 2× DynamoDB ops)");

        recorder.AssertPercentileBelowMs(50, coldThreshold * 0.5);
        recorder.AssertPercentileBelowMs(95, coldThreshold * 0.8);
        recorder.AssertPercentileBelowMs(99, coldThreshold);
    }

    // ── Various destination complexity sizes ──────────────────────────────────

    [Theory]
    [Trait("Category", "Performance")]
    [InlineData("https://example.com/short")]
    [InlineData("https://example.com/medium/path/with/several/segments?utm_source=newsletter&utm_medium=email")]
    [InlineData("https://example.com/long/path/to/a/deeply/nested/page?utm_source=newsletter&utm_medium=email&utm_campaign=summer-sale-2026&utm_content=hero-cta&ref=shortio")]
    public async Task ResolveAsync_CacheWarm_VaryingDestinationLength_MeetsThreshold(string destination)
    {
        var hostname = $"perf-size-{Guid.NewGuid():N}.example.com";
        const string slug = "bench-link";
        await fixture.SeedLinkAsync(hostname, slug, destination);

        var request = new RedirectRequest
        {
            Hostname = hostname,
            Slug = slug,
            Headers = new Dictionary<string, string>(),
            QueryParameters = new Dictionary<string, string>()
        };

        var service = BuildService();

        for (var i = 0; i < _warmupIterations; i++)
            await service.ResolveAsync(request);

        var recorder = new LatencyRecorder($"ResolveAsync (url-length={destination.Length})");
        for (var i = 0; i < 100; i++)
        {
            var sw = Stopwatch.StartNew();
            await service.ResolveAsync(request);
            sw.Stop();
            recorder.Record(sw.Elapsed);
        }

        recorder.WriteSummary(output);
        recorder.AssertPercentileBelowMs(99, _p99ThresholdMs);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Api.Services.RedirectService BuildService()
    {
        var innerRepo = new DynamoDbRedirectRepository(fixture.DynamoDb, fixture.TableName);
        var circuitBreaker = new CircuitBreaker(
            "DynamoDB", failureThreshold: 3, cooldownPeriod: TimeSpan.FromSeconds(30));
        var hotLinkCache = new HotLinkCache(ttl: TimeSpan.FromMinutes(5), maxEntries: 1000);
        var repo = new ResilientRedirectRepository(innerRepo, circuitBreaker, hotLinkCache);
        return new Api.Services.RedirectService(repo, new NullClickEventEmitter());
    }
}
