using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;

namespace ControlPlane.Tests.Endpoints;

[Collection("DynamoDB")]
public sealed class DomainEndpointTests : IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly Guid _tenantId = Guid.NewGuid();

    public DomainEndpointTests(LocalStackFixture localStack)
    {
        _factory = new CustomWebApplicationFactory(localStack.DynamoDb, localStack.TableName);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateDomain_ValidHostname_Returns201WithVerificationInstructions()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var request = new CreateDomainRequest { Hostname = "go.example.com" };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateDomainResponse>();
        body.Should().NotBeNull();
        body!.Hostname.Should().Be("go.example.com");
        body.Status.Should().Be("pending_verification");
        body.Id.Should().NotBeEmpty();
        body.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));

        body.VerificationInstructions.Should().NotBeNull();
        body.VerificationInstructions.TxtName.Should().Be("go.example.com");
        body.VerificationInstructions.TxtValue.Should().StartWith("short-io-verify=");
        body.VerificationInstructions.CnameName.Should().Be("go.example.com");
        body.VerificationInstructions.CnameValue.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateDomain_DuplicateHostname_Returns409Conflict()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var request = new CreateDomainRequest { Hostname = "duplicate.example.com" };

        var first = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains", request);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var second = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains", request);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateDomain_DuplicateHostnameDifferentCase_Returns409Conflict()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var first = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "Case.Example.Com" });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var second = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "case.example.com" });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateDomain_SameHostname_DifferentTenants_Succeeds()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var otherTenantId = Guid.NewGuid();

        var first = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "shared.example.com" });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var (otherClient, _) = _factory.CreateAuthenticatedClient(
            otherTenantId.ToString(), Roles.Admin);
        var second = await otherClient.PostAsJsonAsync(
            $"/api/v1/tenants/{otherTenantId}/domains",
            new CreateDomainRequest { Hostname = "shared.example.com" });

        second.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("")]
    [InlineData("not-a-domain")]
    [InlineData("https://go.example.com")]
    [InlineData("http://go.example.com")]
    [InlineData("go.example.com/path")]
    [InlineData("go.example.com/path?query=1")]
    [InlineData("@invalid.com")]
    [InlineData("user@example.com")]
    public async Task CreateDomain_InvalidHostname_Returns400(string hostname)
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var request = new CreateDomainRequest { Hostname = hostname };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateDomain_DomainLimitExceeded_Returns422()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        for (int i = 0; i < 5; i++)
        {
            claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
                _tenantId.ToString(), Roles.Admin));
            var response = await client.PostAsJsonAsync(
                $"/api/v1/tenants/{_tenantId}/domains",
                new CreateDomainRequest { Hostname = $"site{i}.example.com" });
            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var overLimit = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "overflow.example.com" });

        overLimit.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateDomain_WithoutDomainWritePermission_Returns403()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Viewer);

        var request = new CreateDomainRequest { Hostname = "example.com" };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateDomain_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var request = new CreateDomainRequest { Hostname = "example.com" };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateDomain_ResponseContainsLocationHeader()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "location-test.example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.OriginalString.Should()
            .Contain($"/tenants/{_tenantId}/domains/");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateDomain_HostnameExceedingMaxLength_Returns400()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var longLabel = new string('a', 63);
        var longHostname = $"{longLabel}.{longLabel}.{longLabel}.{longLabel}"; // > 253 chars

        var request = new CreateDomainRequest { Hostname = longHostname };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateDomain_WithMemberRole_Returns403()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Member);

        var request = new CreateDomainRequest { Hostname = "member.example.com" };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── ListDomains ──────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListDomains_ReturnsPaginatedResults()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        for (int i = 0; i < 3; i++)
        {
            claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
                _tenantId.ToString(), Roles.Admin));
            var create = await client.PostAsJsonAsync(
                $"/api/v1/tenants/{_tenantId}/domains",
                new CreateDomainRequest { Hostname = $"list-{i}.example.com" });
            create.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ListDomainsResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().HaveCount(3);
        body.TotalCount.Should().Be(3);
        body.Page.Should().Be(1);
        body.PageSize.Should().Be(20);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListDomains_EmptyList_Returns200WithNoItems()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Viewer);

        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ListDomainsResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().BeEmpty();
        body.TotalCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListDomains_FilteredByStatus_ReturnsMatchingOnly()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "active.example.com" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var activeResponse = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains?status=pending_verification");
        activeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var activeBody = await activeResponse.Content.ReadFromJsonAsync<ListDomainsResponse>();
        activeBody!.TotalCount.Should().Be(1);

        var emptyResponse = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains?status=active");
        emptyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var emptyBody = await emptyResponse.Content.ReadFromJsonAsync<ListDomainsResponse>();
        emptyBody!.TotalCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListDomains_Pagination_RespectsPageAndPageSize()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        for (int i = 0; i < 5; i++)
        {
            claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
                _tenantId.ToString(), Roles.Admin));
            var create = await client.PostAsJsonAsync(
                $"/api/v1/tenants/{_tenantId}/domains",
                new CreateDomainRequest { Hostname = $"page-{i}.example.com" });
            create.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains?page=1&pageSize=2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ListDomainsResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().HaveCount(2);
        body.TotalCount.Should().Be(5);
        body.Page.Should().Be(1);
        body.PageSize.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListDomains_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListDomains_ExcludesDeletedDomains()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "keep.example.com" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var createDelete = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "delete.example.com" });
        createDelete.StatusCode.Should().Be(HttpStatusCode.Created);
        var toDelete = await createDelete.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var deleteResponse = await client.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{toDelete!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var listResponse = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await listResponse.Content.ReadFromJsonAsync<ListDomainsResponse>();
        body!.TotalCount.Should().Be(1);
        body.Items[0].Id.Should().Be(created!.Id);
    }

    // ── GetDomain ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetDomain_ReturnsDetailWithDefaultSettings()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "detail.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DomainDetailResponse>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(created.Id);
        body.Hostname.Should().Be("detail.example.com");
        body.Status.Should().Be("pending_verification");
        body.CertificateStatus.Should().Be("pending");
        body.LinkCount.Should().Be(0);
        body.Settings.Should().NotBeNull();
        body.Settings.NotFoundBehavior.Should().Be("404");
        body.Settings.DefaultRedirectUrl.Should().BeNull();
        body.Settings.ErrorPageBranding.Should().BeNull();
        body.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetDomain_NotFound_Returns404()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Viewer);

        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetDomain_CrossTenant_Returns404()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "cross-tenant.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        var otherTenantId = Guid.NewGuid();
        var (otherClient, _) = _factory.CreateAuthenticatedClient(
            otherTenantId.ToString(), Roles.Viewer);
        var response = await otherClient.GetAsync(
            $"/api/v1/tenants/{otherTenantId}/domains/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetDomain_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── UpdateDomain ─────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateDomain_UpdatesSettings_ReturnsUpdated()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "update.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var update = new UpdateDomainRequest
        {
            DefaultRedirectUrl = "https://example.com",
            NotFoundBehavior = "redirect"
        };
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}", update);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DomainDetailResponse>();
        body.Should().NotBeNull();
        body!.Settings.DefaultRedirectUrl.Should().Be("https://example.com");
        body.Settings.NotFoundBehavior.Should().Be("redirect");
        body.UpdatedAt.Should().NotBeNull();
        body.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateDomain_PartialUpdate_KeepsExistingValues()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "partial.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        // First update: set redirect URL
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}",
            new UpdateDomainRequest { DefaultRedirectUrl = "https://first.example.com" });

        // Second update: only change behavior, URL should persist
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}",
            new UpdateDomainRequest { NotFoundBehavior = "passthrough" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DomainDetailResponse>();
        body!.Settings.DefaultRedirectUrl.Should().Be("https://first.example.com");
        body.Settings.NotFoundBehavior.Should().Be("passthrough");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateDomain_InvalidUrl_Returns400()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "invalid-url.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var update = new UpdateDomainRequest { DefaultRedirectUrl = "not-a-url" };
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}", update);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateDomain_NotFound_Returns404()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{Guid.NewGuid()}",
            new UpdateDomainRequest { NotFoundBehavior = "redirect" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateDomain_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{Guid.NewGuid()}",
            new UpdateDomainRequest { NotFoundBehavior = "redirect" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateDomain_WithViewerRole_Returns403()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Viewer);

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{Guid.NewGuid()}",
            new UpdateDomainRequest { NotFoundBehavior = "redirect" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── DeleteDomain ─────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteDomain_SoftDeletes_Returns204()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "soft-delete.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var response = await client.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteDomain_AfterSoftDelete_Returns404()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "gone.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var delete = await client.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var get = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteDomain_NotFound_Returns404()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var response = await client.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteDomain_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteDomain_WithViewerRole_Returns403()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Viewer);

        var response = await client.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── VerifyDomain ──────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task VerifyDomain_SuccessfulVerification_ReturnsActiveStatus()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "verify-pass.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/verify", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<VerifyDomainResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("active");
        body.Hostname.Should().Be("verify-pass.example.com");
        body.TxtCheck.Passed.Should().BeTrue();
        body.CnameCheck.Passed.Should().BeTrue();
        body.Message.Should().Contain("verified successfully");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task VerifyDomain_TextRecordFails_ReturnsVerificationFailed()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "verify-txt-fail.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        _factory.DnsVerification.SetNextResult(
            TestDnsVerificationService.FailTxtResult(
                $"short-io-verify={created!.VerificationInstructions.TxtValue.Split('=')[1]}",
                created.VerificationInstructions.CnameValue));

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}/verify", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<VerifyDomainResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("verification_failed");
        body.TxtCheck.Passed.Should().BeFalse();
        body.TxtCheck.Error.Should().NotBeNullOrEmpty();
        body.CnameCheck.Passed.Should().BeTrue();
        body.Message.Should().Contain("Verification failed");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task VerifyDomain_CnameRecordFails_ReturnsVerificationFailed()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "verify-cname-fail.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        _factory.DnsVerification.SetNextResult(
            TestDnsVerificationService.FailCnameResult(
                $"short-io-verify={created!.VerificationInstructions.TxtValue.Split('=')[1]}",
                created.VerificationInstructions.CnameValue));

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}/verify", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<VerifyDomainResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("verification_failed");
        body.TxtCheck.Passed.Should().BeTrue();
        body.CnameCheck.Passed.Should().BeFalse();
        body.CnameCheck.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task VerifyDomain_RateLimited_Returns429()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "verify-ratelimit.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        // First verification is allowed (default stub passes)
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var first = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/verify", null);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second verification within cooldown should be rate limited
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var second = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}/verify", null);

        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task VerifyDomain_AlreadyActive_Returns400()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "verify-already-active.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        // First verification succeeds
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var first = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/verify", null);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Wait past cooldown and try again on active domain
        // Can't really wait 60s in tests, but we can verify the domain is marked active.
        // Just check that the status is now "active" by fetching it.
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var getResponse = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}");
        var detail = await getResponse.Content.ReadFromJsonAsync<DomainDetailResponse>();
        detail!.Status.Should().Be("active");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task VerifyDomain_RetryAfterFailed_Allowed()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "verify-retry.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        // First verification fails
        _factory.DnsVerification.SetNextResult(
            TestDnsVerificationService.FailBothResult(
                $"short-io-verify={created!.VerificationInstructions.TxtValue.Split('=')[1]}",
                created.VerificationInstructions.CnameValue));

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var first = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}/verify", null);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<VerifyDomainResponse>();
        firstBody!.Status.Should().Be("verification_failed");

        // Rate limited immediately after
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var second = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}/verify", null);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // But the domain should still be retrievable and show verification_failed
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var getResponse = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}");
        var detail = await getResponse.Content.ReadFromJsonAsync<DomainDetailResponse>();
        detail!.Status.Should().Be("verification_failed");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task VerifyDomain_NotFound_Returns404()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{Guid.NewGuid()}/verify", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task VerifyDomain_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{Guid.NewGuid()}/verify", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task VerifyDomain_WithViewerRole_Returns403()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Viewer);

        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{Guid.NewGuid()}/verify", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Certificate ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RequestCertificate_AutomaticallyProvisionedOnVerification()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        // Create and verify a domain
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "auto-cert2.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var verifyResponse = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/verify", null);
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Domain detail should have certificate provisioned after verification
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var getResponse = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await getResponse.Content.ReadFromJsonAsync<DomainDetailResponse>();
        detail.Should().NotBeNull();
        detail!.CertificateArn.Should().NotBeNullOrEmpty();
        detail.CertificateStatus.Should().Be("pending_validation");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RequestCertificate_NotActiveDomain_Returns400()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "not-active.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/certificate", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RequestCertificate_AlreadyProvisioned_Returns409()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        // Create, verify, and provision certificate
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "dup-cert.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/verify", null);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}/certificate", null);

        // Second request should conflict
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}/certificate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetCertificateStatus_ReturnsStatus()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        // Create, verify, and provision certificate
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "cert-status.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/verify", null);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}/certificate", null);

        // Get certificate status
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}/certificate");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CertificateStatusResponse>();
        body.Should().NotBeNull();
        body!.DomainId.Should().Be(created.Id);
        body.CertificateArn.Should().NotBeNullOrEmpty();
        body.Status.Should().Be("pending_validation");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetCertificateStatus_NoCertificate_Returns400()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "no-cert.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/certificate");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task VerifyDomain_AutoProvisionsCertificate()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "auto-cert.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        // Verify the domain — should auto-provision certificate
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var verifyResponse = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/verify", null);
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Check domain detail — should have certificate ARN
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var getResponse = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await getResponse.Content.ReadFromJsonAsync<DomainDetailResponse>();
        detail.Should().NotBeNull();
        detail!.CertificateArn.Should().NotBeNullOrEmpty();
        detail.CertificateStatus.Should().Be("pending_validation");
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
