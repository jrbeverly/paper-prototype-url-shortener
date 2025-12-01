using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Endpoints;

/// <summary>
/// Tests for GET /api/v1/tenants/{tenantId}/audit-logs.
/// Covers happy-path retrieval, filtering, auth/403, and immutability guarantee.
/// </summary>
public sealed class AuditLogEndpointTests : IAsyncDisposable
{
    private readonly UnitTestWebApplicationFactory _factory = new();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<CreateTenantResponse> CreateTenantAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/tenants", new CreateTenantRequest
        {
            Name = "Audit Corp",
            Email = $"audit-{Guid.NewGuid():N}@example.com"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateTenantResponse>())!;
    }

    private string AuditLogsUrl(Guid tenantId) => $"/api/v1/tenants/{tenantId}/audit-logs";

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task QueryAuditLogs_NewTenant_ReturnsCreatedEntry()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.GetAsync(AuditLogsUrl(tenant.Id));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuditLogListResponse>();
        body!.Items.Should().Contain(e => e.Action == "tenant.created");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task QueryAuditLogs_EntryHasRequiredFields()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);
        await client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenant.Id}/suspend",
            new SuspendTenantRequest { Reason = "Test." });

        var response = await client.GetAsync(AuditLogsUrl(tenant.Id));

        var body = await response.Content.ReadFromJsonAsync<AuditLogListResponse>();
        var entry = body!.Items.First(e => e.Action == "tenant.suspended");
        entry.Id.Should().NotBeEmpty();
        entry.ActorId.Should().NotBeNullOrEmpty();
        entry.ActorType.Should().NotBeNullOrEmpty();
        entry.ResourceType.Should().Be("tenant");
        entry.ResourceId.Should().Be(tenant.Id.ToString());
        entry.OldValue.Should().NotBeNullOrEmpty();
        entry.NewValue.Should().NotBeNullOrEmpty();
        entry.Details.Should().Be("Test.");
        entry.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task QueryAuditLogs_ResultsAreNewestFirst()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);
        await client.PostAsJsonAsync($"/api/v1/tenants/{tenant.Id}/suspend", new SuspendTenantRequest());
        await client.PostAsJsonAsync($"/api/v1/tenants/{tenant.Id}/reactivate", new { });

        var response = await client.GetAsync(AuditLogsUrl(tenant.Id));

        var body = await response.Content.ReadFromJsonAsync<AuditLogListResponse>();
        var actions = body!.Items.Select(e => e.Action).ToList();
        // Newest first: reactivated before suspended before created
        actions.IndexOf("tenant.reactivated").Should().BeLessThan(actions.IndexOf("tenant.suspended"));
        actions.IndexOf("tenant.suspended").Should().BeLessThan(actions.IndexOf("tenant.created"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task QueryAuditLogs_ResponseIncludesPagination()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.GetAsync(AuditLogsUrl(tenant.Id));

        var body = await response.Content.ReadFromJsonAsync<AuditLogListResponse>();
        body!.Page.Should().Be(1);
        body.PageSize.Should().BeGreaterThan(0);
        body.TotalCount.Should().BeGreaterThan(0);
    }

    // ── Filtering ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task QueryAuditLogs_FilterByAction_ReturnsOnlyMatchingEntries()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);
        await client.PostAsJsonAsync($"/api/v1/tenants/{tenant.Id}/suspend", new SuspendTenantRequest());
        await client.PostAsJsonAsync($"/api/v1/tenants/{tenant.Id}/reactivate", new { });

        var response = await client.GetAsync($"{AuditLogsUrl(tenant.Id)}?action=tenant.suspended");

        var body = await response.Content.ReadFromJsonAsync<AuditLogListResponse>();
        body!.Items.Should().AllSatisfy(e => e.Action.Should().StartWith("tenant.suspended"));
        body.Items.Should().NotContain(e => e.Action == "tenant.reactivated");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task QueryAuditLogs_FilterByActionPrefix_ReturnsAllMatchingResourceEvents()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);
        await client.PostAsJsonAsync($"/api/v1/tenants/{tenant.Id}/suspend", new SuspendTenantRequest());
        await client.PostAsJsonAsync($"/api/v1/tenants/{tenant.Id}/reactivate", new { });

        // "tenant." prefix should match created + suspended + reactivated
        var response = await client.GetAsync($"{AuditLogsUrl(tenant.Id)}?action=tenant.");

        var body = await response.Content.ReadFromJsonAsync<AuditLogListResponse>();
        body!.Items.Should().AllSatisfy(e => e.Action.Should().StartWith("tenant."));
        body.Items.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task QueryAuditLogs_FilterByResourceType_ReturnsOnlyThatType()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);
        await client.PostAsJsonAsync($"/api/v1/tenants/{tenant.Id}/suspend", new SuspendTenantRequest());

        var response = await client.GetAsync($"{AuditLogsUrl(tenant.Id)}?resourceType=tenant");

        var body = await response.Content.ReadFromJsonAsync<AuditLogListResponse>();
        body!.Items.Should().AllSatisfy(e => e.ResourceType.Should().Be("tenant"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task QueryAuditLogs_FilterByTimeRange_ExcludesOutOfRangeEntries()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        // Ask for entries in the far future — should be empty
        var futureFrom = DateTime.UtcNow.AddYears(1).ToString("O");
        var response = await client.GetAsync($"{AuditLogsUrl(tenant.Id)}?from={futureFrom}");

        var body = await response.Content.ReadFromJsonAsync<AuditLogListResponse>();
        body!.Items.Should().BeEmpty();
        body.TotalCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task QueryAuditLogs_Pagination_SecondPageIsEmpty()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        // Page 2 with pageSize=100 when there's only 1 entry
        var response = await client.GetAsync($"{AuditLogsUrl(tenant.Id)}?page=2&pageSize=100");

        var body = await response.Content.ReadFromJsonAsync<AuditLogListResponse>();
        body!.Items.Should().BeEmpty();
        body.TotalCount.Should().BeGreaterThan(0);  // total count reflects all entries, not just this page
    }

    // ── Immutability ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AuditLog_NoDeleteOrUpdateRouteExists()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        // These routes must not exist (audit log is append-only)
        var deleteResponse = await client.DeleteAsync($"{AuditLogsUrl(tenant.Id)}/{Guid.NewGuid()}");
        var putResponse = await client.PutAsJsonAsync($"{AuditLogsUrl(tenant.Id)}/{Guid.NewGuid()}", new { });

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        putResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Authorization ─────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task QueryAuditLogs_WithoutAuth_Returns401()
    {
        var tenant = await CreateTenantAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync(AuditLogsUrl(tenant.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task QueryAuditLogs_MemberRole_Returns403()
    {
        // Members don't have audit:read permission
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Member);

        var response = await client.GetAsync(AuditLogsUrl(tenant.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task QueryAuditLogs_ViewerRole_Returns403()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Viewer);

        var response = await client.GetAsync(AuditLogsUrl(tenant.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task QueryAuditLogs_AdminRole_Returns200()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Admin);

        var response = await client.GetAsync(AuditLogsUrl(tenant.Id));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task QueryAuditLogs_CrossTenant_Returns403()
    {
        var tenant = await CreateTenantAsync();
        var otherTenantId = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(otherTenantId.ToString(), Roles.Owner);

        var response = await client.GetAsync(AuditLogsUrl(tenant.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Audit entries for other resources ─────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task QueryAuditLogs_TenantCreate_RecordsCorrectFields()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.GetAsync(AuditLogsUrl(tenant.Id));

        var body = await response.Content.ReadFromJsonAsync<AuditLogListResponse>();
        var entry = body!.Items.Single(e => e.Action == "tenant.created");
        entry.ResourceType.Should().Be("tenant");
        entry.ResourceId.Should().Be(tenant.Id.ToString());
        entry.OldValue.Should().BeNull();
        entry.NewValue.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task QueryAuditLogs_TenantSuspend_RecordsBeforeAndAfterState()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);
        await client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenant.Id}/suspend",
            new SuspendTenantRequest { Reason = "Billing failure." });

        var response = await client.GetAsync(AuditLogsUrl(tenant.Id));

        var body = await response.Content.ReadFromJsonAsync<AuditLogListResponse>();
        var entry = body!.Items.Single(e => e.Action == "tenant.suspended");
        entry.OldValue.Should().Contain("status");
        entry.NewValue.Should().Contain("suspended");
        entry.Details.Should().Be("Billing failure.");
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
