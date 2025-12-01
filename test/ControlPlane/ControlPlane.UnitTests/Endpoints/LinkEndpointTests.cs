using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;

namespace ControlPlane.UnitTests.Endpoints;

public sealed class LinkEndpointTests : IAsyncDisposable
{
    private readonly UnitTestWebApplicationFactory _factory = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    private string LinksUrl => $"/api/v1/tenants/{_tenantId}/links";
    private string DomainsUrl => $"/api/v1/tenants/{_tenantId}/domains";

    private (HttpClient Client, TestClaimsProvider Claims) AsAdmin() =>
        _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Admin);

    private async Task<Guid> CreateVerifiedDomainAsync(HttpClient client)
    {
        var hostname = $"unit-{Guid.NewGuid():N}.example.com";
        var create = await client.PostAsJsonAsync(DomainsUrl,
            new CreateDomainRequest { Hostname = hostname });
        var domain = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();
        // Default DNS service passes — verify it
        await client.PostAsync($"{DomainsUrl}/{domain!.Id}/verify", null);
        return domain.Id;
    }

    private async Task<CreateLinkResponse> CreateLinkAsync(HttpClient client, Guid domainId, string? slug = null)
    {
        var request = new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com/target",
            Slug = slug,
            RedirectType = "302"
        };
        var response = await client.PostAsJsonAsync(LinksUrl, request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CreateLinkResponse>())!;
    }

    // ── CreateLink ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateLink_ValidRequest_Returns201WithBody()
    {
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);

        var response = await client.PostAsJsonAsync(LinksUrl, new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com/landing",
            Slug = "my-slug",
            RedirectType = "301"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body.Should().NotBeNull();
        body!.Id.Should().NotBeEmpty();
        body.Slug.Should().Be("my-slug");
        body.DestinationUrl.Should().Be("https://example.com/landing");
        body.RedirectType.Should().Be("301");
        body.ShortUrl.Should().StartWith("https://");
        body.ShortUrl.Should().Contain("/my-slug");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateLink_NoSlugProvided_AutoGeneratesSlug()
    {
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);

        var body = await CreateLinkAsync(client, domainId);

        body.Slug.Should().NotBeNullOrEmpty();
        body.Slug.Should().MatchRegex(@"^[a-zA-Z0-9]+$");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateLink_DomainNotFound_Returns404()
    {
        var (client, _) = AsAdmin();

        var response = await client.PostAsJsonAsync(LinksUrl, new CreateLinkRequest
        {
            DomainId = Guid.NewGuid(),
            DestinationUrl = "https://example.com"
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateLink_DomainNotActive_Returns400()
    {
        var (client, _) = AsAdmin();
        // Create domain but do NOT verify it → stays pending_verification
        var domain = await (await client.PostAsJsonAsync(DomainsUrl,
            new CreateDomainRequest { Hostname = $"notactive-{Guid.NewGuid():N}.example.com" }))
            .Content.ReadFromJsonAsync<CreateDomainResponse>();

        var response = await client.PostAsJsonAsync(LinksUrl, new CreateLinkRequest
        {
            DomainId = domain!.Id,
            DestinationUrl = "https://example.com"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateLink_DuplicateSlug_Returns409()
    {
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);
        await CreateLinkAsync(client, domainId, "unique-slug");

        var response = await client.PostAsJsonAsync(LinksUrl, new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://other.example.com",
            Slug = "unique-slug"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateLink_ExceedsPerDomainPlanLimit_Returns402()
    {
        // Seed a tenant with MaxLinksPerDomain=3. Grace limit = ceil(3.3) = 4.
        // Create 4 links on one domain (including 1 grace slot), then the 5th must be blocked.
        await _factory.SeedTenantAsync(_tenantId, maxLinksPerDomain: 3);
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);
        for (int i = 0; i < 4; i++)
            await CreateLinkAsync(client, domainId);

        var response = await client.PostAsJsonAsync(LinksUrl, new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://overflow.example.com"
        });

        response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("upgradeUrl").GetString().Should().Be("https://short.io/pricing");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateLink_LimitIsPerDomain_NotCrossedByLinksOnOtherDomains()
    {
        // Limits are per-domain; links on a second domain should not count against the first.
        await _factory.SeedTenantAsync(_tenantId, maxLinksPerDomain: 3);
        var (client, _) = AsAdmin();
        var domain1 = await CreateVerifiedDomainAsync(client);
        var domain2 = await CreateVerifiedDomainAsync(client);

        // Fill domain1 to its grace limit
        for (int i = 0; i < 4; i++)
            await CreateLinkAsync(client, domain1);

        // Creating a link on domain2 must still succeed
        var response = await client.PostAsJsonAsync(LinksUrl, new CreateLinkRequest
        {
            DomainId = domain2,
            DestinationUrl = "https://domain2.example.com"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateLink_WithPlanBypassPermission_IgnoresLimit()
    {
        // Seed a tenant with MaxLinksPerDomain=1. Grace limit = ceil(1.1) = 2. Fill up to grace.
        await _factory.SeedTenantAsync(_tenantId, maxLinksPerDomain: 1);
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);
        for (int i = 0; i < 2; i++)
            await CreateLinkAsync(client, domainId);

        // Confirm the limit is now enforced for a regular admin
        var blocked = await client.PostAsJsonAsync(LinksUrl, new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://blocked.example.com"
        });
        blocked.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);

        // A principal with plan:bypass can still create
        _factory.SetPlanBypassClaims(_tenantId.ToString(), Roles.Admin);
        var bypassed = await client.PostAsJsonAsync(LinksUrl, new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://bypassed.example.com"
        });
        bypassed.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateLink_InvalidDestinationUrl_Returns400()
    {
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);

        var response = await client.PostAsJsonAsync(LinksUrl, new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "not-a-url"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateLink_InvalidRedirectType_Returns400()
    {
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);

        var response = await client.PostAsJsonAsync(LinksUrl, new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com",
            RedirectType = "303"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── GetLink ────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetLink_ExistingLink_Returns200WithDetail()
    {
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);
        var created = await CreateLinkAsync(client, domainId, "get-me");

        var response = await client.GetAsync($"{LinksUrl}/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LinkDetailResponse>();
        body!.Id.Should().Be(created.Id);
        body.Slug.Should().Be("get-me");
        body.Analytics.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetLink_NotFound_Returns404()
    {
        var (client, _) = AsAdmin();

        var response = await client.GetAsync($"{LinksUrl}/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── ListLinks ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListLinks_EmptyTenant_ReturnsEmptyPage()
    {
        var (client, _) = AsAdmin();

        var response = await client.GetAsync(LinksUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LinkListResponse>();
        body!.Items.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListLinks_AfterCreation_ContainsLink()
    {
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);
        await CreateLinkAsync(client, domainId, "listed-link");

        var response = await client.GetAsync(LinksUrl);

        var body = await response.Content.ReadFromJsonAsync<LinkListResponse>();
        body!.Items.Should().ContainSingle(l => l.Slug == "listed-link");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListLinks_LimitOutOfRange_Returns400()
    {
        var (client, _) = AsAdmin();

        var response = await client.GetAsync($"{LinksUrl}?limit=0");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListLinks_InvalidSortField_Returns400()
    {
        var (client, _) = AsAdmin();

        var response = await client.GetAsync($"{LinksUrl}?sort=invalid_sort_field");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── UpdateLink ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateLink_ChangeDestinationUrl_Returns200()
    {
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);
        var created = await CreateLinkAsync(client, domainId, "update-me");

        var update = new UpdateLinkRequest { DestinationUrl = "https://new-destination.example.com" };
        var response = await client.PatchAsJsonAsync($"{LinksUrl}/{created.Id}", update);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body!.DestinationUrl.Should().Be("https://new-destination.example.com");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateLink_VersionIncrements()
    {
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);
        var created = await CreateLinkAsync(client, domainId, "version-check");

        var update = new UpdateLinkRequest { DestinationUrl = "https://v2.example.com" };
        var updated = await (await client.PatchAsJsonAsync($"{LinksUrl}/{created.Id}", update))
            .Content.ReadFromJsonAsync<CreateLinkResponse>();

        updated!.Version.Should().BeGreaterThan(created.Version);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateLink_NotFound_Returns404()
    {
        var (client, _) = AsAdmin();

        var response = await client.PatchAsJsonAsync(
            $"{LinksUrl}/{Guid.NewGuid()}", new UpdateLinkRequest());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateLink_InvalidDestinationUrl_Returns400()
    {
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);
        var created = await CreateLinkAsync(client, domainId, "update-validation");

        var response = await client.PatchAsJsonAsync($"{LinksUrl}/{created.Id}",
            new UpdateLinkRequest { DestinationUrl = "not-a-url" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── DeleteLink ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteLink_SoftDelete_Returns204()
    {
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);
        var created = await CreateLinkAsync(client, domainId, "soft-delete-me");

        var response = await client.DeleteAsync($"{LinksUrl}/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteLink_Permanent_Returns204()
    {
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);
        var created = await CreateLinkAsync(client, domainId, "hard-delete-me");

        var response = await client.DeleteAsync($"{LinksUrl}/{created.Id}?permanent=true");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteLink_NotFound_Returns404()
    {
        var (client, _) = AsAdmin();

        var response = await client.DeleteAsync($"{LinksUrl}/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── RestoreLink ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RestoreLink_SoftDeletedLink_Returns200WithRestoredLink()
    {
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);
        var created = await CreateLinkAsync(client, domainId, "restore-me");
        await client.DeleteAsync($"{LinksUrl}/{created.Id}");

        var response = await client.PostAsync($"{LinksUrl}/{created.Id}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body!.Id.Should().Be(created.Id);
        body.Slug.Should().Be("restore-me");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RestoreLink_NonDeletedLink_Returns404()
    {
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);
        var created = await CreateLinkAsync(client, domainId, "not-deleted");

        // Attempt to restore a link that was never deleted
        var response = await client.PostAsync($"{LinksUrl}/{created.Id}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── BulkCreateLinks ───────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BulkCreateLinks_ValidLinks_Returns200WithResults()
    {
        var (client, _) = AsAdmin();
        var domainId = await CreateVerifiedDomainAsync(client);

        var request = new BulkCreateLinksRequest
        {
            Links =
            [
                new CreateLinkRequest { DomainId = domainId, DestinationUrl = "https://a.example.com" },
                new CreateLinkRequest { DomainId = domainId, DestinationUrl = "https://b.example.com" }
            ]
        };

        var response = await client.PostAsJsonAsync($"{LinksUrl}/bulk", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BulkCreateLinksResponse>();
        body!.TotalRequested.Should().Be(2);
        body.Succeeded.Should().Be(2);
        body.Failed.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task BulkCreateLinks_EmptyLinks_Returns400()
    {
        var (client, _) = AsAdmin();

        var response = await client.PostAsJsonAsync($"{LinksUrl}/bulk",
            new BulkCreateLinksRequest { Links = [] });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
