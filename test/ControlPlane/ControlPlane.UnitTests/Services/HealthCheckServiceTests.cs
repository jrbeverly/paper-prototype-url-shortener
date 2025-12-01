using ControlPlane.Api.Services;
using ControlPlane.UnitTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ControlPlane.UnitTests.Services;

/// <summary>
/// Tests for <see cref="HealthCheckService"/> caching and failure-threshold logic.
/// Uses <see cref="TestAmazonDynamoDB"/> to control DynamoDB probe results without network calls.
/// </summary>
public sealed class HealthCheckServiceTests
{
    private readonly TestAmazonDynamoDB _dynamo = new();

    private HealthCheckService BuildSut(string stripeKey = "") =>
        new(_dynamo, Options.Create(new StripeOptions { SecretKey = stripeKey }),
            NullLogger<HealthCheckService>.Instance);

    // ── GetHealthAsync ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetHealth_AlwaysReturnsHealthy()
    {
        var sut = BuildSut();
        var result = await sut.GetHealthAsync();
        result.Status.Should().Be("healthy");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetHealth_ReturnsVersionString()
    {
        var sut = BuildSut();
        var result = await sut.GetHealthAsync();
        result.Version.Should().NotBeNullOrEmpty();
    }

    // ── GetDetailedHealthAsync — dependency statuses ──────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDetailedHealth_DynamoDbHealthy_ReturnsHealthy()
    {
        var sut = BuildSut();
        var result = await sut.GetDetailedHealthAsync();

        var dynamo = result.Dependencies.Single(d => d.Name == "dynamodb");
        dynamo.Status.Should().Be("healthy");
        dynamo.LatencyMs.Should().BeGreaterThanOrEqualTo(0);
        dynamo.Error.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDetailedHealth_StripeKeyEmpty_ReportsNotConfigured()
    {
        var sut = BuildSut(stripeKey: "");
        var result = await sut.GetDetailedHealthAsync();

        result.Dependencies.Single(d => d.Name == "stripe").Status.Should().Be("not_configured");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDetailedHealth_PostgresAlwaysNotConfigured()
    {
        var sut = BuildSut();
        var result = await sut.GetDetailedHealthAsync();
        result.Dependencies.Single(d => d.Name == "postgres").Status.Should().Be("not_configured");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDetailedHealth_IncludesThreeDependencies()
    {
        var sut = BuildSut();
        var result = await sut.GetDetailedHealthAsync();
        result.Dependencies.Should().HaveCount(3);
    }

    // ── Failure threshold ─────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDetailedHealth_OneDynamoDbFailure_ReturnsDegraded()
    {
        _dynamo.ShouldFail = true;
        var sut = BuildSut();

        var result = await sut.GetDetailedHealthAsync();

        result.Status.Should().Be("degraded");
        result.Dependencies.Single(d => d.Name == "dynamodb").Status.Should().Be("degraded");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDetailedHealth_TwoDynamoDbFailures_StillDegraded()
    {
        _dynamo.ShouldFail = true;
        var sut = BuildSut();

        await sut.GetDetailedHealthAsync();          // failure 1
        sut.InvalidateCache();
        var result = await sut.GetDetailedHealthAsync(); // failure 2

        result.Dependencies.Single(d => d.Name == "dynamodb").Status.Should().Be("degraded");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDetailedHealth_ThreeDynamoDbFailures_ReturnsUnhealthy()
    {
        _dynamo.ShouldFail = true;
        var sut = BuildSut();

        await sut.GetDetailedHealthAsync(); sut.InvalidateCache(); // failure 1
        await sut.GetDetailedHealthAsync(); sut.InvalidateCache(); // failure 2
        var result = await sut.GetDetailedHealthAsync();           // failure 3

        result.Status.Should().Be("unhealthy");
        result.Dependencies.Single(d => d.Name == "dynamodb").Status.Should().Be("unhealthy");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDetailedHealth_RecoveryAfterFailures_ResetsToHealthy()
    {
        _dynamo.ShouldFail = true;
        var sut = BuildSut();

        await sut.GetDetailedHealthAsync(); // failure 1 → degraded
        sut.InvalidateCache();

        _dynamo.ShouldFail = false;
        var ok = await sut.GetDetailedHealthAsync();

        ok.Status.Should().Be("healthy");
        ok.Dependencies.Single(d => d.Name == "dynamodb").Status.Should().Be("healthy");
    }

    // ── Caching ───────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDetailedHealth_SecondCallWithinTtl_UsesCachedResult()
    {
        var sut = BuildSut();
        var before = _dynamo.ListCallCount;

        await sut.GetDetailedHealthAsync();
        await sut.GetDetailedHealthAsync();

        (_dynamo.ListCallCount - before).Should().Be(1,
            because: "the second call within the 15-second TTL should be served from cache");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDetailedHealth_AfterCacheInvalidation_ProbesDependenciesAgain()
    {
        var sut = BuildSut();
        var before = _dynamo.ListCallCount;

        await sut.GetDetailedHealthAsync();
        sut.InvalidateCache();
        await sut.GetDetailedHealthAsync();

        (_dynamo.ListCallCount - before).Should().Be(2,
            because: "cache invalidation forces a fresh probe on the next call");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDetailedHealth_CachedResult_ReturnsConsistentData()
    {
        var sut = BuildSut();
        var first = await sut.GetDetailedHealthAsync();
        var second = await sut.GetDetailedHealthAsync();

        second.Status.Should().Be(first.Status);
        second.Version.Should().Be(first.Version);
        second.Dependencies.Should().HaveCount(first.Dependencies.Count);
    }
}
