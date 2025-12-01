namespace RedirectService.PerformanceTests.Benchmarks;

/// <summary>
/// Measures and documents cold start overhead for the redirect resolution path.
///
/// "Cold start" in a Lambda context includes:
///   1. Container allocation (OS, JVM/CLR runtime)  — not measurable in tests; typically 200-500ms
///   2. .NET 8 CLR initialization                   — not measurable in tests; typically 100-300ms
///   3. ReadyToRun (R2R) pre-compiled startup        — reduces step 2 by ~40% (configured in this project)
///   4. Application-layer initialization             — MEASURED HERE: service graph wiring + JIT
///   5. First request processing                     — MEASURED HERE: first call through JIT'd code
///
/// This test quantifies the application-layer portion (4 + 5) so regressions in startup
/// complexity (added singletons, heavy constructors, etc.) are detected before they ship.
///
/// Typical values (LocalStack; real DynamoDB will show lower latency):
///   Application-layer init:  1–10ms
///   First call overhead:     10–50ms above warm p50
///   Overall Lambda cold start estimate: ~500–1500ms (steps 1–3) + application layer
/// </summary>
[Collection("DynamoDB")]
public sealed class ColdStartLatencyTests(LocalStackFixture fixture, ITestOutputHelper output)
{
    private const int _warmSamples = 100;

    [Fact]
    [Trait("Category", "Performance")]
    public async Task FirstInvocation_ColdStartOverhead_IsDocumented()
    {
        var hostname = $"perf-cold-{Guid.NewGuid():N}.example.com";
        const string slug = "cold-bench";
        await fixture.SeedLinkAsync(hostname, slug);

        var request = new RedirectRequest
        {
            Hostname = hostname,
            Slug = slug,
            Headers = new Dictionary<string, string>(),
            QueryParameters = new Dictionary<string, string>()
        };

        // ── Application-layer init time (service graph construction) ──────────
        var initSw = Stopwatch.StartNew();
        var innerRepo = new DynamoDbRedirectRepository(fixture.DynamoDb, fixture.TableName);
        var circuitBreaker = new CircuitBreaker(
            "DynamoDB", failureThreshold: 3, cooldownPeriod: TimeSpan.FromSeconds(30));
        var hotLinkCache = new HotLinkCache(ttl: TimeSpan.FromMinutes(5), maxEntries: 1000);
        var repo = new ResilientRedirectRepository(innerRepo, circuitBreaker, hotLinkCache);
        var service = new Api.Services.RedirectService(repo, new NullClickEventEmitter());
        initSw.Stop();
        var initMs = initSw.Elapsed.TotalMilliseconds;

        // ── First call (cold path — JIT compiles all hot branches) ────────────
        var firstCallSw = Stopwatch.StartNew();
        var firstResult = await service.ResolveAsync(request);
        firstCallSw.Stop();
        var firstCallMs = firstCallSw.Elapsed.TotalMilliseconds;

        firstResult.Outcome.Should().Be(RedirectOutcome.Redirect,
            "seed data must be reachable for a meaningful cold-start measurement");

        // ── Warm samples (post-JIT, cache-warm reads) ─────────────────────────
        var warmRecorder = new LatencyRecorder("ResolveAsync (warm)");
        for (var i = 0; i < _warmSamples; i++)
        {
            var sw = Stopwatch.StartNew();
            await service.ResolveAsync(request);
            sw.Stop();
            warmRecorder.Record(sw.Elapsed);
        }

        var warmP50 = warmRecorder.P50;
        var warmP99 = warmRecorder.P99;
        var firstCallOverheadMs = firstCallMs - warmP50;

        // ── Report ────────────────────────────────────────────────────────────
        output.WriteLine("Cold start simulation (application-layer only):");
        output.WriteLine($"  Service graph init:      {initMs,7:F1}ms");
        output.WriteLine($"  First call:              {firstCallMs,7:F1}ms");
        output.WriteLine($"  Warm p50:                {warmP50,7:F1}ms");
        output.WriteLine($"  Warm p99:                {warmP99,7:F1}ms");
        output.WriteLine($"  First-call overhead:     {firstCallOverheadMs,7:F1}ms  " +
                         $"(= first call − warm p50)");
        output.WriteLine("");
        output.WriteLine("Lambda cold start baseline (estimated, not measured here):");
        output.WriteLine("  Container + CLR init:    ~200–500ms  (steps 1–3 above)");
        output.WriteLine("  ReadyToRun (R2R) savings: ~40% reduction on CLR init");
        output.WriteLine("  Total estimated cold start: ~500–1500ms");

        // ── Assertions ────────────────────────────────────────────────────────

        // Service graph construction must be trivial — catches accidentally expensive constructors.
        initMs.Should().BeLessThan(500,
            because: $"application-layer service graph construction should complete in < 500ms. " +
                     $"Actual: {initMs:F1}ms. Heavy constructors delay Lambda cold start.");

        // First call must complete within a generous budget.
        // Catches hangs or catastrophic initialization failures.
        firstCallMs.Should().BeLessThan(5000,
            because: $"first call (JIT + cold DynamoDB) should complete in < 5s. " +
                     $"Actual: {firstCallMs:F1}ms.");

        // First-call overhead above warm p50 should be bounded.
        // A large delta indicates expensive JIT or cache misses on the hot path.
        firstCallOverheadMs.Should().BeLessThan(2000,
            because: $"first-call overhead above warm p50 should be < 2s. " +
                     $"Actual: {firstCallOverheadMs:F1}ms (first={firstCallMs:F1}ms, warm-p50={warmP50:F1}ms). " +
                     $"Large overhead suggests expensive JIT or heavyweight initialization on the hot path.");
    }

    /// <summary>
    /// Verifies that the service graph can be reconstructed quickly — relevant because Lambda
    /// can recycle containers, forcing re-initialization of the static + instance state.
    /// </summary>
    [Fact]
    [Trait("Category", "Performance")]
    public void ServiceGraphInit_RepeatedConstruction_RemainsUnder100ms()
    {
        // After the first test the CLR is warm, so this measures pure allocation cost.
        var recorder = new LatencyRecorder("Service graph construction");

        for (var i = 0; i < 20; i++)
        {
            var sw = Stopwatch.StartNew();
            var innerRepo = new DynamoDbRedirectRepository(fixture.DynamoDb, fixture.TableName);
            var cb = new CircuitBreaker("DynamoDB", failureThreshold: 3, cooldownPeriod: TimeSpan.FromSeconds(30));
            var cache = new HotLinkCache(ttl: TimeSpan.FromMinutes(5), maxEntries: 1000);
            var repo = new ResilientRedirectRepository(innerRepo, cb, cache);
            _ = new Api.Services.RedirectService(repo, new NullClickEventEmitter());
            sw.Stop();
            recorder.Record(sw.Elapsed);
        }

        recorder.WriteSummary(output);

        recorder.AssertPercentileBelowMs(99, 100);
    }
}
