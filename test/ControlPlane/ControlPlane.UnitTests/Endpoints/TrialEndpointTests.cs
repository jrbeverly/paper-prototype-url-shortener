using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;

namespace ControlPlane.UnitTests.Endpoints;

public sealed class TrialEndpointTests : IAsyncDisposable
{
    private readonly UnitTestWebApplicationFactory _factory = new();

    private async Task<(Guid TenantId, HttpClient Client, TestClaimsProvider Claims)> CreateTenantAndAuthAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Trial Tenant", Email = $"{Guid.NewGuid():N}@example.com" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var tenant = await response.Content.ReadFromJsonAsync<CreateTenantResponse>();

        var (authClient, claims) = _factory.CreateAuthenticatedClient(tenant!.Id.ToString(), Roles.Admin);
        return (tenant.Id, authClient, claims);
    }

    // ── GetTrialStatus ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetTrialStatus_NewTenant_ReturnsTrialingStatus()
    {
        var (tenantId, client, _) = await CreateTenantAndAuthAsync();

        var response = await client.GetAsync($"/api/v1/tenants/{tenantId}/trial");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TrialStatusResponse>();
        body!.IsOnTrial.Should().BeTrue();
        body.DaysRemaining.Should().BeGreaterThan(0);
        body.TrialPlan.Should().Be("pro");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetTrialStatus_NotFound_Returns404()
    {
        var (_, client, _) = await CreateTenantAndAuthAsync();
        var nonExistentId = Guid.NewGuid();
        var (badClient, _) = _factory.CreateAuthenticatedClient(nonExistentId.ToString(), Roles.Admin);

        var response = await badClient.GetAsync($"/api/v1/tenants/{nonExistentId}/trial");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── ExtendTrial ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExtendTrial_TrialingTenant_ReturnsDaysRemainingIncreased()
    {
        var (tenantId, client, _) = await CreateTenantAndAuthAsync();
        var before = await (await client.GetAsync($"/api/v1/tenants/{tenantId}/trial"))
            .Content.ReadFromJsonAsync<TrialStatusResponse>();

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/trial",
            new ExtendTrialRequest { AdditionalDays = 7 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await response.Content.ReadFromJsonAsync<TrialStatusResponse>();
        after!.DaysRemaining.Should().BeGreaterThan(before!.DaysRemaining!.Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExtendTrial_ZeroAdditionalDays_Returns400()
    {
        var (tenantId, client, _) = await CreateTenantAndAuthAsync();

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/trial",
            new ExtendTrialRequest { AdditionalDays = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExtendTrial_NotFound_Returns404()
    {
        var nonExistentId = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(nonExistentId.ToString(), Roles.Admin);

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{nonExistentId}/trial",
            new ExtendTrialRequest { AdditionalDays = 7 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── ProcessExpiredTrials (admin batch) ────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessExpiredTrials_NoExpiredTrials_Returns200WithZeroProcessed()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/v1/admin/trials/expire", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ProcessTrialsResponse>();
        body!.Processed.Should().Be(0);
    }

    // ── SendTrialNotifications (admin batch) ──────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task SendTrialNotifications_NoEligibleTenants_Returns200WithZeroProcessed()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/v1/admin/trials/notify", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ProcessTrialsResponse>();
        body!.Processed.Should().Be(0);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
