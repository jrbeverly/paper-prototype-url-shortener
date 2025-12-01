using ControlPlane.Api.Middleware;
using ControlPlane.UnitTests.Infrastructure;

namespace ControlPlane.UnitTests.Middleware;

public sealed class CorrelationIdMiddlewareTests : IAsyncDisposable
{
    private readonly UnitTestWebApplicationFactory _factory = new();

    // ── Correlation ID propagation ────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Request_WithCorrelationId_PropagatesSameIdToResponse()
    {
        var client = _factory.CreateClient();
        var expectedId = "test-correlation-abc-123";

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, expectedId);

        var response = await client.SendAsync(request);

        response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values)
            .Should().BeTrue("response must echo back the correlation ID header");
        values!.First().Should().Be(expectedId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Request_WithoutCorrelationId_GeneratesNewIdInResponse()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values)
            .Should().BeTrue("middleware must generate a correlation ID when none is provided");
        values!.First().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TwoRequests_WithoutCorrelationId_GetDifferentGeneratedIds()
    {
        var client = _factory.CreateClient();

        var r1 = await client.GetAsync("/health");
        var r2 = await client.GetAsync("/health");

        r1.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var v1);
        r2.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var v2);

        v1!.First().Should().NotBe(v2!.First(), "each request must receive a unique correlation ID");
    }

    // ── ParseXRayRootId ───────────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(
        "Root=1-5b0f9e4b-000000000000000000000001;Parent=53995c3f42cd8ad8;Sampled=1",
        "1-5b0f9e4b-000000000000000000000001")]
    [InlineData(
        "Root=1-abc12345-fedcba9876543210fedcba98;Sampled=0",
        "1-abc12345-fedcba9876543210fedcba98")]
    [InlineData(
        "root=1-UPPERCASE-000000000000000000000002;Parent=abc",
        "1-UPPERCASE-000000000000000000000002")]
    public void ParseXRayRootId_ValidHeader_ReturnsRootSegment(string header, string expected)
    {
        CorrelationIdMiddleware.ParseXRayRootId(header).Should().Be(expected);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Parent=53995c3f42cd8ad8;Sampled=1")]
    [InlineData("Sampled=1")]
    public void ParseXRayRootId_AbsentOrMissingRoot_ReturnsNull(string? header)
    {
        CorrelationIdMiddleware.ParseXRayRootId(header).Should().BeNull();
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
