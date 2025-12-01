using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;

namespace ControlPlane.Tests.Endpoints;

[Collection("DynamoDB")]
public sealed class LinkEndpointRetrievalTests : IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly Guid _tenantId = Guid.NewGuid();

    public LinkEndpointRetrievalTests(LocalStackFixture localStack)
    {
        _factory = new CustomWebApplicationFactory(localStack.DynamoDb, localStack.TableName);
    }

    private async Task<(Guid DomainId, string Hostname)> CreateVerifiedDomainAsync(
        HttpClient client, TestClaimsProvider claimsProvider)
    {
        var hostname = $"ret-{Guid.NewGuid():N}.example.com";
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = hostname });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var verify = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/verify", null);
        verify.StatusCode.Should().Be(HttpStatusCode.OK);

        return (created.Id, hostname);
    }

    private async Task<CreateLinkResponse> CreateLinkAsync(
        HttpClient client, TestClaimsProvider claimsProvider, Guid domainId,
        string destination = "https://example.com", string? slug = null)
    {
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var request = new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = destination,
            Slug = slug
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        return body!;
    }

    // ── List Links ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListLinks_ReturnsAllLinksForTenant()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        await CreateLinkAsync(client, claimsProvider, domainId, slug: "link-a");
        await CreateLinkAsync(client, claimsProvider, domainId, slug: "link-b");
        await CreateLinkAsync(client, claimsProvider, domainId, slug: "link-c");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LinkListResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().HaveCount(3);
        body.TotalCount.Should().Be(3);
        body.NextCursor.Should().BeNull();
        body.Items.Should().AllSatisfy(i =>
        {
            i.DomainHostname.Should().NotBeNullOrEmpty();
            i.ShortUrl.Should().NotBeNullOrEmpty();
            i.ClickCount.Should().Be(0);
        });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListLinks_EmptyTenant_ReturnsEmptyList()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Viewer);

        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LinkListResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().BeEmpty();
        body.TotalCount.Should().Be(0);
        body.NextCursor.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListLinks_Pagination_CursorReturnsNextPage()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        for (int i = 0; i < 5; i++)
            await CreateLinkAsync(client, claimsProvider, domainId, slug: $"page-{i}");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var page1 = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links?limit=2");

        page1.StatusCode.Should().Be(HttpStatusCode.OK);
        var body1 = await page1.Content.ReadFromJsonAsync<LinkListResponse>();
        body1!.Items.Should().HaveCount(2);
        body1.TotalCount.Should().Be(5);
        body1.NextCursor.Should().NotBeNullOrEmpty();

        var page2 = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links?limit=2&cursor={body1.NextCursor}");

        page2.StatusCode.Should().Be(HttpStatusCode.OK);
        var body2 = await page2.Content.ReadFromJsonAsync<LinkListResponse>();
        body2!.Items.Should().HaveCount(2);
        body2.TotalCount.Should().Be(5);

        // Verify unique IDs across pages
        var page1Ids = body1.Items.Select(i => i.Id).ToHashSet();
        var page2Ids = body2.Items.Select(i => i.Id).ToHashSet();
        page1Ids.Intersect(page2Ids).Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListLinks_Pagination_LastPageHasNoCursor()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        for (int i = 0; i < 3; i++)
            await CreateLinkAsync(client, claimsProvider, domainId, slug: $"last-{i}");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links?limit=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LinkListResponse>();
        body!.Items.Should().HaveCount(3);
        body.NextCursor.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListLinks_SearchBySlugPrefix_ReturnsMatchingLinks()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        await CreateLinkAsync(client, claimsProvider, domainId, slug: "marketing-page");
        await CreateLinkAsync(client, claimsProvider, domainId, slug: "marketing-blog");
        await CreateLinkAsync(client, claimsProvider, domainId, slug: "sales-page");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links?search=marketing");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LinkListResponse>();
        body!.Items.Should().HaveCount(2);
        body.TotalCount.Should().Be(2);
        body.Items.Should().AllSatisfy(i =>
            i.Slug.Should().StartWith("marketing"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListLinks_SearchByDestinationUrl_ReturnsMatchingLinks()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        await CreateLinkAsync(client, claimsProvider, domainId,
            destination: "https://acme.com/products", slug: "p1");
        await CreateLinkAsync(client, claimsProvider, domainId,
            destination: "https://acme.com/pricing", slug: "p2");
        await CreateLinkAsync(client, claimsProvider, domainId,
            destination: "https://other.com/page", slug: "p3");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links?search=acme");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LinkListResponse>();
        body!.Items.Should().HaveCount(2);
        body.TotalCount.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListLinks_FilterByDomain_ReturnsOnlyThatDomainsLinks()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (d1, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var (d2, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        await CreateLinkAsync(client, claimsProvider, d1, slug: "domain1-link");
        await CreateLinkAsync(client, claimsProvider, d2, slug: "domain2-link");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links?domainId={d1}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LinkListResponse>();
        body!.Items.Should().HaveCount(1);
        body.Items[0].DomainId.Should().Be(d1);
        body.Items[0].Slug.Should().Be("domain1-link");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListLinks_FilterByDomain_NoMatches_ReturnsEmptyList()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        await CreateLinkAsync(client, claimsProvider, domainId);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links?domainId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LinkListResponse>();
        body!.Items.Should().BeEmpty();
        body.TotalCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListLinks_FilterByStatus_ReturnsOnlyMatchingStatus()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        var link1 = await CreateLinkAsync(client, claimsProvider, domainId, slug: "to-delete");

        // Soft-delete the link so it has status "deleted"
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        await client.DeleteAsync($"/api/v1/tenants/{_tenantId}/links/{link1.Id}");

        await CreateLinkAsync(client, claimsProvider, domainId, slug: "active-link");

        // Filter by "active" should return only the active link
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links?status=active");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LinkListResponse>();
        body!.Items.Should().HaveCount(1);
        body.Items[0].Status.Should().Be("active");
        body.Items[0].Slug.Should().Be("active-link");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListLinks_SortByCreatedAt_DefaultOrderDescending()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        await CreateLinkAsync(client, claimsProvider, domainId, slug: "first");
        await Task.Delay(10);
        await CreateLinkAsync(client, claimsProvider, domainId, slug: "second");
        await Task.Delay(10);
        await CreateLinkAsync(client, claimsProvider, domainId, slug: "third");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links?sort=created_at");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LinkListResponse>();
        body!.Items.Should().HaveCount(3);
        body.Items[0].Slug.Should().Be("third");
        body.Items[1].Slug.Should().Be("second");
        body.Items[2].Slug.Should().Be("first");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListLinks_IncludesClickCount()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        await CreateLinkAsync(client, claimsProvider, domainId, slug: "clickable");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LinkListResponse>();
        body!.Items.Should().HaveCount(1);
        body.Items[0].ClickCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListLinks_InvalidLimit_Returns400()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Viewer);

        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links?limit=0");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListLinks_LimitTooLarge_Returns400()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Viewer);

        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links?limit=101");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListLinks_InvalidSort_Returns400()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Viewer);

        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links?sort=invalid_field");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListLinks_WithoutAuth_Returns401()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Viewer);

        _ = client; // suppress unused variable warning
        claimsProvider.SetClaims(null!);
        var unauthClient = _factory.CreateClient();

        var response = await unauthClient.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListLinks_WithoutLinkReadPermission_Returns403()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        await CreateLinkAsync(client, claimsProvider, domainId);

        // Create principal with domain:read but no link:read
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), "none", additionalPermissions: [Permissions.DomainRead]));

        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Get Link ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetLink_ValidId_ReturnsLinkDetail()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, hostname) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId,
            destination: "https://example.com/target", slug: "get-me");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LinkDetailResponse>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(created.Id);
        body.DomainId.Should().Be(domainId);
        body.DomainHostname.Should().Be(hostname);
        body.DestinationUrl.Should().Be("https://example.com/target");
        body.Slug.Should().Be("get-me");
        body.ShortUrl.Should().Be($"https://{hostname}/get-me");
        body.RedirectType.Should().Be("302");
        body.Status.Should().Be("active");
        body.ClickCount.Should().Be(0);
        body.DeletedAt.Should().BeNull();
        body.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetLink_IncludesAnalyticsSummary()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LinkDetailResponse>();
        body!.Analytics.Should().NotBeNull();
        body.Analytics!.TotalClicks.Should().Be(body.ClickCount);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetLink_NonExistent_Returns404()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Viewer);

        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetLink_WithoutAuth_Returns401()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId);

        claimsProvider.SetClaims(null!);
        var unauthClient = _factory.CreateClient();
        var response = await unauthClient.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetLink_WithoutLinkReadPermission_Returns403()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), "none", additionalPermissions: [Permissions.DomainRead]));

        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListLinks_DeletedLinksExcluded()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        var link = await CreateLinkAsync(client, claimsProvider, domainId, slug: "will-delete");
        await CreateLinkAsync(client, claimsProvider, domainId, slug: "will-stay");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        await client.DeleteAsync($"/api/v1/tenants/{_tenantId}/links/{link.Id}");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LinkListResponse>();
        body!.Items.Should().HaveCount(1);
        body.Items[0].Slug.Should().Be("will-stay");
        body.TotalCount.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListLinks_DefaultLimitIs20()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        for (int i = 0; i < 25; i++)
            await CreateLinkAsync(client, claimsProvider, domainId, slug: $"l-{i:D3}");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LinkListResponse>();
        body!.Items.Should().HaveCount(20);
        body.TotalCount.Should().Be(25);
        body.NextCursor.Should().NotBeNullOrEmpty();
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
