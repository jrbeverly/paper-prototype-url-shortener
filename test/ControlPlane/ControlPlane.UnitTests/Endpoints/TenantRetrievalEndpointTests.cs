using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Endpoints;

/// <summary>
/// Tests for GET /tenants/{tenantId} and GET /tenants (admin list).
/// </summary>
public sealed class TenantRetrievalEndpointTests : IAsyncDisposable
{
    private readonly UnitTestWebApplicationFactory _factory = new();

    private async Task<CreateTenantResponse> CreateTenantAsync(string? email = null)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/tenants", new CreateTenantRequest
        {
            Name = "Test Corp",
            Email = email ?? $"test-{Guid.NewGuid():N}@example.com"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateTenantResponse>())!;
    }

    private static string GetUrl(Guid tenantId) => $"/api/v1/tenants/{tenantId}";
    private const string _listUrl = "/api/v1/tenants";

    // ── GetTenant — happy path ────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetTenant_ExistingTenant_Returns200WithFullDetails()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.GetAsync(GetUrl(tenant.Id));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TenantDetailResponse>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(tenant.Id);
        body.Name.Should().Be("Test Corp");
        body.Email.Should().NotBeNullOrEmpty();
        body.Plan.Should().NotBeNullOrEmpty();
        body.Status.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetTenant_ResponseIncludesLimits()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.GetAsync(GetUrl(tenant.Id));

        var body = await response.Content.ReadFromJsonAsync<TenantDetailResponse>();
        body!.Limits.Should().NotBeNull();
        body.Limits.MaxDomains.Should().BeGreaterThan(0);
        body.Limits.MaxLinksPerDomain.Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetTenant_ResponseIncludesSettings()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.GetAsync(GetUrl(tenant.Id));

        var body = await response.Content.ReadFromJsonAsync<TenantDetailResponse>();
        body!.Settings.Should().NotBeNull();
        body.Settings.NotificationsEnabled.Should().BeTrue();
        body.Settings.LogoUrl.Should().BeNull();
        body.Settings.DefaultRedirectType.Should().BeNull();
        body.Settings.NotificationEmail.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetTenant_TrialingTenant_ResponseIncludesTrial()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.GetAsync(GetUrl(tenant.Id));

        var body = await response.Content.ReadFromJsonAsync<TenantDetailResponse>();
        body!.Trial.Should().NotBeNull();
        body.Trial!.IsOnTrial.Should().BeTrue();
        body.Trial.DaysRemaining.Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetTenant_ViewerRole_Returns200()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Viewer);

        var response = await client.GetAsync(GetUrl(tenant.Id));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetTenant_MemberRole_Returns200()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Member);

        var response = await client.GetAsync(GetUrl(tenant.Id));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── GetTenant — error cases ───────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetTenant_UnknownTenant_Returns404()
    {
        var unknownId = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(unknownId.ToString(), Roles.Owner);

        var response = await client.GetAsync(GetUrl(unknownId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetTenant_WithoutAuth_Returns401()
    {
        var tenant = await CreateTenantAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync(GetUrl(tenant.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetTenant_CrossTenant_Returns403()
    {
        var tenant = await CreateTenantAsync();
        var otherTenantId = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(otherTenantId.ToString(), Roles.Owner);

        var response = await client.GetAsync(GetUrl(tenant.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── ListTenants — happy path ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListTenants_AdminRole_Returns200WithItems()
    {
        await CreateTenantAsync("list1@example.com");
        await CreateTenantAsync("list2@example.com");
        var adminTenantId = Guid.NewGuid().ToString();
        var (client, _) = _factory.CreateAuthenticatedClient(adminTenantId, Roles.Admin);

        var response = await client.GetAsync(_listUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TenantListResponse>();
        body.Should().NotBeNull();
        body!.Items.Should().NotBeEmpty();
        body.TotalCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListTenants_ResponseIsPaginated()
    {
        var adminTenantId = Guid.NewGuid().ToString();
        var (client, _) = _factory.CreateAuthenticatedClient(adminTenantId, Roles.Admin);

        var response = await client.GetAsync($"{_listUrl}?page=1&pageSize=2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TenantListResponse>();
        body!.Page.Should().Be(1);
        body.PageSize.Should().Be(2);
        body.Items.Count.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListTenants_ItemsIncludeExpectedFields()
    {
        await CreateTenantAsync("fields@example.com");
        var adminTenantId = Guid.NewGuid().ToString();
        var (client, _) = _factory.CreateAuthenticatedClient(adminTenantId, Roles.Admin);

        var response = await client.GetAsync(_listUrl);

        var body = await response.Content.ReadFromJsonAsync<TenantListResponse>();
        var item = body!.Items.First(t => t.Email == "fields@example.com");
        item.Id.Should().NotBeEmpty();
        item.Name.Should().NotBeNullOrEmpty();
        item.Plan.Should().NotBeNullOrEmpty();
        item.Status.Should().NotBeNullOrEmpty();
        item.CreatedAt.Should().NotBe(default);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListTenants_PageSizeCappedAt100()
    {
        var adminTenantId = Guid.NewGuid().ToString();
        var (client, _) = _factory.CreateAuthenticatedClient(adminTenantId, Roles.Admin);

        var response = await client.GetAsync($"{_listUrl}?pageSize=9999");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TenantListResponse>();
        body!.PageSize.Should().Be(100);
    }

    // ── ListTenants — authorization ───────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListTenants_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(_listUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListTenants_MemberRole_Returns403()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(Guid.NewGuid().ToString(), Roles.Member);

        var response = await client.GetAsync(_listUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListTenants_ViewerRole_Returns403()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(Guid.NewGuid().ToString(), Roles.Viewer);

        var response = await client.GetAsync(_listUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListTenants_OwnerRole_Returns200()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(Guid.NewGuid().ToString(), Roles.Owner);

        var response = await client.GetAsync(_listUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
