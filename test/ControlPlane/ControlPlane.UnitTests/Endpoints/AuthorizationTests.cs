using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;

namespace ControlPlane.UnitTests.Endpoints;

/// <summary>
/// Tests for the permission authorization system. Verifies that:
/// - Unauthenticated requests return 401
/// - Cross-tenant requests return 403
/// - Role-derived permissions are correctly mapped
/// - Fine-grained permission policies match role expectations
/// </summary>
public sealed class AuthorizationTests : IAsyncDisposable
{
    private readonly UnitTestWebApplicationFactory _factory = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    private string DomainsUrl => $"/api/v1/tenants/{_tenantId}/domains";
    private string LinksUrl => $"/api/v1/tenants/{_tenantId}/links";
    private string ApiKeysUrl => $"/api/v1/tenants/{_tenantId}/api-keys";
    private string TrialUrl => $"/api/v1/tenants/{_tenantId}/trial";

    // ── Unauthenticated ───────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UnauthenticatedRequest_ProtectedEndpoint_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(DomainsUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UnauthenticatedRequest_PublicEndpoint_Returns200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Cross-tenant ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CrossTenantRequest_Returns403()
    {
        var otherTenantId = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(otherTenantId.ToString(), Roles.Admin);

        // Authenticated as otherTenantId but requesting _tenantId's resources
        var response = await client.GetAsync(DomainsUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Owner / Admin — full permissions ──────────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Admin)]
    public async Task AdminAndOwner_CanAccessDomainWrite(string role)
    {
        var (client, _) = _factory.CreateAuthenticatedClient(_tenantId.ToString(), role);

        var response = await client.PostAsJsonAsync(DomainsUrl,
            new CreateDomainRequest { Hostname = $"auth-{Guid.NewGuid():N}.example.com" });

        // 201 Created — not 401/403
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Admin)]
    public async Task AdminAndOwner_CanManageApiKeys(string role)
    {
        var (client, _) = _factory.CreateAuthenticatedClient(_tenantId.ToString(), role);

        var response = await client.PostAsJsonAsync(ApiKeysUrl,
            new CreateApiKeyRequest { Name = "Auth Test Key", Permissions = [Roles.Viewer] });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Admin)]
    public async Task AdminAndOwner_CanAccessBillingRead(string role)
    {
        // Create a tenant first to have a real tenant to query trial for
        var createClient = _factory.CreateClient();
        var tenantResponse = await createClient.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Auth Tenant", Email = $"auth-{Guid.NewGuid():N}@ex.com" });
        var tenant = await tenantResponse.Content.ReadFromJsonAsync<CreateTenantResponse>();

        var (client, _) = _factory.CreateAuthenticatedClient(tenant!.Id.ToString(), role);
        var response = await client.GetAsync($"/api/v1/tenants/{tenant.Id}/trial");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Member — link write, domain/tenant read only ──────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Member_CanReadDomains()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Member);

        var response = await client.GetAsync(DomainsUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Member_CannotWriteDomains()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Member);

        var response = await client.PostAsJsonAsync(DomainsUrl,
            new CreateDomainRequest { Hostname = $"member-{Guid.NewGuid():N}.example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Member_CannotManageApiKeys()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Member);

        var response = await client.GetAsync(ApiKeysUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Member_CanReadLinks()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Member);

        var response = await client.GetAsync(LinksUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Member_CannotReadBilling()
    {
        // Create tenant and authenticate the member for that tenant
        var createClient = _factory.CreateClient();
        var tenantResponse = await createClient.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Member Tenant", Email = $"mbr-{Guid.NewGuid():N}@ex.com" });
        var tenant = await tenantResponse.Content.ReadFromJsonAsync<CreateTenantResponse>();

        var (client, _) = _factory.CreateAuthenticatedClient(tenant!.Id.ToString(), Roles.Member);
        var response = await client.GetAsync($"/api/v1/tenants/{tenant.Id}/trial");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Viewer — read-only ────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Viewer_CanReadDomains()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Viewer);

        var response = await client.GetAsync(DomainsUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Viewer_CannotWriteDomains()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Viewer);

        var response = await client.PostAsJsonAsync(DomainsUrl,
            new CreateDomainRequest { Hostname = $"viewer-{Guid.NewGuid():N}.example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Viewer_CanReadLinks()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Viewer);

        var response = await client.GetAsync(LinksUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Viewer_CannotWriteLinks()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Viewer);

        // POST to links requires link:write, which Viewer doesn't have
        var response = await client.PostAsJsonAsync(LinksUrl, new CreateLinkRequest
        {
            DomainId = Guid.NewGuid(),
            DestinationUrl = "https://example.com"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Viewer_CannotManageApiKeys()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Viewer);

        var response = await client.GetAsync(ApiKeysUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
