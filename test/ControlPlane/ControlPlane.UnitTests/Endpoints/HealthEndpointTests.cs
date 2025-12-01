using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Endpoints;

public sealed class HealthEndpointTests : IAsyncDisposable
{
    private readonly UnitTestWebApplicationFactory _factory = new();

    // ── GET /health ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetHealth_Returns200WithHealthyStatus()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("healthy");
        body.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetHealth_DoesNotRequireAuthentication()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── GET /health/details ───────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetHealthDetails_Returns200WithDependenciesArray()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/details");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().BeOneOf("healthy", "degraded");
        body.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
        var deps = body.GetProperty("dependencies");
        deps.ValueKind.Should().Be(JsonValueKind.Array);
        deps.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetHealthDetails_DynamoDb_ReturnsHealthy()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/details");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var dynamoDb = body.GetProperty("dependencies").EnumerateArray()
            .Single(d => d.GetProperty("name").GetString() == "dynamodb");

        dynamoDb.GetProperty("status").GetString().Should().Be("healthy");
        dynamoDb.GetProperty("latencyMs").GetInt64().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetHealthDetails_Stripe_NotConfigured_WhenKeyIsEmpty()
    {
        // Stripe key is set to empty in UnitTestWebApplicationFactory.
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/details");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var stripe = body.GetProperty("dependencies").EnumerateArray()
            .Single(d => d.GetProperty("name").GetString() == "stripe");

        stripe.GetProperty("status").GetString().Should().Be("not_configured");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetHealthDetails_Postgres_NotConfigured()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/details");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var postgres = body.GetProperty("dependencies").EnumerateArray()
            .Single(d => d.GetProperty("name").GetString() == "postgres");

        postgres.GetProperty("status").GetString().Should().Be("not_configured");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetHealthDetails_HasThreeDependencies()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/details");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("dependencies").GetArrayLength().Should().Be(3);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetHealthDetails_DynamoDbFailure_ReturnsDegraded()
    {
        _factory.DynamoDb.ShouldFail = true;
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/details");

        // Still 200; degraded is not fatal.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("degraded");

        var dynamoDb = body.GetProperty("dependencies").EnumerateArray()
            .Single(d => d.GetProperty("name").GetString() == "dynamodb");
        dynamoDb.GetProperty("status").GetString().Should().Be("degraded");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetHealthDetails_CachesResults_DynamoDbCalledOnce()
    {
        var client = _factory.CreateClient();
        var initialCallCount = _factory.DynamoDb.ListCallCount;

        // Two back-to-back requests within the cache TTL.
        await client.GetAsync("/health/details");
        await client.GetAsync("/health/details");

        // DynamoDB should have been probed exactly once; second response is cached.
        var totalCalls = _factory.DynamoDb.ListCallCount - initialCallCount;
        totalCalls.Should().Be(1);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
