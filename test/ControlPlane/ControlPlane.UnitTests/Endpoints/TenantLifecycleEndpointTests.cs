using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Endpoints;

/// <summary>
/// Tests for tenant lifecycle endpoints: suspend, reactivate, delete.
/// Covers happy paths, error cases (404/409), auth (401/403), and audit log entries.
/// </summary>
public sealed class TenantLifecycleEndpointTests : IAsyncDisposable
{
    private readonly UnitTestWebApplicationFactory _factory = new();

    // ── Helpers ───────────────────────────────────────────────────────────────

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

    private string SuspendUrl(Guid tenantId) => $"/api/v1/tenants/{tenantId}/suspend";
    private string ReactivateUrl(Guid tenantId) => $"/api/v1/tenants/{tenantId}/reactivate";
    private string DeleteUrl(Guid tenantId) => $"/api/v1/tenants/{tenantId}";

    // ── SuspendTenant — happy path ────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SuspendTenant_ActiveTenant_Returns200WithSuspendedStatus()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.PostAsJsonAsync(SuspendUrl(tenant.Id), new SuspendTenantRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TenantStateResponse>();
        body!.Id.Should().Be(tenant.Id);
        body.Status.Should().Be("suspended");
        body.SuspendedAt.Should().NotBeNull();
        body.SuspendedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
        body.DeletedAt.Should().BeNull();
        body.PurgesAt.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SuspendTenant_WithReason_ReasonReturnedInResponse()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Admin);

        var response = await client.PostAsJsonAsync(SuspendUrl(tenant.Id),
            new SuspendTenantRequest { Reason = "Abuse detected." });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TenantStateResponse>();
        body!.SuspendedReason.Should().Be("Abuse detected.");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SuspendTenant_WithoutReason_SuspendedReasonIsNull()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.PostAsJsonAsync(SuspendUrl(tenant.Id), new SuspendTenantRequest());

        var body = await response.Content.ReadFromJsonAsync<TenantStateResponse>();
        body!.SuspendedReason.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SuspendTenant_StatePersistedInRepository()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        await client.PostAsJsonAsync(SuspendUrl(tenant.Id),
            new SuspendTenantRequest { Reason = "Policy violation." });

        var repo = _factory.Services.GetRequiredService<ITenantRepository>();
        var stored = await repo.GetByIdAsync(tenant.Id);
        stored!.Status.Should().Be("suspended");
        stored.SuspendedAt.Should().NotBeNull();
        stored.SuspendedReason.Should().Be("Policy violation.");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SuspendTenant_CreatesAuditLogEntry()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        await client.PostAsJsonAsync(SuspendUrl(tenant.Id),
            new SuspendTenantRequest { Reason = "Audit test." });

        var auditLog = _factory.Services.GetRequiredService<IAuditLogService>();
        var page = await auditLog.QueryAsync(tenant.Id);
        page.Items.Should().ContainSingle(e => e.Action == "tenant.suspended");
        page.Items.Single(e => e.Action == "tenant.suspended").Details.Should().Be("Audit test.");
    }

    // ── SuspendTenant — error cases ───────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SuspendTenant_UnknownTenant_Returns404()
    {
        var unknownId = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(unknownId.ToString(), Roles.Owner);

        var response = await client.PostAsJsonAsync(SuspendUrl(unknownId), new SuspendTenantRequest());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SuspendTenant_AlreadySuspended_Returns409()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        await client.PostAsJsonAsync(SuspendUrl(tenant.Id), new SuspendTenantRequest());
        var second = await client.PostAsJsonAsync(SuspendUrl(tenant.Id), new SuspendTenantRequest());

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SuspendTenant_DeletedTenant_Returns409()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);
        await client.DeleteAsync(DeleteUrl(tenant.Id));

        var response = await client.PostAsJsonAsync(SuspendUrl(tenant.Id), new SuspendTenantRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SuspendTenant_ReasonExceeds500Chars_Returns400()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.PostAsJsonAsync(SuspendUrl(tenant.Id),
            new SuspendTenantRequest { Reason = new string('x', 501) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── SuspendTenant — authorization ─────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SuspendTenant_WithoutAuth_Returns401()
    {
        var tenant = await CreateTenantAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(SuspendUrl(tenant.Id), new SuspendTenantRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SuspendTenant_CrossTenant_Returns403()
    {
        var tenant = await CreateTenantAsync();
        var otherTenantId = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(otherTenantId.ToString(), Roles.Owner);

        var response = await client.PostAsJsonAsync(SuspendUrl(tenant.Id), new SuspendTenantRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── ReactivateTenant — happy path ─────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ReactivateTenant_SuspendedTenant_Returns200WithActiveStatus()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);
        await client.PostAsJsonAsync(SuspendUrl(tenant.Id), new SuspendTenantRequest());

        var response = await client.PostAsJsonAsync(ReactivateUrl(tenant.Id), new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TenantStateResponse>();
        body!.Id.Should().Be(tenant.Id);
        body.SuspendedAt.Should().BeNull();
        body.SuspendedReason.Should().BeNull();
        body.DeletedAt.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ReactivateTenant_SuspendedTrialingTenant_RestoresTrialingStatus()
    {
        // New tenants start on trial (trialing). Suspend then reactivate should restore trialing.
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);
        await client.PostAsJsonAsync(SuspendUrl(tenant.Id), new SuspendTenantRequest());

        var response = await client.PostAsJsonAsync(ReactivateUrl(tenant.Id), new { });

        var body = await response.Content.ReadFromJsonAsync<TenantStateResponse>();
        body!.Status.Should().Be("trialing");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ReactivateTenant_SuspensionFieldsCleared()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);
        await client.PostAsJsonAsync(SuspendUrl(tenant.Id),
            new SuspendTenantRequest { Reason = "Temporary." });

        await client.PostAsJsonAsync(ReactivateUrl(tenant.Id), new { });

        var repo = _factory.Services.GetRequiredService<ITenantRepository>();
        var stored = await repo.GetByIdAsync(tenant.Id);
        stored!.SuspendedAt.Should().BeNull();
        stored.SuspendedReason.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ReactivateTenant_CreatesAuditLogEntry()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);
        await client.PostAsJsonAsync(SuspendUrl(tenant.Id), new SuspendTenantRequest());

        await client.PostAsJsonAsync(ReactivateUrl(tenant.Id), new { });

        var auditLog = _factory.Services.GetRequiredService<IAuditLogService>();
        var page = await auditLog.QueryAsync(tenant.Id);
        page.Items.Should().Contain(e => e.Action == "tenant.reactivated");
    }

    // ── ReactivateTenant — error cases ────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ReactivateTenant_ActiveTenant_Returns409()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.PostAsJsonAsync(ReactivateUrl(tenant.Id), new { });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ReactivateTenant_DeletedTenant_Returns409()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);
        await client.DeleteAsync(DeleteUrl(tenant.Id));

        var response = await client.PostAsJsonAsync(ReactivateUrl(tenant.Id), new { });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ReactivateTenant_UnknownTenant_Returns404()
    {
        var unknownId = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(unknownId.ToString(), Roles.Owner);

        var response = await client.PostAsJsonAsync(ReactivateUrl(unknownId), new { });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── ReactivateTenant — authorization ──────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ReactivateTenant_WithoutAuth_Returns401()
    {
        var tenant = await CreateTenantAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(ReactivateUrl(tenant.Id), new { });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ReactivateTenant_CrossTenant_Returns403()
    {
        var tenant = await CreateTenantAsync();
        var otherTenantId = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(otherTenantId.ToString(), Roles.Owner);

        var response = await client.PostAsJsonAsync(ReactivateUrl(tenant.Id), new { });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── DeleteTenant — happy path ─────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteTenant_ActiveTenant_Returns200WithDeletedStatus()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.DeleteAsync(DeleteUrl(tenant.Id));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TenantStateResponse>();
        body!.Id.Should().Be(tenant.Id);
        body.Status.Should().Be("deleted");
        body.DeletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteTenant_ResponseIncludesPurgesAt30DaysOut()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.DeleteAsync(DeleteUrl(tenant.Id));

        var body = await response.Content.ReadFromJsonAsync<TenantStateResponse>();
        body!.PurgesAt.Should().NotBeNull();
        body.PurgesAt!.Value.Should().BeCloseTo(
            DateTime.UtcNow.AddDays(30), TimeSpan.FromSeconds(10));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteTenant_SuspendedTenant_Returns200WithDeletedStatus()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);
        await client.PostAsJsonAsync(SuspendUrl(tenant.Id), new SuspendTenantRequest());

        var response = await client.DeleteAsync(DeleteUrl(tenant.Id));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TenantStateResponse>();
        body!.Status.Should().Be("deleted");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteTenant_StatePersistedInRepository()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        await client.DeleteAsync(DeleteUrl(tenant.Id));

        var repo = _factory.Services.GetRequiredService<ITenantRepository>();
        var stored = await repo.GetByIdAsync(tenant.Id);
        stored!.Status.Should().Be("deleted");
        stored.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteTenant_CreatesAuditLogEntry()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        await client.DeleteAsync(DeleteUrl(tenant.Id));

        var auditLog = _factory.Services.GetRequiredService<IAuditLogService>();
        var page = await auditLog.QueryAsync(tenant.Id);
        page.Items.Should().ContainSingle(e => e.Action == "tenant.deleted");
    }

    // ── DeleteTenant — error cases ────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteTenant_AlreadyDeleted_Returns409()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);
        await client.DeleteAsync(DeleteUrl(tenant.Id));

        var second = await client.DeleteAsync(DeleteUrl(tenant.Id));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteTenant_UnknownTenant_Returns404()
    {
        var unknownId = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(unknownId.ToString(), Roles.Owner);

        var response = await client.DeleteAsync(DeleteUrl(unknownId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── DeleteTenant — authorization ──────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteTenant_WithoutAuth_Returns401()
    {
        var tenant = await CreateTenantAsync();
        var client = _factory.CreateClient();

        var response = await client.DeleteAsync(DeleteUrl(tenant.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteTenant_CrossTenant_Returns403()
    {
        var tenant = await CreateTenantAsync();
        var otherTenantId = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(otherTenantId.ToString(), Roles.Owner);

        var response = await client.DeleteAsync(DeleteUrl(tenant.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Full lifecycle ─────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task FullLifecycle_CreateSuspendReactivateDelete()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var suspend = await client.PostAsJsonAsync(SuspendUrl(tenant.Id),
            new SuspendTenantRequest { Reason = "Billing." });
        suspend.StatusCode.Should().Be(HttpStatusCode.OK);

        var reactivate = await client.PostAsJsonAsync(ReactivateUrl(tenant.Id), new { });
        reactivate.StatusCode.Should().Be(HttpStatusCode.OK);

        var delete = await client.DeleteAsync(DeleteUrl(tenant.Id));
        delete.StatusCode.Should().Be(HttpStatusCode.OK);

        var auditLog = _factory.Services.GetRequiredService<IAuditLogService>();
        var page = await auditLog.QueryAsync(tenant.Id);
        page.Items.Select(e => e.Action).Should().Contain(
            ["tenant.suspended", "tenant.reactivated", "tenant.deleted"]);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
