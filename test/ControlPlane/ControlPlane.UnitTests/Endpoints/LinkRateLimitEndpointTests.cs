using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;
using ControlPlane.UnitTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ControlPlane.UnitTests.Endpoints;

/// <summary>
/// Tests rate-limiting behaviour on the POST /links endpoint.
/// Uses <see cref="TestLinkRateLimiter"/> to control the rate-limit response;
/// the real <see cref="InMemoryLinkRateLimiter"/> logic is covered by <see cref="LinkRateLimiterTests"/>.
/// </summary>
public sealed class LinkRateLimitEndpointTests : IAsyncDisposable
{
    private readonly RateLimitWebApplicationFactory _factory = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    private string LinksUrl => $"/api/v1/tenants/{_tenantId}/links";
    private string DomainsUrl => $"/api/v1/tenants/{_tenantId}/domains";

    private (HttpClient Client, TestClaimsProvider Claims) AsAdmin() =>
        _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Admin);

    private async Task<Guid> CreateVerifiedDomainAsync(HttpClient client)
    {
        var hostname = $"rl-test-{Guid.NewGuid():N}.example.com";
        var create = await client.PostAsJsonAsync(DomainsUrl,
            new CreateDomainRequest { Hostname = hostname });
        var domain = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();
        await client.PostAsync($"{DomainsUrl}/{domain!.Id}/verify", null);
        return domain.Id;
    }

    // ── Rate limit headers on success ─────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateLink_Success_IncludesRateLimitHeaders()
    {
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);

        var response = await client.PostAsJsonAsync(LinksUrl, new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com",
            RedirectType = "302"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Should().ContainKey("X-RateLimit-Limit");
        response.Headers.Should().ContainKey("X-RateLimit-Remaining");
        response.Headers.Should().ContainKey("X-RateLimit-Reset");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateLink_Success_RateLimitHeadersHavePositiveValues()
    {
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);

        var response = await client.PostAsJsonAsync(LinksUrl, new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com",
            RedirectType = "302"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var limit = int.Parse(response.Headers.GetValues("X-RateLimit-Limit").First());
        var remaining = int.Parse(response.Headers.GetValues("X-RateLimit-Remaining").First());
        var reset = long.Parse(response.Headers.GetValues("X-RateLimit-Reset").First());

        limit.Should().BeGreaterThan(0);
        remaining.Should().BeGreaterThanOrEqualTo(0);
        reset.Should().BeGreaterThan(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    // ── Tenant rate limit exceeded ────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateLink_TenantRateLimitExceeded_Returns429()
    {
        _factory.TestRateLimiter.BlockTenantCheck = true;
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);

        var response = await client.PostAsJsonAsync(LinksUrl, new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com",
            RedirectType = "302"
        });

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateLink_TenantRateLimitExceeded_IncludesRetryAfterHeader()
    {
        _factory.TestRateLimiter.BlockTenantCheck = true;
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);

        var response = await client.PostAsJsonAsync(LinksUrl, new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com",
            RedirectType = "302"
        });

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.Should().ContainKey("Retry-After");
        var retryAfter = int.Parse(response.Headers.GetValues("Retry-After").First());
        retryAfter.Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateLink_TenantRateLimitExceeded_IncludesRateLimitHeaders()
    {
        _factory.TestRateLimiter.BlockTenantCheck = true;
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);

        var response = await client.PostAsJsonAsync(LinksUrl, new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com",
            RedirectType = "302"
        });

        response.Headers.Should().ContainKey("X-RateLimit-Limit");
        response.Headers.Should().ContainKey("X-RateLimit-Remaining");
        response.Headers.Should().ContainKey("X-RateLimit-Reset");
        var remaining = int.Parse(response.Headers.GetValues("X-RateLimit-Remaining").First());
        remaining.Should().Be(0);
    }

    // ── IP rate limit exceeded ─────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateLink_IpRateLimitExceeded_Returns429()
    {
        _factory.TestRateLimiter.BlockIpCheck = true;
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);

        var response = await client.PostAsJsonAsync(LinksUrl, new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com",
            RedirectType = "302"
        });

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.Should().ContainKey("Retry-After");
    }

    // ── Admin bypass ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateLink_PlanBypassUser_NotRateLimited()
    {
        // Even with both checks set to block, plan:bypass skips rate limiting entirely.
        _factory.TestRateLimiter.BlockTenantCheck = true;
        _factory.TestRateLimiter.BlockIpCheck = true;

        _factory.SetPlanBypassClaims(_tenantId.ToString(), Roles.Admin);
        var client = _factory.CreateClient();
        var domainId = await CreateVerifiedDomainAsync(client);

        var response = await client.PostAsJsonAsync(LinksUrl, new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com",
            RedirectType = "302"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── Exempt IP bypass ──────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateLink_ExemptIp_NotRateLimited()
    {
        // Exempt the "unknown" IP (what WebApplicationFactory reports for in-memory requests).
        _factory.TestRateLimiter.BlockTenantCheck = true;
        _factory.TestRateLimiter.ExemptIpsSet.Add("unknown");
        _factory.TestRateLimiter.ExemptIpsSet.Add("127.0.0.1");
        _factory.TestRateLimiter.ExemptIpsSet.Add("::1");

        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);

        var response = await client.PostAsJsonAsync(LinksUrl, new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com",
            RedirectType = "302"
        });

        // Should succeed because the IP is exempt, bypassing tenant check too
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
