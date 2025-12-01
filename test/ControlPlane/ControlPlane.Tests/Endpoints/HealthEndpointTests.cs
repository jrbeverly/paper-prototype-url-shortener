using System.Text.Json;

namespace ControlPlane.Tests.Endpoints;

[Collection("DynamoDB")]
public sealed class HealthEndpointTests : IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;

    public HealthEndpointTests(LocalStackFixture localStack)
    {
        _factory = new CustomWebApplicationFactory(localStack.DynamoDb, localStack.TableName);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetHealth_WithoutAuth_Returns200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("healthy");
        body.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetHealth_ReturnsJsonContentType()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetHealthDetails_Returns200_WithDependencies()
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
    [Trait("Category", "Integration")]
    public async Task GetHealthDetails_DynamoDb_ReturnsHealthy()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/details");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var deps = body.GetProperty("dependencies").EnumerateArray();

        var dynamoDb = deps.Single(d => d.GetProperty("name").GetString() == "dynamodb");
        dynamoDb.GetProperty("status").GetString().Should().Be("healthy");
        dynamoDb.GetProperty("latencyMs").GetInt64().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetHealthDetails_IncludesPostgres_NotConfigured()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/details");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var deps = body.GetProperty("dependencies").EnumerateArray();

        var postgres = deps.Single(d => d.GetProperty("name").GetString() == "postgres");
        postgres.GetProperty("status").GetString().Should().Be("not_configured");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetHealthDetails_IncludesStripe_NotConfigured()
    {
        // Stripe key is set to empty in CustomWebApplicationFactory.
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/details");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var deps = body.GetProperty("dependencies").EnumerateArray();

        var stripe = deps.Single(d => d.GetProperty("name").GetString() == "stripe");
        stripe.GetProperty("status").GetString().Should().Be("not_configured");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetHealthDetails_HasThreeDependencies()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/details");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("dependencies").GetArrayLength().Should().Be(3);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
