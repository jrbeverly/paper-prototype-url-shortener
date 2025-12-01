namespace RedirectService.PerformanceTests.Benchmarks;

/// <summary>
/// Measures raw DynamoDB round-trip latency for redirect lookups (GetItem) and
/// click increments (UpdateItem) against a LocalStack instance.
///
/// Production targets (set via env vars to enforce in staging/prod):
///   PERF_DYNAMO_P99_MS — p99 latency in ms (production target: 10ms)
///
/// LocalStack default: 200ms (catches regressions; not representative of real DynamoDB speed).
/// To validate production targets, set DYNAMODB_ENDPOINT to a real DynamoDB endpoint and
/// set PERF_DYNAMO_P99_MS=10.
/// </summary>
[Collection("DynamoDB")]
public sealed class DynamoDbLookupLatencyTests(LocalStackFixture fixture, ITestOutputHelper output)
{
    private static readonly double _p99ThresholdMs =
        double.TryParse(Environment.GetEnvironmentVariable("PERF_DYNAMO_P99_MS"), out var v) ? v : 200;

    private const int _iterations = 200;
    private const int _warmupIterations = 10;

    // ── GetItem (redirect lookup) ─────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Performance")]
    public async Task GetAsync_WarmRead_MeetsLatencyThresholds()
    {
        var hostname = $"perf-read-{Guid.NewGuid():N}.example.com";
        const string slug = "bench-link";
        await fixture.SeedLinkAsync(hostname, slug);

        var repo = new DynamoDbRedirectRepository(fixture.DynamoDb, fixture.TableName);
        var recorder = new LatencyRecorder("DynamoDB GetItem (read)");

        for (var i = 0; i < _warmupIterations; i++)
            await repo.GetAsync(hostname, slug);

        for (var i = 0; i < _iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            await repo.GetAsync(hostname, slug);
            sw.Stop();
            recorder.Record(sw.Elapsed);
        }

        recorder.WriteSummary(output);
        output.WriteLine($"Threshold: p99 < {_p99ThresholdMs:F0}ms  (production target: 10ms)");

        recorder.AssertPercentileBelowMs(50, _p99ThresholdMs * 0.5);
        recorder.AssertPercentileBelowMs(95, _p99ThresholdMs * 0.8);
        recorder.AssertPercentileBelowMs(99, _p99ThresholdMs);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task GetAsync_MissingLink_MeetsLatencyThresholds()
    {
        // Not-found lookups (GetItem returning empty) should be just as fast.
        var hostname = $"perf-miss-{Guid.NewGuid():N}.example.com";

        var repo = new DynamoDbRedirectRepository(fixture.DynamoDb, fixture.TableName);
        var recorder = new LatencyRecorder("DynamoDB GetItem (miss)");

        for (var i = 0; i < _warmupIterations; i++)
            await repo.GetAsync(hostname, "no-such-slug");

        for (var i = 0; i < _iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            await repo.GetAsync(hostname, "no-such-slug");
            sw.Stop();
            recorder.Record(sw.Elapsed);
        }

        recorder.WriteSummary(output);
        output.WriteLine($"Threshold: p99 < {_p99ThresholdMs:F0}ms  (production target: 10ms)");

        recorder.AssertPercentileBelowMs(50, _p99ThresholdMs * 0.5);
        recorder.AssertPercentileBelowMs(95, _p99ThresholdMs * 0.8);
        recorder.AssertPercentileBelowMs(99, _p99ThresholdMs);
    }

    // ── UpdateItem (click increment) ──────────────────────────────────────────

    [Fact]
    [Trait("Category", "Performance")]
    public async Task TryIncrementClickAsync_WarmWrite_MeetsLatencyThresholds()
    {
        var hostname = $"perf-write-{Guid.NewGuid():N}.example.com";
        const string slug = "bench-link";
        await fixture.SeedLinkAsync(hostname, slug);

        var repo = new DynamoDbRedirectRepository(fixture.DynamoDb, fixture.TableName);
        var recorder = new LatencyRecorder("DynamoDB UpdateItem (increment)");

        for (var i = 0; i < _warmupIterations; i++)
            await repo.TryIncrementClickAsync(hostname, slug);

        for (var i = 0; i < _iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            await repo.TryIncrementClickAsync(hostname, slug);
            sw.Stop();
            recorder.Record(sw.Elapsed);
        }

        recorder.WriteSummary(output);
        output.WriteLine($"Threshold: p99 < {_p99ThresholdMs:F0}ms  (production target: 10ms)");

        recorder.AssertPercentileBelowMs(50, _p99ThresholdMs * 0.5);
        recorder.AssertPercentileBelowMs(95, _p99ThresholdMs * 0.8);
        recorder.AssertPercentileBelowMs(99, _p99ThresholdMs);
    }
}

