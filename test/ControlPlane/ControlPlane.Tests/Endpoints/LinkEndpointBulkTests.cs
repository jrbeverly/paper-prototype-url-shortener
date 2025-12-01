using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;

namespace ControlPlane.Tests.Endpoints;

[Collection("DynamoDB")]
public sealed class LinkEndpointBulkTests : IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly Guid _tenantId = Guid.NewGuid();

    public LinkEndpointBulkTests(LocalStackFixture localStack)
    {
        _factory = new CustomWebApplicationFactory(localStack.DynamoDb, localStack.TableName);
    }

    private async Task<(Guid DomainId, string Hostname)> CreateVerifiedDomainAsync(
        HttpClient client, TestClaimsProvider claimsProvider)
    {
        var hostname = $"bulk-{Guid.NewGuid():N}.example.com";
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

    // ── Bulk Create ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BulkCreate_AllSucceed_Returns200WithAllResults()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, hostname) = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var request = new BulkCreateLinksRequest
        {
            Links =
            [
                new CreateLinkRequest { DomainId = domainId, DestinationUrl = "https://example.com/a" },
                new CreateLinkRequest { DomainId = domainId, DestinationUrl = "https://example.com/b" },
                new CreateLinkRequest { DomainId = domainId, DestinationUrl = "https://example.com/c" }
            ]
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/bulk", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BulkCreateLinksResponse>();
        body.Should().NotBeNull();
        body!.TotalRequested.Should().Be(3);
        body.Succeeded.Should().Be(3);
        body.Failed.Should().Be(0);
        body.Results.Should().HaveCount(3);
        body.Results.Should().AllSatisfy(r => r.Success.Should().BeTrue());
        body.Results.Should().AllSatisfy(r => r.Link.Should().NotBeNull());
        body.Results.Select(r => r.Link!.ShortUrl).Should().AllSatisfy(url =>
            url.Should().Contain(hostname));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BulkCreate_PartialFailures_Returns200WithMixedResults()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var request = new BulkCreateLinksRequest
        {
            Links =
            [
                new CreateLinkRequest { DomainId = domainId, DestinationUrl = "https://example.com/valid" },
                new CreateLinkRequest { DomainId = domainId, DestinationUrl = "not-a-valid-url" },
                new CreateLinkRequest { DomainId = domainId, DestinationUrl = "https://example.com/also-valid" }
            ]
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/bulk", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BulkCreateLinksResponse>();
        body.Should().NotBeNull();
        body!.TotalRequested.Should().Be(3);
        body.Succeeded.Should().Be(2);
        body.Failed.Should().Be(1);
        body.Results[0].Success.Should().BeTrue();
        body.Results[1].Success.Should().BeFalse();
        body.Results[1].Error.Should().Contain("Destination URL");
        body.Results[2].Success.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BulkCreate_DomainNotFound_ReturnsErrorPerItem()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Member);

        var request = new BulkCreateLinksRequest
        {
            Links =
            [
                new CreateLinkRequest { DomainId = Guid.NewGuid(), DestinationUrl = "https://example.com" }
            ]
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/bulk", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BulkCreateLinksResponse>();
        body.Should().NotBeNull();
        body!.Failed.Should().Be(1);
        body.Results[0].Error.Should().Contain("not found");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BulkCreate_WithCustomSlugs_Succeeds()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var request = new BulkCreateLinksRequest
        {
            Links =
            [
                new CreateLinkRequest { DomainId = domainId, DestinationUrl = "https://example.com/1", Slug = "bulk-slug-1" },
                new CreateLinkRequest { DomainId = domainId, DestinationUrl = "https://example.com/2", Slug = "bulk-slug-2" }
            ]
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/bulk", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BulkCreateLinksResponse>();
        body.Should().NotBeNull();
        body!.Succeeded.Should().Be(2);
        body.Results[0].Link!.Slug.Should().Be("bulk-slug-1");
        body.Results[1].Link!.Slug.Should().Be("bulk-slug-2");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BulkCreate_DuplicateSlugInBatch_SecondFails()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var request = new BulkCreateLinksRequest
        {
            Links =
            [
                new CreateLinkRequest { DomainId = domainId, DestinationUrl = "https://example.com/1", Slug = "dup-slug" },
                new CreateLinkRequest { DomainId = domainId, DestinationUrl = "https://example.com/2", Slug = "dup-slug" }
            ]
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/bulk", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BulkCreateLinksResponse>();
        body.Should().NotBeNull();
        body!.Succeeded.Should().Be(1);
        body.Failed.Should().Be(1);
        body.Results[1].Error.Should().Contain("already in use");
    }

    // ── Rate Limit Enforcement ───────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BulkCreate_RateLimitApplied_PartialSuccess()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        // Fill up to near the limit (50). Create 48 links first.
        for (int i = 0; i < 48; i++)
        {
            claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
                _tenantId.ToString(), Roles.Member));
            var r = await client.PostAsJsonAsync(
                $"/api/v1/tenants/{_tenantId}/links",
                new CreateLinkRequest { DomainId = domainId, DestinationUrl = $"https://example.com/{i}" });
            r.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // Now try to bulk create 5 more — only 2 should succeed
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var request = new BulkCreateLinksRequest
        {
            Links = Enumerable.Range(0, 5).Select(i => new CreateLinkRequest
            {
                DomainId = domainId,
                DestinationUrl = $"https://example.com/bulk-{i}"
            }).ToList()
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/bulk", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BulkCreateLinksResponse>();
        body.Should().NotBeNull();
        body!.Succeeded.Should().Be(2);
        body.Failed.Should().Be(3);
    }

    // ── Authorization ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BulkCreate_WithoutAuth_Returns401()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(null!);

        var unauthClient = _factory.CreateClient();
        var request = new BulkCreateLinksRequest
        {
            Links = [new CreateLinkRequest { DomainId = domainId, DestinationUrl = "https://example.com" }]
        };

        var response = await unauthClient.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/bulk", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BulkCreate_WithoutLinkWritePermission_Returns403()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var request = new BulkCreateLinksRequest
        {
            Links = [new CreateLinkRequest { DomainId = domainId, DestinationUrl = "https://example.com" }]
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/bulk", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── CSV Import ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ImportCsv_ValidFile_CreatesLinks()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, hostname) = await CreateVerifiedDomainAsync(client, claimsProvider);

        var csv = $"domain,slug,destination,redirect_type\n{hostname},imported-1,https://example.com/a,301\n{hostname},imported-2,https://example.com/b,302\n";
        using var content = new MultipartFormDataContent();
        var csvContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(csv));
        csvContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        content.Add(csvContent, "file", "links.csv");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/links/import", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BulkCreateLinksResponse>();
        body.Should().NotBeNull();
        body!.Succeeded.Should().Be(2);
        body.Results[0].Link!.Slug.Should().Be("imported-1");
        body.Results[0].Link!.RedirectType.Should().Be("301");
        body.Results[1].Link!.Slug.Should().Be("imported-2");
        body.Results[1].Link!.RedirectType.Should().Be("302");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ImportCsv_InvalidRows_ReturnsPartialErrors()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, hostname) = await CreateVerifiedDomainAsync(client, claimsProvider);

        var csv = $"domain,slug,destination,redirect_type\n{hostname},valid-1,https://example.com/a,301\nnonexistent.example.com,invalid-1,https://example.com/b,302\n";
        using var content = new MultipartFormDataContent();
        var csvContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(csv));
        csvContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        content.Add(csvContent, "file", "links.csv");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/links/import", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BulkCreateLinksResponse>();
        body.Should().NotBeNull();
        body!.Succeeded.Should().Be(1);
        body.Failed.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ImportCsv_NoFile_Returns400()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Member);
        using var content = new MultipartFormDataContent();

        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/links/import", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ImportCsv_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();
        using var content = new MultipartFormDataContent();

        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/links/import", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── CSV Export ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExportCsv_ReturnsCsvWithHeaders()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        // Create a couple of links first
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links",
            new CreateLinkRequest { DomainId = domainId, DestinationUrl = "https://example.com/1", Slug = "export-1" });
        await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links",
            new CreateLinkRequest { DomainId = domainId, DestinationUrl = "https://example.com/2", Slug = "export-2" });

        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var csv = await response.Content.ReadAsStringAsync();
        csv.Should().Contain("id,domain_id,domain,slug,destination_url,short_url,redirect_type,expires_at,created_at");
        csv.Should().Contain("export-1");
        csv.Should().Contain("export-2");
        csv.Should().Contain("https://example.com/1");
        csv.Should().Contain("https://example.com/2");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExportCsv_EmptyTenant_ReturnsHeadersOnly()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Member);

        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var csv = await response.Content.ReadAsStringAsync();
        csv.Should().Contain("id,domain_id,domain,slug,destination_url,short_url,redirect_type,expires_at,created_at");
        // Only one line (header)
        csv.TrimEnd().Split('\n').Should().HaveCount(1);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExportCsv_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links/export");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Bulk with Empty Request ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BulkCreate_EmptyLinksArray_Returns400()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Member);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/bulk",
            new BulkCreateLinksRequest { Links = [] });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
