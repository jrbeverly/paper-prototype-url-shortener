using Amazon.Lambda.APIGatewayEvents;
using RedirectService.Api.Functions;

namespace RedirectService.Tests.Functions;

/// <summary>
/// Unit tests for rate limiting behaviour in <see cref="RedirectFunction"/>.
/// Uses <see cref="FakeRedirectRateLimiter"/> and <see cref="StubRedirectService"/>
/// so no DynamoDB access is required.
/// </summary>
public sealed class RedirectFunctionRateLimitTests
{
    private static RedirectFunction CreateFunction(FakeRedirectRateLimiter rateLimiter)
    {
        var dynamoDb = new StubDynamoDB();
        var service = new StubRedirectService();
        service.Returns(RedirectResult.Redirect("https://example.com/dest", 302));
        return new RedirectFunction(dynamoDb, service, rateLimiter: rateLimiter);
    }

    private static APIGatewayHttpApiV2ProxyRequest BuildRequest(
        string host = "go.example.com",
        string slug = "test",
        string? clientIp = null,
        string? userAgent = null,
        string? correlationId = null)
    {
        var headers = new Dictionary<string, string> { ["host"] = host };
        if (clientIp is not null)
            headers["cloudfront-viewer-address"] = $"{clientIp}:12345";
        if (userAgent is not null)
            headers["user-agent"] = userAgent;
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

    // ── Normal request (no rate limiting triggered) ───────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_UnderLimit_ReturnsRedirect()
    {
        var rateLimiter = new FakeRedirectRateLimiter();
        var fn = CreateFunction(rateLimiter);

        var response = await fn.HandleAsync(BuildRequest(), new StubLambdaContext());

        response.StatusCode.Should().Be(302);
    }

    // ── IP rate limit exceeded ────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_IpLimitExceeded_Returns429()
    {
        var rateLimiter = new FakeRedirectRateLimiter { BlockIpCheck = true };
        var fn = CreateFunction(rateLimiter);

        var response = await fn.HandleAsync(BuildRequest(clientIp: "1.2.3.4"), new StubLambdaContext());

        response.StatusCode.Should().Be(429);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_IpLimitExceeded_IncludesRetryAfterHeader()
    {
        var rateLimiter = new FakeRedirectRateLimiter { BlockIpCheck = true };
        var fn = CreateFunction(rateLimiter);

        var response = await fn.HandleAsync(BuildRequest(clientIp: "1.2.3.4"), new StubLambdaContext());

        response.Headers.Should().ContainKey("Retry-After");
        int.Parse(response.Headers["Retry-After"]).Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_IpLimitExceeded_IncludesRateLimitHeaders()
    {
        var rateLimiter = new FakeRedirectRateLimiter { BlockIpCheck = true };
        var fn = CreateFunction(rateLimiter);

        var response = await fn.HandleAsync(BuildRequest(clientIp: "1.2.3.4"), new StubLambdaContext());

        response.Headers.Should().ContainKey("X-RateLimit-Limit");
        response.Headers.Should().ContainKey("X-RateLimit-Remaining");
        response.Headers.Should().ContainKey("X-RateLimit-Reset");
        response.Headers["X-RateLimit-Remaining"].Should().Be("0");
    }

    // ── Domain rate limit exceeded ────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_DomainLimitExceeded_Returns429()
    {
        var rateLimiter = new FakeRedirectRateLimiter { BlockDomainCheck = true };
        var fn = CreateFunction(rateLimiter);

        var response = await fn.HandleAsync(BuildRequest(), new StubLambdaContext());

        response.StatusCode.Should().Be(429);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_DomainLimitExceeded_IncludesRetryAfterHeader()
    {
        var rateLimiter = new FakeRedirectRateLimiter { BlockDomainCheck = true };
        var fn = CreateFunction(rateLimiter);

        var response = await fn.HandleAsync(BuildRequest(), new StubLambdaContext());

        response.Headers.Should().ContainKey("Retry-After");
        int.Parse(response.Headers["Retry-After"]).Should().BeGreaterThan(0);
    }

    // ── IP check takes priority over domain check ─────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_IpBlockedAndDomainAllowed_Returns429ForIp()
    {
        var rateLimiter = new FakeRedirectRateLimiter { BlockIpCheck = true, BlockDomainCheck = false };
        var fn = CreateFunction(rateLimiter);

        var response = await fn.HandleAsync(BuildRequest(clientIp: "1.2.3.4"), new StubLambdaContext());

        response.StatusCode.Should().Be(429);
    }

    // ── Security headers present on 429 ──────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_RateLimited_IncludesSecurityHeaders()
    {
        var rateLimiter = new FakeRedirectRateLimiter { BlockIpCheck = true };
        var fn = CreateFunction(rateLimiter);

        var response = await fn.HandleAsync(BuildRequest(), new StubLambdaContext());

        response.Headers["X-Content-Type-Options"].Should().Be("nosniff");
        response.Headers["X-Frame-Options"].Should().Be("DENY");
        response.Headers.Should().ContainKey("X-Correlation-ID");
    }

    // ── Correlation ID is propagated ──────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_RateLimited_PropagatesCorrelationId()
    {
        var rateLimiter = new FakeRedirectRateLimiter { BlockIpCheck = true };
        var fn = CreateFunction(rateLimiter);

        var response = await fn.HandleAsync(
            BuildRequest(correlationId: "trace-xyz"), new StubLambdaContext());

        response.Headers["X-Correlation-ID"].Should().Be("trace-xyz");
    }

    // ── Exempt IP bypasses rate limiting ──────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_ExemptIp_SkipsRateLimitAndReturnsRedirect()
    {
        var rateLimiter = new FakeRedirectRateLimiter
        {
            BlockIpCheck = true,
            BlockDomainCheck = true
        };
        rateLimiter.ExemptIpSet.Add("10.0.0.1");
        var fn = CreateFunction(rateLimiter);

        var response = await fn.HandleAsync(BuildRequest(clientIp: "10.0.0.1"), new StubLambdaContext());

        response.StatusCode.Should().Be(302);
    }

    // ── Exempt user-agent bypasses rate limiting ──────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_ExemptUserAgent_SkipsRateLimitAndReturnsRedirect()
    {
        var rateLimiter = new FakeRedirectRateLimiter
        {
            BlockIpCheck = true,
            BlockDomainCheck = true
        };
        rateLimiter.ExemptUserAgentPrefixSet.Add("Googlebot");
        var fn = CreateFunction(rateLimiter);

        var response = await fn.HandleAsync(
            BuildRequest(userAgent: "Googlebot/2.1 (+http://www.google.com/bot.html)"),
            new StubLambdaContext());

        response.StatusCode.Should().Be(302);
    }

    // ── Rate limit events are logged ──────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_IpLimitExceeded_LogsWarning()
    {
        var rateLimiter = new FakeRedirectRateLimiter { BlockIpCheck = true };
        var fn = CreateFunction(rateLimiter);
        var context = new StubLambdaContext();

        await fn.HandleAsync(BuildRequest(clientIp: "1.2.3.4"), context);

        context.Warnings.Should().ContainSingle(w => w.Contains("Rate limited by IP"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_DomainLimitExceeded_LogsWarning()
    {
        var rateLimiter = new FakeRedirectRateLimiter { BlockDomainCheck = true };
        var fn = CreateFunction(rateLimiter);
        var context = new StubLambdaContext();

        await fn.HandleAsync(BuildRequest(), context);

        context.Warnings.Should().ContainSingle(w => w.Contains("Rate limited by domain"));
    }

    // ── Client IP extraction from CloudFront header ───────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_CloudFrontViewerAddress_StripPortForRateLimiting()
    {
        // FakeRedirectRateLimiter always allows, so this just verifies the function runs without error.
        var rateLimiter = new FakeRedirectRateLimiter();
        var fn = CreateFunction(rateLimiter);
        var request = BuildRequest();
        request.Headers["cloudfront-viewer-address"] = "203.0.113.5:54321";

        var response = await fn.HandleAsync(request, new StubLambdaContext());

        response.StatusCode.Should().Be(302);
    }
}
