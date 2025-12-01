using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;
using ControlPlane.UnitTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.UnitTests.Endpoints;

/// <summary>
/// Tests URL safety scanning behaviour on the POST /links endpoint.
/// Uses <see cref="TestUrlSafetyService"/> via <see cref="UnitTestWebApplicationFactory.UrlSafety"/>
/// to control scan outcomes; the real <see cref="InMemoryUrlSafetyService"/> is covered by
/// <see cref="Services.UrlSafetyServiceTests"/>.
/// </summary>
public sealed class LinkUrlSafetyEndpointTests : IAsyncDisposable
{
    private readonly UnitTestWebApplicationFactory _factory = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    private string LinksUrl => $"/api/v1/tenants/{_tenantId}/links";
    private string DomainsUrl => $"/api/v1/tenants/{_tenantId}/domains";

    private (HttpClient Client, TestClaimsProvider Claims) AsAdmin()
    {
        var claimsProvider = _factory.Services.GetRequiredService<TestClaimsProvider>();
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(_tenantId.ToString(), Roles.Admin));
        return (_factory.CreateClient(), claimsProvider);
    }

    private async Task<Guid> CreateVerifiedDomainAsync(HttpClient client)
    {
        var hostname = $"us-{Guid.NewGuid():N}.example.com";
        var create = await client.PostAsJsonAsync(DomainsUrl,
            new CreateDomainRequest { Hostname = hostname });
        var domain = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();
        await client.PostAsync($"{DomainsUrl}/{domain!.Id}/verify", null);
        return domain.Id;
    }

    // ── Safe URL ────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateLink_SafeUrl_Returns201()
    {
        _factory.UrlSafety.Verdict = UrlSafetyVerdict.Safe;
        _factory.UrlSafety.Reason = "No threats detected";
        _factory.UrlSafety.Source = "local";

        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);

        var response = await client.PostAsJsonAsync(LinksUrl, new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com/safe",
            RedirectType = "302"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("active");
        body.SafetyWarning.Should().BeNull();
    }

    // ── Malicious URL → 422 ─────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateLink_MaliciousUrl_Returns422()
    {
        _factory.UrlSafety.Verdict = UrlSafetyVerdict.Malicious;
        _factory.UrlSafety.Reason = "Host matches blocklist: malware.example.com";
        _factory.UrlSafety.Source = "blocklist";

        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);

        var response = await client.PostAsJsonAsync(LinksUrl, new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://malware.example.com/bad",
            RedirectType = "302"
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── Suspicious URL → quarantined ────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateLink_SuspiciousUrl_CreatesQuarantined()
    {
        _factory.UrlSafety.Verdict = UrlSafetyVerdict.Suspicious;
        _factory.UrlSafety.Reason = "Host matches suspicious pattern: free-offer";
        _factory.UrlSafety.Source = "suspicious_patterns";

        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);

        var response = await client.PostAsJsonAsync(LinksUrl, new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://free-offer-deals.example.com",
            RedirectType = "302"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("quarantined");
        body.SafetyWarning.Should().NotBeNull();
        body.SafetyWarning!.Verdict.Should().Be("Suspicious");
        body.SafetyWarning.Reason.Should().Contain("free-offer");
        body.SafetyWarning.Source.Should().Be("suspicious_patterns");
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
