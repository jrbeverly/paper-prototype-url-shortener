using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Endpoints;

/// <summary>
/// Tests for the domain deactivate/reactivate lifecycle: link status, AWS resource cleanup,
/// grace period enforcement, audit log entries, and error cases.
/// Uses InMemoryDistributionService and InMemoryCertificateService — no real AWS calls.
/// </summary>
public sealed class DomainDeactivationTests : IAsyncDisposable
{
    private readonly UnitTestWebApplicationFactory _factory = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    private string BaseUrl => $"/api/v1/tenants/{_tenantId}/domains";

    private (HttpClient Client, TestClaimsProvider Claims) AsAdmin() =>
        _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Admin);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<CreateDomainResponse> CreateDomainAsync(HttpClient client, string? hostname = null)
    {
        hostname ??= $"deact-{Guid.NewGuid():N}.example.com";
        var resp = await client.PostAsJsonAsync(BaseUrl, new CreateDomainRequest { Hostname = hostname });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<CreateDomainResponse>())!;
    }

    private async Task VerifyDomainAsync(HttpClient client, Guid domainId)
    {
        _factory.DnsVerification.SetNextResult(TestDnsVerificationService.PassResult("txt", "cname"));
        var resp = await client.PostAsync($"{BaseUrl}/{domainId}/verify", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<DomainDetailResponse> GetDomainAsync(HttpClient client, Guid domainId)
    {
        var resp = await client.GetAsync($"{BaseUrl}/{domainId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<DomainDetailResponse>())!;
    }

    private async Task<Guid> CreateActiveLinkAsync(HttpClient client, Guid domainId)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links",
            new CreateLinkRequest
            {
                DomainId = domainId,
                Slug = $"slug-{Guid.NewGuid():N}",
                DestinationUrl = "https://example.com"
            });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<CreateLinkResponse>();
        return body!.Id;
    }

    // ── DeactivateDomain — happy path ─────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeactivateDomain_ActiveDomain_Returns200WithInactiveStatus()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var resp = await client.PostAsync($"{BaseUrl}/{domain.Id}/deactivate", null);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<DomainDetailResponse>();
        body!.Status.Should().Be("inactive");
        body.DeactivatedAt.Should().NotBeNull();
        body.DeactivatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeactivateDomain_SetsDeactivatedAtTimestamp()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var before = DateTime.UtcNow;
        await client.PostAsync($"{BaseUrl}/{domain.Id}/deactivate", null);
        var after = DateTime.UtcNow;

        var detail = await GetDomainAsync(client, domain.Id);
        detail.DeactivatedAt.Should().NotBeNull();
        detail.DeactivatedAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeactivateDomain_DeactivatesActiveLinks()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);
        var linkId = await CreateActiveLinkAsync(client, domain.Id);

        await client.PostAsync($"{BaseUrl}/{domain.Id}/deactivate", null);

        // GetAllByTenantAsync returns non-deleted links regardless of status.
        var linkRepo = _factory.Services.GetRequiredService<ILinkRepository>();
        var allLinks = await linkRepo.GetAllByTenantAsync(_tenantId);
        var link = allLinks.Single(l => l.Id == linkId);
        link.Status.Should().Be("inactive");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeactivateDomain_LinksSetToInactiveInRepository()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);
        await CreateActiveLinkAsync(client, domain.Id);
        await CreateActiveLinkAsync(client, domain.Id);

        await client.PostAsync($"{BaseUrl}/{domain.Id}/deactivate", null);

        var linkRepo = _factory.Services.GetRequiredService<ILinkRepository>();
        // After deactivation, DeactivateByDomainAsync has set links to "inactive";
        // ReactivateByDomainAsync on a fresh repo returns the count, confirming they were set.
        var reactivated = await linkRepo.ReactivateByDomainAsync(_tenantId, domain.Id);
        reactivated.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeactivateDomain_RemovesDistributionTenant()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);
        var before = await GetDomainAsync(client, domain.Id);
        before.DistributionTenantId.Should().NotBeNull();

        await client.PostAsync($"{BaseUrl}/{domain.Id}/deactivate", null);

        var after = await GetDomainAsync(client, domain.Id);
        after.DistributionTenantId.Should().BeNull();
        after.DistributionTenantStatus.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeactivateDomain_RevokesAndClearsCertificate()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);
        var before = await GetDomainAsync(client, domain.Id);
        before.CertificateArn.Should().NotBeNullOrEmpty();

        await client.PostAsync($"{BaseUrl}/{domain.Id}/deactivate", null);

        var after = await GetDomainAsync(client, domain.Id);
        after.CertificateArn.Should().BeNull();
        after.CertificateStatus.Should().Be("revoked");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeactivateDomain_PendingVerificationDomain_Returns200()
    {
        // Domains that never verified still serve no traffic, but deactivation is allowed
        // so operators have a single consistent way to shut a domain down.
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var resp = await client.PostAsync($"{BaseUrl}/{domain.Id}/deactivate", null);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<DomainDetailResponse>();
        body!.Status.Should().Be("inactive");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeactivateDomain_NoCertificateOrTenant_Returns200()
    {
        // An unverified domain has no certificate or distribution tenant — deactivation must still succeed.
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var resp = await client.PostAsync($"{BaseUrl}/{domain.Id}/deactivate", null);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── DeactivateDomain — error cases ────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeactivateDomain_AlreadyInactive_Returns409()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);
        await client.PostAsync($"{BaseUrl}/{domain.Id}/deactivate", null);

        var resp = await client.PostAsync($"{BaseUrl}/{domain.Id}/deactivate", null);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeactivateDomain_NotFound_Returns404()
    {
        var (client, _) = AsAdmin();

        var resp = await client.PostAsync($"{BaseUrl}/{Guid.NewGuid()}/deactivate", null);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeactivateDomain_Unauthenticated_Returns401()
    {
        // Use a fresh factory with no claims configured to get a truly unauthenticated client.
        await using var factory = new UnitTestWebApplicationFactory();
        var unauthClient = factory.CreateClient();

        var resp = await unauthClient.PostAsync(
            $"/api/v1/tenants/{Guid.NewGuid()}/domains/{Guid.NewGuid()}/deactivate", null);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── ReactivateDomain — happy path ─────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ReactivateDomain_InactiveDomain_Returns200WithActiveStatus()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);
        await client.PostAsync($"{BaseUrl}/{domain.Id}/deactivate", null);

        var resp = await client.PostAsync($"{BaseUrl}/{domain.Id}/reactivate", null);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<DomainDetailResponse>();
        body!.Status.Should().Be("active");
        body.DeactivatedAt.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ReactivateDomain_RestoresInactiveLinks()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);
        await CreateActiveLinkAsync(client, domain.Id);
        await CreateActiveLinkAsync(client, domain.Id);
        await client.PostAsync($"{BaseUrl}/{domain.Id}/deactivate", null);

        await client.PostAsync($"{BaseUrl}/{domain.Id}/reactivate", null);

        var linkRepo = _factory.Services.GetRequiredService<ILinkRepository>();
        // After reactivation the links are "active" again; a second DeactivateByDomain call
        // should find both links again.
        var deactivated = await linkRepo.DeactivateByDomainAsync(_tenantId, domain.Id);
        deactivated.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ReactivateDomain_ReprovisionsCertificate()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);
        await client.PostAsync($"{BaseUrl}/{domain.Id}/deactivate", null);

        await client.PostAsync($"{BaseUrl}/{domain.Id}/reactivate", null);

        var detail = await GetDomainAsync(client, domain.Id);
        detail.CertificateArn.Should().NotBeNullOrEmpty();
        detail.CertificateStatus.Should().NotBe("revoked");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ReactivateDomain_RecreatesDistributionTenant()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);
        await client.PostAsync($"{BaseUrl}/{domain.Id}/deactivate", null);

        await client.PostAsync($"{BaseUrl}/{domain.Id}/reactivate", null);

        var detail = await GetDomainAsync(client, domain.Id);
        detail.DistributionTenantId.Should().NotBeNullOrEmpty();
        detail.DistributionTenantStatus.Should().Be("InProgress");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ReactivateDomain_ClearsDeactivatedAt()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);
        await client.PostAsync($"{BaseUrl}/{domain.Id}/deactivate", null);

        await client.PostAsync($"{BaseUrl}/{domain.Id}/reactivate", null);

        var detail = await GetDomainAsync(client, domain.Id);
        detail.DeactivatedAt.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ReactivateDomain_PersistsActiveStatusInRepository()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);
        await client.PostAsync($"{BaseUrl}/{domain.Id}/deactivate", null);

        await client.PostAsync($"{BaseUrl}/{domain.Id}/reactivate", null);

        var repo = _factory.Services.GetRequiredService<IDomainRepository>();
        var stored = await repo.GetByIdAsync(_tenantId, domain.Id);
        stored!.Status.Should().Be("active");
        stored.DeactivatedAt.Should().BeNull();
    }

    // ── ReactivateDomain — error cases ────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ReactivateDomain_ActiveDomain_Returns409()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var resp = await client.PostAsync($"{BaseUrl}/{domain.Id}/reactivate", null);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ReactivateDomain_PendingVerificationDomain_Returns409()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var resp = await client.PostAsync($"{BaseUrl}/{domain.Id}/reactivate", null);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ReactivateDomain_NotFound_Returns404()
    {
        var (client, _) = AsAdmin();

        var resp = await client.PostAsync($"{BaseUrl}/{Guid.NewGuid()}/reactivate", null);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ReactivateDomain_Unauthenticated_Returns401()
    {
        // Use a fresh factory with no claims configured to get a truly unauthenticated client.
        await using var factory = new UnitTestWebApplicationFactory();
        var unauthClient = factory.CreateClient();

        var resp = await unauthClient.PostAsync(
            $"/api/v1/tenants/{Guid.NewGuid()}/domains/{Guid.NewGuid()}/reactivate", null);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ReactivateDomain_ExpiredGracePeriod_Returns409()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);
        await client.PostAsync($"{BaseUrl}/{domain.Id}/deactivate", null);

        // Backdate DeactivatedAt beyond the 30-day grace period via the repository.
        var repo = _factory.Services.GetRequiredService<IDomainRepository>();
        var entity = await repo.GetByIdAsync(_tenantId, domain.Id);
        await repo.UpdateAsync(entity! with { DeactivatedAt = DateTime.UtcNow - TimeSpan.FromDays(31) });

        var resp = await client.PostAsync($"{BaseUrl}/{domain.Id}/reactivate", null);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── Deactivate → reactivate round-trip ───────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeactivateThenReactivate_FullRoundTrip_DomainServesTrafficAgain()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);
        await CreateActiveLinkAsync(client, domain.Id);

        await client.PostAsync($"{BaseUrl}/{domain.Id}/deactivate", null);
        var inactive = await GetDomainAsync(client, domain.Id);
        inactive.Status.Should().Be("inactive");

        await client.PostAsync($"{BaseUrl}/{domain.Id}/reactivate", null);
        var active = await GetDomainAsync(client, domain.Id);
        active.Status.Should().Be("active");
        active.DistributionTenantId.Should().NotBeNullOrEmpty();
        active.CertificateArn.Should().NotBeNullOrEmpty();
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
