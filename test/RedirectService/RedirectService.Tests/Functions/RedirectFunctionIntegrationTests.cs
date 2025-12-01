using Amazon.DynamoDBv2;
using Amazon.Lambda.APIGatewayEvents;
using RedirectService.Api.Functions;
// Alias required: the class name "RedirectService" conflicts with the root namespace "RedirectService".
using RedirectServiceImpl = global::RedirectService.Api.Services.RedirectService;

namespace RedirectService.Tests.Functions;

/// <summary>
/// End-to-end integration tests for <see cref="RedirectFunction"/>.
/// Exercises the full request → DynamoDB lookup → rule evaluation → HTTP response chain
/// against a real LocalStack DynamoDB instance.
/// </summary>
[Collection("DynamoDB")]
public sealed class RedirectFunctionIntegrationTests
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;

    public RedirectFunctionIntegrationTests(LocalStackFixture fixture)
    {
        _dynamoDb = fixture.DynamoDb;
        _tableName = fixture.TableName;
    }

    // Each test gets a unique hostname so tests run independently without cleanup.
    private static string UniqueHost() => $"go-{Guid.NewGuid():N}.example.com";

    private RedirectFunction CreateFunction()
    {
        var redirectRepo = new DynamoDbRedirectRepository(_dynamoDb, _tableName);
        var domainConfigRepo = new DynamoDbDomainConfigRepository(_dynamoDb, _tableName);
        var service = new RedirectServiceImpl(redirectRepo, new NullClickEventEmitter());
        return new RedirectFunction(_dynamoDb, service, domainConfigRepo);
    }

    private static APIGatewayHttpApiV2ProxyRequest BuildRequest(
        string host,
        string slug,
        string? correlationId = null)
    {
        var headers = new Dictionary<string, string> { ["host"] = host };
        if (correlationId is not null)
            headers["x-correlation-id"] = correlationId;

        return new APIGatewayHttpApiV2ProxyRequest
        {
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext
            {
                Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription { Path = "/" + slug }
            },
            Headers = headers,
            QueryStringParameters = new Dictionary<string, string>()
        };
    }

    private async Task SeedLinkAsync(
        string hostname,
        string slug,
        string destination,
        int redirectType = 302,
        string status = "active",
        string? expiresAt = null,
        int? maxClicks = null,
        long currentClicks = 0)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new AttributeValue { S = $"HOST#{hostname}#SLUG#{slug}" },
            ["SK"] = new AttributeValue { S = "CONFIG" },
            ["TenantId"] = new AttributeValue { S = "tenant-integration-test" },
            ["DomainId"] = new AttributeValue { S = "domain-integration-test" },
            ["Hostname"] = new AttributeValue { S = hostname },
            ["Slug"] = new AttributeValue { S = slug },
            ["DestinationUrl"] = new AttributeValue { S = destination },
            ["RedirectType"] = new AttributeValue { N = redirectType.ToString() },
            ["Status"] = new AttributeValue { S = status },
            ["CurrentClicks"] = new AttributeValue { N = currentClicks.ToString() }
        };
        if (expiresAt is not null)
            item["ExpiresAt"] = new AttributeValue { S = expiresAt };
        if (maxClicks.HasValue)
            item["MaxClicks"] = new AttributeValue { N = maxClicks.Value.ToString() };

        await _dynamoDb.PutItemAsync(new PutItemRequest { TableName = _tableName, Item = item });
    }

    private async Task SeedDomainConfigAsync(
        string hostname,
        string? fallbackUrl = null,
        string? brandColor = null)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new AttributeValue { S = $"HOST#{hostname}" },
            ["SK"] = new AttributeValue { S = "DOMAIN_CONFIG" }
        };
        if (fallbackUrl is not null)
            item["FallbackUrl"] = new AttributeValue { S = fallbackUrl };
        if (brandColor is not null)
            item["BrandColor"] = new AttributeValue { S = brandColor };

        await _dynamoDb.PutItemAsync(new PutItemRequest { TableName = _tableName, Item = item });
    }

    // ── Redirect types ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_Active302Link_Returns302WithLocation()
    {
        var host = UniqueHost();
        await SeedLinkAsync(host, "link", "https://example.com/landing");
        var fn = CreateFunction();

        var response = await fn.HandleAsync(BuildRequest(host, "link"), new StubLambdaContext());

        response.StatusCode.Should().Be(302);
        response.Headers["Location"].Should().Be("https://example.com/landing");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_Active301Link_Returns301WithLocation()
    {
        var host = UniqueHost();
        await SeedLinkAsync(host, "perm", "https://example.com/permanent", redirectType: 301);
        var fn = CreateFunction();

        var response = await fn.HandleAsync(BuildRequest(host, "perm"), new StubLambdaContext());

        response.StatusCode.Should().Be(301);
        response.Headers["Location"].Should().Be("https://example.com/permanent");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_Active307Link_Returns307WithLocation()
    {
        var host = UniqueHost();
        await SeedLinkAsync(host, "temp", "https://example.com/preserve-method", redirectType: 307);
        var fn = CreateFunction();

        var response = await fn.HandleAsync(BuildRequest(host, "temp"), new StubLambdaContext());

        response.StatusCode.Should().Be(307);
        response.Headers["Location"].Should().Be("https://example.com/preserve-method");
    }

    // ── Error status codes ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_LinkNotFound_Returns404()
    {
        var host = UniqueHost();
        var fn = CreateFunction();

        var response = await fn.HandleAsync(BuildRequest(host, "nonexistent"), new StubLambdaContext());

        response.StatusCode.Should().Be(404);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_ExpiredByTime_Returns410()
    {
        var host = UniqueHost();
        var pastExpiry = DateTime.UtcNow.AddDays(-1).ToString("O");
        await SeedLinkAsync(host, "old", "https://example.com/", expiresAt: pastExpiry);
        var fn = CreateFunction();

        var response = await fn.HandleAsync(BuildRequest(host, "old"), new StubLambdaContext());

        response.StatusCode.Should().Be(410);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_ExpiredByClickLimit_Returns410()
    {
        var host = UniqueHost();
        await SeedLinkAsync(host, "capped", "https://example.com/", maxClicks: 100, currentClicks: 100);
        var fn = CreateFunction();

        var response = await fn.HandleAsync(BuildRequest(host, "capped"), new StubLambdaContext());

        response.StatusCode.Should().Be(410);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_SuspendedLink_Returns451()
    {
        var host = UniqueHost();
        await SeedLinkAsync(host, "suspended", "https://example.com/", status: "suspended");
        var fn = CreateFunction();

        var response = await fn.HandleAsync(BuildRequest(host, "suspended"), new StubLambdaContext());

        response.StatusCode.Should().Be(451);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_QuarantinedLink_Returns403()
    {
        var host = UniqueHost();
        await SeedLinkAsync(host, "quarantined", "https://example.com/", status: "quarantined");
        var fn = CreateFunction();

        var response = await fn.HandleAsync(BuildRequest(host, "quarantined"), new StubLambdaContext());

        response.StatusCode.Should().Be(403);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_DeletedLink_Returns404()
    {
        // Deleted items are treated as missing by the repository — callers see 404, not a special status.
        var host = UniqueHost();
        await SeedLinkAsync(host, "deleted", "https://example.com/", status: "deleted");
        var fn = CreateFunction();

        var response = await fn.HandleAsync(BuildRequest(host, "deleted"), new StubLambdaContext());

        response.StatusCode.Should().Be(404);
    }

    // ── Cache-Control headers ─────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_Redirect301_HasPublicCacheControl()
    {
        var host = UniqueHost();
        await SeedLinkAsync(host, "perm", "https://example.com/", redirectType: 301);
        var fn = CreateFunction();

        var response = await fn.HandleAsync(BuildRequest(host, "perm"), new StubLambdaContext());

        response.Headers["Cache-Control"].Should().Be("public, max-age=3600");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_Redirect302_HasNoStoreCacheControl()
    {
        var host = UniqueHost();
        await SeedLinkAsync(host, "link", "https://example.com/");
        var fn = CreateFunction();

        var response = await fn.HandleAsync(BuildRequest(host, "link"), new StubLambdaContext());

        response.Headers["Cache-Control"].Should().Contain("no-store");
    }

    // ── Security response headers ─────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_AnyResponse_IncludesSecurityHeaders()
    {
        var host = UniqueHost();
        await SeedLinkAsync(host, "link", "https://example.com/");
        var fn = CreateFunction();

        var response = await fn.HandleAsync(BuildRequest(host, "link"), new StubLambdaContext());

        response.Headers["X-Content-Type-Options"].Should().Be("nosniff");
        response.Headers["X-Frame-Options"].Should().Be("DENY");
        response.Headers.Should().ContainKey("Referrer-Policy");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_AnyResponse_IncludesCorrelationId()
    {
        var host = UniqueHost();
        await SeedLinkAsync(host, "link", "https://example.com/");
        var fn = CreateFunction();

        var response = await fn.HandleAsync(BuildRequest(host, "link"), new StubLambdaContext());

        response.Headers["X-Correlation-ID"].Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_RequestWithCorrelationId_PropagatesItToResponse()
    {
        var host = UniqueHost();
        await SeedLinkAsync(host, "link", "https://example.com/");
        var fn = CreateFunction();

        var response = await fn.HandleAsync(
            BuildRequest(host, "link", correlationId: "trace-abc-123"),
            new StubLambdaContext());

        response.Headers["X-Correlation-ID"].Should().Be("trace-abc-123");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_ErrorResponse_IncludesContentSecurityPolicy()
    {
        var host = UniqueHost();
        var fn = CreateFunction();

        var response = await fn.HandleAsync(BuildRequest(host, "missing"), new StubLambdaContext());

        response.Headers.Should().ContainKey("Content-Security-Policy");
    }

    // ── HTML error bodies ─────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_NotFound_BodyIsHtmlDocument()
    {
        var host = UniqueHost();
        var fn = CreateFunction();

        var response = await fn.HandleAsync(BuildRequest(host, "missing"), new StubLambdaContext());

        response.Headers["Content-Type"].Should().Contain("text/html");
        response.Body.Should().StartWith("<!DOCTYPE html>");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_ExpiredByTime_BodyContainsExpiredText()
    {
        var host = UniqueHost();
        var pastExpiry = DateTime.UtcNow.AddDays(-1).ToString("O");
        await SeedLinkAsync(host, "old", "https://example.com/", expiresAt: pastExpiry);
        var fn = CreateFunction();

        var response = await fn.HandleAsync(BuildRequest(host, "old"), new StubLambdaContext());

        response.Body.Should().Contain("Expired");
    }

    // ── Domain config / fallback URL ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_NotFound_WithFallbackUrl_RedirectsToFallback()
    {
        var host = UniqueHost();
        await SeedDomainConfigAsync(host, fallbackUrl: "https://fallback.example.com");
        var fn = CreateFunction();

        var response = await fn.HandleAsync(BuildRequest(host, "missing"), new StubLambdaContext());

        response.StatusCode.Should().Be(302);
        response.Headers["Location"].Should().Be("https://fallback.example.com?error=404");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_Expired_WithFallbackUrl_RedirectsToFallback()
    {
        var host = UniqueHost();
        var pastExpiry = DateTime.UtcNow.AddDays(-1).ToString("O");
        await SeedLinkAsync(host, "old", "https://example.com/", expiresAt: pastExpiry);
        await SeedDomainConfigAsync(host, fallbackUrl: "https://fallback.example.com");
        var fn = CreateFunction();

        var response = await fn.HandleAsync(BuildRequest(host, "old"), new StubLambdaContext());

        response.StatusCode.Should().Be(302);
        response.Headers["Location"].Should().Be("https://fallback.example.com?error=410");
    }

    // ── Click counting ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_ActiveLink_IncrementsClickCount()
    {
        var host = UniqueHost();
        await SeedLinkAsync(host, "counted", "https://example.com/", currentClicks: 0);
        var fn = CreateFunction();

        await fn.HandleAsync(BuildRequest(host, "counted"), new StubLambdaContext());

        var item = await _dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = $"HOST#{host}#SLUG#counted" },
                ["SK"] = new AttributeValue { S = "CONFIG" }
            }
        });
        item.Item["CurrentClicks"].N.Should().Be("1");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_ActiveLinkUnderClickLimit_Redirects()
    {
        var host = UniqueHost();
        await SeedLinkAsync(host, "link", "https://example.com/", maxClicks: 100, currentClicks: 50);
        var fn = CreateFunction();

        var response = await fn.HandleAsync(BuildRequest(host, "link"), new StubLambdaContext());

        response.StatusCode.Should().Be(302);
    }

    // ── Cold start ────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_ColdStart_FirstInvocationSucceeds()
    {
        // A brand-new function instance has no warm state — this mirrors a Lambda cold start.
        var host = UniqueHost();
        await SeedLinkAsync(host, "first-call", "https://example.com/cold-start");
        var fn = CreateFunction();

        var response = await fn.HandleAsync(BuildRequest(host, "first-call"), new StubLambdaContext());

        response.StatusCode.Should().Be(302);
        response.Headers["Location"].Should().Be("https://example.com/cold-start");
    }

    // ── Health check ──────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HandleAsync_HealthPath_Returns200WithJsonBody()
    {
        var fn = CreateFunction();
        var request = new APIGatewayHttpApiV2ProxyRequest
        {
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext
            {
                Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription { Path = "/health" }
            },
            Headers = new Dictionary<string, string> { ["host"] = "health.example.com" },
            QueryStringParameters = new Dictionary<string, string>()
        };

        var response = await fn.HandleAsync(request, new StubLambdaContext());

        response.StatusCode.Should().Be(200);
        response.Headers["Content-Type"].Should().Contain("application/json");
        response.Body.Should().Contain("\"status\"");
    }
}
