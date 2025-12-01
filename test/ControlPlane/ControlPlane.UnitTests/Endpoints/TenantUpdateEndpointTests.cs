using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Endpoints;

/// <summary>
/// Tests for PATCH /tenants/{tenantId}.
/// Covers partial updates, validation, auth, and state conflicts.
/// </summary>
public sealed class TenantUpdateEndpointTests : IAsyncDisposable
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

    private static string PatchUrl(Guid tenantId) => $"/api/v1/tenants/{tenantId}";

    // ── UpdateTenant — happy path ─────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_Name_Returns200WithUpdatedName()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.PatchAsJsonAsync(PatchUrl(tenant.Id),
            new UpdateTenantRequest { Name = "New Name Corp" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TenantDetailResponse>();
        body!.Name.Should().Be("New Name Corp");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_LogoUrl_Returns200WithUpdatedSettings()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.PatchAsJsonAsync(PatchUrl(tenant.Id),
            new UpdateTenantRequest { LogoUrl = "https://cdn.example.com/logo.png" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TenantDetailResponse>();
        body!.Settings.LogoUrl.Should().Be("https://cdn.example.com/logo.png");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_DefaultRedirectType_Returns200WithUpdatedSettings()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.PatchAsJsonAsync(PatchUrl(tenant.Id),
            new UpdateTenantRequest { DefaultRedirectType = "301" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TenantDetailResponse>();
        body!.Settings.DefaultRedirectType.Should().Be("301");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_NotificationsDisabled_Returns200WithUpdatedSettings()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.PatchAsJsonAsync(PatchUrl(tenant.Id),
            new UpdateTenantRequest { NotificationsEnabled = false });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TenantDetailResponse>();
        body!.Settings.NotificationsEnabled.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_NotificationEmail_Returns200WithNormalizedEmail()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.PatchAsJsonAsync(PatchUrl(tenant.Id),
            new UpdateTenantRequest { NotificationEmail = "ALERTS@EXAMPLE.COM" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TenantDetailResponse>();
        body!.Settings.NotificationEmail.Should().Be("alerts@example.com");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_PartialUpdate_OmittedFieldsUnchanged()
    {
        var tenant = await CreateTenantAsync("partial@example.com");
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);
        // First set a logo and redirect type
        await client.PatchAsJsonAsync(PatchUrl(tenant.Id), new UpdateTenantRequest
        {
            LogoUrl = "https://cdn.example.com/logo.png",
            DefaultRedirectType = "302"
        });

        // Now update only the name — other fields must remain unchanged
        var response = await client.PatchAsJsonAsync(PatchUrl(tenant.Id),
            new UpdateTenantRequest { Name = "Updated Name" });

        var body = await response.Content.ReadFromJsonAsync<TenantDetailResponse>();
        body!.Name.Should().Be("Updated Name");
        body.Settings.LogoUrl.Should().Be("https://cdn.example.com/logo.png");
        body.Settings.DefaultRedirectType.Should().Be("302");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_EmptyRequest_Returns200Unchanged()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.PatchAsJsonAsync(PatchUrl(tenant.Id), new UpdateTenantRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TenantDetailResponse>();
        body!.Name.Should().Be("Test Corp");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_PlanNotUpdatable_PlanUnchanged()
    {
        // Plan is not in UpdateTenantRequest; the plan should remain from the original tenant.
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        await client.PatchAsJsonAsync(PatchUrl(tenant.Id), new UpdateTenantRequest { Name = "Renamed" });

        var get = await client.GetAsync(PatchUrl(tenant.Id));
        var body = await get.Content.ReadFromJsonAsync<TenantDetailResponse>();
        body!.Plan.Should().Be(tenant.Plan);
        body.Status.Should().Be(tenant.Status);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_StatePersistedInRepository()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        await client.PatchAsJsonAsync(PatchUrl(tenant.Id),
            new UpdateTenantRequest { Name = "Persisted Name" });

        var repo = _factory.Services.GetRequiredService<ITenantRepository>();
        var stored = await repo.GetByIdAsync(tenant.Id);
        stored!.Name.Should().Be("Persisted Name");
        stored.UpdatedAt.Should().NotBeNull();
        stored.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_AdminRole_Returns200()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Admin);

        var response = await client.PatchAsJsonAsync(PatchUrl(tenant.Id),
            new UpdateTenantRequest { Name = "Admin Updated" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── UpdateTenant — validation errors ─────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_NameTooShort_Returns400()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.PatchAsJsonAsync(PatchUrl(tenant.Id),
            new UpdateTenantRequest { Name = "X" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_NameTooLong_Returns400()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.PatchAsJsonAsync(PatchUrl(tenant.Id),
            new UpdateTenantRequest { Name = new string('a', 101) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_InvalidLogoUrl_Returns400()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.PatchAsJsonAsync(PatchUrl(tenant.Id),
            new UpdateTenantRequest { LogoUrl = "not-a-url" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_HttpLogoUrl_Returns400()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        // Logo must be HTTPS
        var response = await client.PatchAsJsonAsync(PatchUrl(tenant.Id),
            new UpdateTenantRequest { LogoUrl = "http://cdn.example.com/logo.png" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_InvalidRedirectType_Returns400()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.PatchAsJsonAsync(PatchUrl(tenant.Id),
            new UpdateTenantRequest { DefaultRedirectType = "999" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_InvalidNotificationEmail_Returns400()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.PatchAsJsonAsync(PatchUrl(tenant.Id),
            new UpdateTenantRequest { NotificationEmail = "not-an-email" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── UpdateTenant — state conflicts ────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_DeletedTenant_Returns409()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);
        await client.DeleteAsync(PatchUrl(tenant.Id));

        var response = await client.PatchAsJsonAsync(PatchUrl(tenant.Id),
            new UpdateTenantRequest { Name = "Should Fail" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_SuspendedTenant_Returns200()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);
        await client.PostAsJsonAsync($"/api/v1/tenants/{tenant.Id}/suspend", new SuspendTenantRequest());

        var response = await client.PatchAsJsonAsync(PatchUrl(tenant.Id),
            new UpdateTenantRequest { Name = "Suspended But Updated" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TenantDetailResponse>();
        body!.Name.Should().Be("Suspended But Updated");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_UnknownTenant_Returns404()
    {
        var unknownId = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(unknownId.ToString(), Roles.Owner);

        var response = await client.PatchAsJsonAsync(PatchUrl(unknownId),
            new UpdateTenantRequest { Name = "Should Fail" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── UpdateTenant — authorization ──────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_WithoutAuth_Returns401()
    {
        var tenant = await CreateTenantAsync();
        var client = _factory.CreateClient();

        var response = await client.PatchAsJsonAsync(PatchUrl(tenant.Id),
            new UpdateTenantRequest { Name = "Unauthorized" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_CrossTenant_Returns403()
    {
        var tenant = await CreateTenantAsync();
        var otherTenantId = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(otherTenantId.ToString(), Roles.Owner);

        var response = await client.PatchAsJsonAsync(PatchUrl(tenant.Id),
            new UpdateTenantRequest { Name = "Cross Tenant" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_ViewerRole_Returns403()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Viewer);

        var response = await client.PatchAsJsonAsync(PatchUrl(tenant.Id),
            new UpdateTenantRequest { Name = "Viewer Cannot Write" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateTenant_MemberRole_Returns403()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Member);

        var response = await client.PatchAsJsonAsync(PatchUrl(tenant.Id),
            new UpdateTenantRequest { Name = "Member Cannot Write Tenant" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
