namespace RedirectService.PerformanceTests.Infrastructure;

/// <summary>
/// Records latency samples and computes percentiles for performance assertions.
/// </summary>
public sealed class LatencyRecorder(string operationName)
{
    private readonly List<double> _samples = [];

    public void Record(TimeSpan elapsed) => _samples.Add(elapsed.TotalMilliseconds);

    public int Count => _samples.Count;

    /// <summary>
    /// Returns the value at the given percentile using the nearest-rank method.
    /// For example, <c>Percentile(99)</c> returns the 99th-percentile latency in milliseconds.
    /// </summary>
    public double Percentile(int pct)
    {
        if (_samples.Count == 0)
            throw new InvalidOperationException($"No samples recorded for '{operationName}'.");

        var sorted = _samples.OrderBy(x => x).ToArray();
        var rank = (int)Math.Ceiling(pct / 100.0 * sorted.Length) - 1;
        rank = Math.Clamp(rank, 0, sorted.Length - 1);
        return sorted[rank];
    }

    public double P50 => Percentile(50);
    public double P95 => Percentile(95);
    public double P99 => Percentile(99);

    /// <summary>
    /// Asserts that the given percentile is below <paramref name="thresholdMs"/>.
    /// Throws with a detailed failure message if the threshold is violated.
    /// </summary>
    public void AssertPercentileBelowMs(int pct, double thresholdMs)
    {
        var actual = Percentile(pct);
        if (actual <= thresholdMs)
            return;

        throw new PerformanceThresholdException(
            $"PERFORMANCE THRESHOLD VIOLATED — {operationName} p{pct}\n" +
            $"  p{pct}: {actual:F1}ms  (threshold: < {thresholdMs:F0}ms)\n" +
            $"  p50:  {P50:F1}ms\n" +
            $"  p95:  {P95:F1}ms\n" +
            $"  p99:  {P99:F1}ms\n" +
            $"  n:    {Count} samples\n" +
            $"\n" +
            $"Production targets: PERF_DYNAMO_P99_MS (DynamoDB read, default 10ms), " +
            $"PERF_WARM_P99_MS (warm invocation, default 50ms).\n" +
            $"To loosen thresholds for slower CI environments set these env vars.");
    }

    /// <summary>Writes a one-line percentile summary to the test output.</summary>
    public void WriteSummary(ITestOutputHelper output)
        => output.WriteLine(
            $"{operationName,-40} p50={P50,7:F1}ms  p95={P95,7:F1}ms  p99={P99,7:F1}ms  n={Count}");
}

/// <summary>Thrown when a performance threshold is violated.</summary>
public sealed class PerformanceThresholdException(string message) : Exception(message);
