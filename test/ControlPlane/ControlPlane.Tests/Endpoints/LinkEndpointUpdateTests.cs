using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;

namespace ControlPlane.Tests.Endpoints;

[Collection("DynamoDB")]
public sealed class LinkEndpointUpdateTests : IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly Guid _tenantId = Guid.NewGuid();

    public LinkEndpointUpdateTests(LocalStackFixture localStack)
    {
        _factory = new CustomWebApplicationFactory(localStack.DynamoDb, localStack.TableName);
    }

    private async Task<(Guid DomainId, string Hostname)> CreateVerifiedDomainAsync(
        HttpClient client, TestClaimsProvider claimsProvider)
    {
        var hostname = $"upd-{Guid.NewGuid():N}.example.com";
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
        string destination = "https://example.com", string? slug = null,
        string redirectType = "302")
    {
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var request = new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = destination,
            Slug = slug,
            RedirectType = redirectType
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        return body!;
    }

    // ── Destination URL ─────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_ChangeDestinationUrl_Returns200()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, hostname) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId,
            destination: "https://example.com/old", slug: "my-link");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { DestinationUrl = "https://example.com/new" });

        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await patch.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body!.DestinationUrl.Should().Be("https://example.com/new");
        body.Slug.Should().Be("my-link");
        body.ShortUrl.Should().Be($"https://{hostname}/my-link");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_SlugPreservedAfterUpdate()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, hostname) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId, slug: "preserve-me");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { DestinationUrl = "https://other.com" });

        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await patch.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body!.Slug.Should().Be("preserve-me");
        body.ShortUrl.Should().Be($"https://{hostname}/preserve-me");
    }

    // ── Redirect Type ───────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_ChangeRedirectType_Returns200()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId, redirectType: "302");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { RedirectType = "301" });

        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await patch.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body!.RedirectType.Should().Be("301");
    }

    // ── Expiration ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_SetExpiresAt_Returns200()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId);
        var future = DateTime.UtcNow.AddDays(30);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { ExpiresAt = future });

        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await patch.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body!.ExpiresAt.Should().BeCloseTo(future, TimeSpan.FromSeconds(5));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_ModifyExpiresAt_Returns200()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId);
        var first = DateTime.UtcNow.AddDays(10);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { ExpiresAt = first });

        var second = DateTime.UtcNow.AddDays(60);
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { ExpiresAt = second });

        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await patch.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body!.ExpiresAt.Should().BeCloseTo(second, TimeSpan.FromSeconds(5));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_ClearExpiresAt_Returns200()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId);

        // Set expiration first
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { ExpiresAt = DateTime.UtcNow.AddDays(30) });

        // Clear it
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { ClearExpiresAt = true });

        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await patch.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body!.ExpiresAt.Should().BeNull();
    }

    // ── Versioning ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_VersionIncrementsOnEachUpdate()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId);
        created.Version.Should().Be(1);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var patch1 = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { DestinationUrl = "https://v2.example.com" });
        var body1 = await patch1.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body1!.Version.Should().Be(2);

        var patch2 = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { RedirectType = "301" });
        var body2 = await patch2.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body2!.Version.Should().Be(3);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_UpdatedAtIsSet()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId);

        await Task.Delay(10);
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { DestinationUrl = "https://updated.example.com" });

        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await patch.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body!.UpdatedAt.Should().BeAfter(created.CreatedAt);
    }

    // ── Status ──────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_PauseLink_Returns200()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { Status = "paused" });

        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await patch.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body!.Status.Should().Be("paused");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_ResumeLink_Returns200()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId);

        // Pause first
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { Status = "paused" });

        // Resume
        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { Status = "active" });

        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await patch.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body!.Status.Should().Be("active");
    }

    // ── Rules ───────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_SetRules_Returns200()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId);

        var rules = new Dictionary<string, string>
        {
            ["utm_source"] = "twitter",
            ["utm_medium"] = "social"
        };

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { Rules = rules });

        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await patch.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body!.Rules.Should().NotBeNull();
        body.Rules!["utm_source"].Should().Be("twitter");
        body.Rules["utm_medium"].Should().Be("social");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_ClearRules_Returns200()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId);

        // Set rules first
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { Rules = new Dictionary<string, string> { ["key"] = "value" } });

        // Clear rules
        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { ClearRules = true });

        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await patch.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body!.Rules.Should().BeNull();
    }

    // ── Multiple Fields ─────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_MultipleFields_Returns200()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId,
            destination: "https://old.example.com", redirectType: "302");
        var future = DateTime.UtcNow.AddDays(60);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest
            {
                DestinationUrl = "https://new.example.com",
                RedirectType = "301",
                ExpiresAt = future,
                Status = "paused"
            });

        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await patch.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body!.DestinationUrl.Should().Be("https://new.example.com");
        body.RedirectType.Should().Be("301");
        body.ExpiresAt.Should().BeCloseTo(future, TimeSpan.FromSeconds(5));
        body.Status.Should().Be("paused");
        body.Version.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_EmptyBody_Returns200WithNoChanges()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId,
            destination: "https://unchanged.example.com");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest());

        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await patch.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body!.DestinationUrl.Should().Be("https://unchanged.example.com");
        body.Version.Should().Be(2); // version still increments
    }

    // ── Error Cases ─────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_NonExistent_Returns404()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Member);

        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{Guid.NewGuid()}",
            new UpdateLinkRequest { DestinationUrl = "https://example.com" });

        patch.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_DeletedLink_Returns404()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        await client.DeleteAsync($"/api/v1/tenants/{_tenantId}/links/{created.Id}");

        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { DestinationUrl = "https://example.com" });

        patch.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_WithoutAuth_Returns401()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId);

        claimsProvider.SetClaims(null!);
        var unauthClient = _factory.CreateClient();
        var patch = await unauthClient.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { DestinationUrl = "https://example.com" });

        patch.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_WithoutLinkWritePermission_Returns403()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { DestinationUrl = "https://example.com" });

        patch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_InvalidDestinationUrl_Returns400()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { DestinationUrl = "not-a-valid-url" });

        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_InvalidRedirectType_Returns400()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { RedirectType = "999" });

        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_ExpiresInPast_Returns400()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { ExpiresAt = DateTime.UtcNow.AddHours(-1) });

        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_InvalidStatus_Returns400()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { Status = "invalid-status" });

        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Response Integrity ──────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_ResponseIncludesAllFields()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, hostname) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var created = await CreateLinkAsync(client, claimsProvider, domainId, slug: "full-response");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var patch = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created.Id}",
            new UpdateLinkRequest { DestinationUrl = "https://updated.example.com" });

        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await patch.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body!.Id.Should().Be(created.Id);
        body.DomainId.Should().Be(domainId);
        body.DestinationUrl.Should().Be("https://updated.example.com");
        body.Slug.Should().Be("full-response");
        body.ShortUrl.Should().Be($"https://{hostname}/full-response");
        body.RedirectType.Should().Be("302");
        body.Status.Should().Be("active");
        body.ClickCount.Should().Be(0);
        body.Version.Should().Be(2);
        body.UpdatedAt.Should().NotBeNull();
        body.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
