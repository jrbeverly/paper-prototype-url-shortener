using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;

namespace ControlPlane.Tests.Endpoints;

[Collection("DynamoDB")]
public sealed class LinkEndpointDeleteTests : IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly Guid _tenantId = Guid.NewGuid();

    public LinkEndpointDeleteTests(LocalStackFixture localStack)
    {
        _factory = new CustomWebApplicationFactory(localStack.DynamoDb, localStack.TableName);
    }

    private async Task<(Guid DomainId, string Hostname)> CreateVerifiedDomainAsync(
        HttpClient client, TestClaimsProvider claimsProvider)
    {
        var hostname = $"del-{Guid.NewGuid():N}.example.com";
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

    private async Task<Guid> CreateLinkAsync(
        HttpClient client, TestClaimsProvider claimsProvider, Guid domainId, string? slug = null)
    {
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var request = new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com",
            Slug = slug
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        return body!.Id;
    }

    // ── Soft Delete ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteLink_SoftDelete_Returns204()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var linkId = await CreateLinkAsync(client, claimsProvider, domainId);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var response = await client.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/links/{linkId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteLink_NonExistent_Returns404()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Member);

        var response = await client.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/links/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteLink_AlreadyDeleted_Returns404()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var linkId = await CreateLinkAsync(client, claimsProvider, domainId);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        await client.DeleteAsync($"/api/v1/tenants/{_tenantId}/links/{linkId}");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var second = await client.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/links/{linkId}");

        second.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteLink_WithoutAuth_Returns401()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var linkId = await CreateLinkAsync(client, claimsProvider, domainId);

        claimsProvider.SetClaims(null!);
        var unauthClient = _factory.CreateClient();
        var response = await unauthClient.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/links/{linkId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteLink_WithoutLinkWritePermission_Returns403()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var linkId = await CreateLinkAsync(client, claimsProvider, domainId);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/links/{linkId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Hard Delete (Permanent) ───────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteLink_Permanent_Returns204()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var linkId = await CreateLinkAsync(client, claimsProvider, domainId);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var response = await client.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/links/{linkId}?permanent=true");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteLink_Permanent_NonExistent_Returns404()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Member);

        var response = await client.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/links/{Guid.NewGuid()}?permanent=true");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteLink_Permanent_CannotBeRestored()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var linkId = await CreateLinkAsync(client, claimsProvider, domainId);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        await client.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/links/{linkId}?permanent=true");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var restore = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/links/{linkId}/restore", null);

        restore.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Slug Reuse After Deletion ────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteLink_SlugCanBeReusedAfterDeletion()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var linkId = await CreateLinkAsync(client, claimsProvider, domainId, slug: "reusable-slug");

        // Delete the link
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var delete = await client.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/links/{linkId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Create a new link with the same slug
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links",
            new CreateLinkRequest
            {
                DomainId = domainId,
                DestinationUrl = "https://example.com/new",
                Slug = "reusable-slug"
            });

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await create.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body!.Slug.Should().Be("reusable-slug");
        body.Id.Should().NotBe(linkId);
    }

    // ── Deleted Links Excluded from Export ────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteLink_ExcludedFromExport()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        // Create two links
        var link1 = await CreateLinkAsync(client, claimsProvider, domainId, slug: "keep-me");
        var link2 = await CreateLinkAsync(client, claimsProvider, domainId, slug: "delete-me");

        // Delete one
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        await client.DeleteAsync($"/api/v1/tenants/{_tenantId}/links/{link2}");

        // Export should only include the non-deleted link
        var export = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/links/export");
        export.StatusCode.Should().Be(HttpStatusCode.OK);
        var csv = await export.Content.ReadAsStringAsync();
        csv.Should().Contain("keep-me");
        csv.Should().NotContain("delete-me");
    }

    // ── Deleted Links Excluded from Link Limit Count ─────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteLink_DoesNotCountTowardLimit()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        // Fill up to the limit (50)
        for (int i = 0; i < 50; i++)
        {
            claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
                _tenantId.ToString(), Roles.Member));
            var r = await client.PostAsJsonAsync(
                $"/api/v1/tenants/{_tenantId}/links",
                new CreateLinkRequest { DomainId = domainId, DestinationUrl = $"https://example.com/{i}" });
            r.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // Verify we're at the limit
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var overLimit = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links",
            new CreateLinkRequest { DomainId = domainId, DestinationUrl = "https://example.com/overflow" });
        overLimit.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Get a link ID to delete (the first one we created won't be easily accessible,
        // so let's use export to find one)
        var export = await client.GetAsync($"/api/v1/tenants/{_tenantId}/links/export");
        var csv = await export.Content.ReadAsStringAsync();
        var lines = csv.TrimEnd().Split('\n');
        lines.Should().HaveCount(51); // header + 50 links
        var firstLinkLine = lines[1];
        var firstLinkId = Guid.Parse(firstLinkLine.Split(',')[0]);

        // Delete one link
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var delete = await client.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/links/{firstLinkId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Now we should be able to create a new link
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var newLink = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links",
            new CreateLinkRequest { DomainId = domainId, DestinationUrl = "https://example.com/after-delete" });
        newLink.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ── Restore ───────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RestoreLink_DeletedLink_Returns200WithLinkData()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, hostname) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var linkId = await CreateLinkAsync(client, claimsProvider, domainId, slug: "to-restore");

        // Soft delete
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var delete = await client.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/links/{linkId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Restore
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var restore = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/links/{linkId}/restore", null);

        restore.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await restore.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(linkId);
        body.Slug.Should().Be("to-restore");
        body.Status.Should().Be("active");
        body.DeletedAt.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RestoreLink_NonExistent_Returns404()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Member);

        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/links/{Guid.NewGuid()}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RestoreLink_ActiveLink_Returns404()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var linkId = await CreateLinkAsync(client, claimsProvider, domainId);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/links/{linkId}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RestoreLink_WithoutAuth_Returns401()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var linkId = await CreateLinkAsync(client, claimsProvider, domainId);

        // Soft delete
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        await client.DeleteAsync($"/api/v1/tenants/{_tenantId}/links/{linkId}");

        claimsProvider.SetClaims(null!);
        var unauthClient = _factory.CreateClient();
        var response = await unauthClient.PostAsync(
            $"/api/v1/tenants/{_tenantId}/links/{linkId}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
