using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using Stripe;

namespace ControlPlane.Api.Services;

public interface IHealthCheckService
{
    Task<HealthReport> GetHealthAsync(CancellationToken ct = default);
    Task<DetailedHealthReport> GetDetailedHealthAsync(CancellationToken ct = default);
}

public sealed record HealthReport
{
    public required string Status { get; init; }
    public required string Version { get; init; }
}

public sealed record DetailedHealthReport
{
    public required string Status { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyList<DependencyStatus> Dependencies { get; init; }
}

public sealed record DependencyStatus
{
    public required string Name { get; init; }
    public required string Status { get; init; }
    public long LatencyMs { get; init; }
    public string? Error { get; init; }
}

public sealed class HealthCheckService : IHealthCheckService
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly IOptions<StripeOptions> _stripeOptions;
    private readonly ILogger<HealthCheckService> _logger;

    private static readonly string _version =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

    // Consecutive failure counts per dependency name; cleared on recovery.
    private readonly ConcurrentDictionary<string, int> _consecutiveFailures = new();

    // Cached result and its expiry. Protected by _cacheLock.
    private volatile DetailedHealthReport? _cached;
    private DateTimeOffset _cacheExpiry = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private static readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(15);

    // Threshold: 1-2 consecutive failures → degraded; 3+ → unhealthy.
    private const int _unhealthyThreshold = 3;

    private const int _dynamoDbTimeoutMs = 500;
    private const int _stripeTimeoutMs = 2000;

    public HealthCheckService(
        IAmazonDynamoDB dynamoDb,
        IOptions<StripeOptions> stripeOptions,
        ILogger<HealthCheckService> logger)
    {
        _dynamoDb = dynamoDb;
        _stripeOptions = stripeOptions;
        _logger = logger;
    }

    public Task<HealthReport> GetHealthAsync(CancellationToken ct = default) =>
        Task.FromResult(new HealthReport { Status = "healthy", Version = _version });

    public async Task<DetailedHealthReport> GetDetailedHealthAsync(CancellationToken ct = default)
    {
        // Fast path: serve cached result without acquiring the lock.
        var now = DateTimeOffset.UtcNow;
        if (_cached is not null && now < _cacheExpiry)
            return _cached;

        await _cacheLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring the lock.
            now = DateTimeOffset.UtcNow;
            if (_cached is not null && now < _cacheExpiry)
                return _cached;

            var dependencies = new List<DependencyStatus>
            {
                await CheckDynamoDbAsync(ct),
                await CheckStripeAsync(ct),
                GetPostgresStatus()
            };

            var report = new DetailedHealthReport
            {
                Status = ComputeStatus(dependencies),
                Version = _version,
                Dependencies = dependencies
            };

            _cached = report;
            _cacheExpiry = now.Add(_cacheTtl);

            EmitMetrics(dependencies);

            return report;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    // ── Status helpers ───────────────────────────────────────────────────────

    private static string ComputeStatus(IReadOnlyList<DependencyStatus> dependencies)
    {
        var active = dependencies.Where(d => d.Status != "not_configured").ToList();
        if (active.Any(d => d.Status == "unhealthy")) return "unhealthy";
        if (active.Any(d => d.Status == "degraded")) return "degraded";
        return "healthy";
    }

    private string Classify(string name, bool success)
    {
        if (success)
        {
            _consecutiveFailures.TryRemove(name, out _);
            return "healthy";
        }

        var count = _consecutiveFailures.AddOrUpdate(name, 1, (_, prev) => prev + 1);
        return count >= _unhealthyThreshold ? "unhealthy" : "degraded";
    }

    // ── Dependency probes ────────────────────────────────────────────────────

    private async Task<DependencyStatus> CheckDynamoDbAsync(CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_dynamoDbTimeoutMs);

        var sw = Stopwatch.StartNew();
        try
        {
            await _dynamoDb.ListTablesAsync(new ListTablesRequest { Limit = 1 }, timeoutCts.Token);
            sw.Stop();
            return new DependencyStatus
            {
                Name = "dynamodb",
                Status = Classify("dynamodb", true),
                LatencyMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning(ex, "DynamoDB health check failed: {Message}", ex.Message);
            return new DependencyStatus
            {
                Name = "dynamodb",
                Status = Classify("dynamodb", false),
                LatencyMs = sw.ElapsedMilliseconds,
                Error = ex.Message
            };
        }
    }

    private async Task<DependencyStatus> CheckStripeAsync(CancellationToken ct)
    {
        var secretKey = _stripeOptions.Value.SecretKey;
        if (string.IsNullOrWhiteSpace(secretKey))
            return new DependencyStatus { Name = "stripe", Status = "not_configured" };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_stripeTimeoutMs);

        var sw = Stopwatch.StartNew();
        try
        {
            var client = new StripeClient(secretKey);
            await new BalanceService(client).GetAsync(cancellationToken: timeoutCts.Token);
            sw.Stop();
            return new DependencyStatus
            {
                Name = "stripe",
                Status = Classify("stripe", true),
                LatencyMs = sw.ElapsedMilliseconds
            };
        }
        catch (StripeException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // 401 = Stripe is reachable; treat as healthy connectivity regardless of key validity.
            sw.Stop();
            return new DependencyStatus
            {
                Name = "stripe",
                Status = Classify("stripe", true),
                LatencyMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Stripe health check failed: {Message}", ex.Message);
            return new DependencyStatus
            {
                Name = "stripe",
                Status = Classify("stripe", false),
                LatencyMs = sw.ElapsedMilliseconds,
                Error = ex.Message
            };
        }
    }

    private static DependencyStatus GetPostgresStatus() =>
        new() { Name = "postgres", Status = "not_configured" };

    // ── Test support ─────────────────────────────────────────────────────────

    // Invalidates the cache so the next call performs fresh dependency probes.
    // Marked internal; exposed via InternalsVisibleTo for ControlPlane.UnitTests only.
    internal void InvalidateCache()
    {
        _cached = null;
        _cacheExpiry = DateTimeOffset.MinValue;
    }

    // ── Metric emission ──────────────────────────────────────────────────────

    // Emits structured log events. Set up a CloudWatch Logs Metric Filter on
    // the pattern "HealthMetric" to extract DependencyHealth as a custom metric.
    private void EmitMetrics(IReadOnlyList<DependencyStatus> dependencies)
    {
        foreach (var dep in dependencies.Where(d => d.Status != "not_configured"))
        {
            double value = dep.Status switch
            {
                "healthy" => 1.0,
                "degraded" => 0.5,
                _ => 0.0
            };
            _logger.LogInformation(
                "HealthMetric Dependency={Dependency} Status={Status} HealthValue={HealthValue} LatencyMs={LatencyMs}",
                dep.Name, dep.Status, value, dep.LatencyMs);
        }
    }
}
