using Amazon.Lambda.APIGatewayEvents;
using RedirectService.Api.Functions;

namespace RedirectService.Tests.Functions;

public sealed class RedirectFunctionFallbackTests
{
    private const string _hostname = "go.example.com";
    private const string _slug = "test-slug";

    private static RedirectFunction CreateFunction(
        StubRedirectService service,
        FakeDomainConfigRepository? configRepo = null)
        => new(new StubDynamoDB(), service, configRepo);

    private static APIGatewayHttpApiV2ProxyRequest BuildRequest(
        string host = _hostname,
        string path = "/" + _slug)
        => new()
        {
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext
            {
                Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription { Path = path }
            },
            Headers = new Dictionary<string, string> { ["host"] = host },
            QueryStringParameters = new Dictionary<string, string>()
        };

    // ── HTML response structure ───────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_NotFound_Returns404WithHtmlContentType()
    {
        var service = new StubRedirectService();
        service.Returns(RedirectResult.NotFound());
        var fn = CreateFunction(service);

        var response = await fn.HandleAsync(BuildRequest(), new StubLambdaContext());

        response.StatusCode.Should().Be(404);
        response.Headers["Content-Type"].Should().Contain("text/html");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_NotFound_BodyIsHtmlDocument()
    {
        var service = new StubRedirectService();
        service.Returns(RedirectResult.NotFound());
        var fn = CreateFunction(service);

        var response = await fn.HandleAsync(BuildRequest(), new StubLambdaContext());

        response.Body.Should().StartWith("<!DOCTYPE html>");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_Expired_Returns410WithHtml()
    {
        var service = new StubRedirectService();
        service.Returns(RedirectResult.Expired("time"));
        var fn = CreateFunction(service);

        var response = await fn.HandleAsync(BuildRequest(), new StubLambdaContext());

        response.StatusCode.Should().Be(410);
        response.Headers["Content-Type"].Should().Contain("text/html");
        response.Body.Should().Contain("Expired");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_Suspended_Returns451WithHtml()
    {
        var service = new StubRedirectService();
        service.Returns(RedirectResult.Suspended());
        var fn = CreateFunction(service);

        var response = await fn.HandleAsync(BuildRequest(), new StubLambdaContext());

        response.StatusCode.Should().Be(451);
        response.Headers["Content-Type"].Should().Contain("text/html");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_Quarantined_Returns403WithHtml()
    {
        var service = new StubRedirectService();
        service.Returns(RedirectResult.Quarantined());
        var fn = CreateFunction(service);

        var response = await fn.HandleAsync(BuildRequest(), new StubLambdaContext());

        response.StatusCode.Should().Be(403);
        response.Headers["Content-Type"].Should().Contain("text/html");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_ErrorResponse_IncludesContentSecurityPolicy()
    {
        var service = new StubRedirectService();
        service.Returns(RedirectResult.NotFound());
        var fn = CreateFunction(service);

        var response = await fn.HandleAsync(BuildRequest(), new StubLambdaContext());

        response.Headers.Should().ContainKey("Content-Security-Policy");
    }

    // ── Fallback redirect ─────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_NotFound_WithFallbackUrl_Returns302ToFallback()
    {
        var service = new StubRedirectService();
        service.Returns(RedirectResult.NotFound());
        var configRepo = new FakeDomainConfigRepository();
        configRepo.Returns(new DomainConfig
        {
            Hostname = _hostname,
            FallbackUrl = "https://example.com"
        });
        var fn = CreateFunction(service, configRepo);

        var response = await fn.HandleAsync(BuildRequest(), new StubLambdaContext());

        response.StatusCode.Should().Be(302);
        response.Headers["Location"].Should().Be("https://example.com?error=404");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_Expired_WithFallbackUrl_Returns302ToFallback()
    {
        var service = new StubRedirectService();
        service.Returns(RedirectResult.Expired("time"));
        var configRepo = new FakeDomainConfigRepository();
        configRepo.Returns(new DomainConfig
        {
            Hostname = _hostname,
            FallbackUrl = "https://example.com"
        });
        var fn = CreateFunction(service, configRepo);

        var response = await fn.HandleAsync(BuildRequest(), new StubLambdaContext());

        response.StatusCode.Should().Be(302);
        response.Headers["Location"].Should().Be("https://example.com?error=410");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_WithFallbackUrlAlreadyHavingQueryString_AppendsErrorWithAmpersand()
    {
        var service = new StubRedirectService();
        service.Returns(RedirectResult.NotFound());
        var configRepo = new FakeDomainConfigRepository();
        configRepo.Returns(new DomainConfig
        {
            Hostname = _hostname,
            FallbackUrl = "https://example.com/404?from=link"
        });
        var fn = CreateFunction(service, configRepo);

        var response = await fn.HandleAsync(BuildRequest(), new StubLambdaContext());

        response.Headers["Location"].Should().Be("https://example.com/404?from=link&error=404");
    }

    // ── Branding in HTML ──────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_NotFound_WithBrandColor_HtmlContainsBrandColor()
    {
        var service = new StubRedirectService();
        service.Returns(RedirectResult.NotFound());
        var configRepo = new FakeDomainConfigRepository();
        configRepo.Returns(new DomainConfig { Hostname = _hostname, BrandColor = "#e63946" });
        var fn = CreateFunction(service, configRepo);

        var response = await fn.HandleAsync(BuildRequest(), new StubLambdaContext());

        response.Body.Should().Contain("#e63946");
    }

    // ── Error tolerance ───────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_DomainConfigLookupThrows_StillReturnsHtmlPage()
    {
        var service = new StubRedirectService();
        service.Returns(RedirectResult.NotFound());
        var configRepo = new FakeDomainConfigRepository();
        configRepo.Throws(new InvalidOperationException("DynamoDB unavailable"));
        var fn = CreateFunction(service, configRepo);

        var response = await fn.HandleAsync(BuildRequest(), new StubLambdaContext());

        response.StatusCode.Should().Be(404);
        response.Body.Should().StartWith("<!DOCTYPE html>");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_DomainConfigLookupThrows_LogsWarning()
    {
        var service = new StubRedirectService();
        service.Returns(RedirectResult.NotFound());
        var configRepo = new FakeDomainConfigRepository();
        configRepo.Throws(new InvalidOperationException("DynamoDB unavailable"));
        var context = new StubLambdaContext();
        var fn = CreateFunction(service, configRepo);

        await fn.HandleAsync(BuildRequest(), context);

        context.Warnings.Should().NotBeEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_NullDomainConfigRepo_Returns404WithHtml()
    {
        var service = new StubRedirectService();
        service.Returns(RedirectResult.NotFound());
        // null domain config repo — simulates test constructor with default
        var fn = CreateFunction(service, configRepo: null);

        var response = await fn.HandleAsync(BuildRequest(), new StubLambdaContext());

        response.StatusCode.Should().Be(404);
        response.Body.Should().StartWith("<!DOCTYPE html>");
    }

    // ── Domain config not consulted on successful redirect ────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_SuccessfulRedirect_DoesNotLookUpDomainConfig()
    {
        var service = new StubRedirectService();
        service.Returns(RedirectResult.Redirect("https://destination.com", 302));
        var configRepo = new FakeDomainConfigRepository();
        var fn = CreateFunction(service, configRepo);

        await fn.HandleAsync(BuildRequest(), new StubLambdaContext());

        configRepo.GetCallCount.Should().Be(0);
    }

    // ── Branding config caching ───────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_SecondErrorCallSameHost_UsesCachedConfig()
    {
        var service = new StubRedirectService();
        service.Returns(RedirectResult.NotFound());
        var configRepo = new FakeDomainConfigRepository();
        configRepo.Returns(new DomainConfig { Hostname = _hostname, BrandColor = "#3498db" });
        var fn = CreateFunction(service, configRepo);

        // First call populates the cache.
        await fn.HandleAsync(BuildRequest(), new StubLambdaContext());
        // Second call must be served from cache, not the repository.
        await fn.HandleAsync(BuildRequest(), new StubLambdaContext());

        configRepo.GetCallCount.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_SecondErrorCallSameHost_NullConfig_UsesCachedNull()
    {
        // Negative caching: if the host has no config, that fact should also be cached
        // so repeated errors don't hit DynamoDB every time.
        var service = new StubRedirectService();
        service.Returns(RedirectResult.NotFound());
        var configRepo = new FakeDomainConfigRepository();
        configRepo.Returns(null); // no config for this domain
        var fn = CreateFunction(service, configRepo);

        await fn.HandleAsync(BuildRequest(), new StubLambdaContext());
        await fn.HandleAsync(BuildRequest(), new StubLambdaContext());

        configRepo.GetCallCount.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_ErrorCallsDifferentHosts_FetchesSeparately()
    {
        var service = new StubRedirectService();
        service.Returns(RedirectResult.NotFound());
        var configRepo = new FakeDomainConfigRepository();
        configRepo.Returns(new DomainConfig { Hostname = "host-a.example.com" });
        var fn = CreateFunction(service, configRepo);

        await fn.HandleAsync(BuildRequest(host: "host-a.example.com"), new StubLambdaContext());
        await fn.HandleAsync(BuildRequest(host: "host-b.example.com"), new StubLambdaContext());

        configRepo.GetCallCount.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_DomainConfigThrows_DoesNotCacheResult()
    {
        // A transient DynamoDB failure must not permanently suppress future config lookups.
        var service = new StubRedirectService();
        service.Returns(RedirectResult.NotFound());
        var configRepo = new FakeDomainConfigRepository();
        configRepo.Throws(new InvalidOperationException("DynamoDB unavailable"));
        var fn = CreateFunction(service, configRepo);

        await fn.HandleAsync(BuildRequest(), new StubLambdaContext());
        await fn.HandleAsync(BuildRequest(), new StubLambdaContext());

        configRepo.GetCallCount.Should().Be(2);
    }
}
