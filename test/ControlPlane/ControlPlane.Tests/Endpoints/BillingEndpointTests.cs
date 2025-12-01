using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.Tests.Endpoints;

[Collection("DynamoDB")]
public sealed class BillingEndpointTests : IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;

    public BillingEndpointTests(LocalStackFixture localStack)
    {
        _factory = new CustomWebApplicationFactory(localStack.DynamoDb, localStack.TableName);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<(HttpClient Client, Guid TenantId)> CreateTenantWithSubscriptionAsync(
        string plan = "pro", string role = Roles.Owner)
    {
        var unauthenticatedClient = _factory.CreateClient();
        var createResponse = await unauthenticatedClient.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Billing Corp", Email = $"billing-{Guid.NewGuid():N}@test.com" });
        createResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);

        var body = await createResponse.Content.ReadFromJsonAsync<CreateTenantResponse>();
        var tenantId = body!.Id;

        // Assign a Stripe subscription to the tenant via the repository.
        var repo = _factory.Services.GetRequiredService<ITenantRepository>();
        var tenant = await repo.GetByIdAsync(tenantId);
        tenant.Should().NotBeNull();
        var updated = tenant! with
        {
            StripeSubscriptionId = "sub_test_abc123",
            Plan = plan,
            MaxDomains = PlanCatalog.Get(plan).MaxDomains,
            MaxLinksPerDomain = PlanCatalog.Get(plan).MaxLinksPerDomain
        };
        await repo.UpdateAsync(updated);

        // Wire up the test billing service with real repositories for usage checks.
        var billing = _factory.Billing;
        billing.TenantRepository = repo;
        billing.DomainRepository = _factory.Services.GetRequiredService<IDomainRepository>();
        billing.LinkRepository = _factory.Services.GetRequiredService<ILinkRepository>();
        billing.NotificationService = _factory.PlanChangeNotification;

        var (client, _) = _factory.CreateAuthenticatedClient(tenantId.ToString(), role);
        return (client, tenantId);
    }

    // ── POST /tenants/{tenantId}/billing/upgrade ───────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpgradePlan_ValidUpgrade_Returns200WithImmediateChange()
    {
        var (client, tenantId) = await CreateTenantWithSubscriptionAsync("starter");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/billing/upgrade",
            new UpgradePlanRequest { Plan = "pro" });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PlanChangeResponse>();
        body.Should().NotBeNull();
        body!.CurrentPlan.Should().Be("pro");
        body.ScheduledPlan.Should().BeNull();
        body.ScheduledChangeAt.Should().BeNull();
        body.Warnings.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpgradePlan_ToLowerOrEqualPlan_Returns400()
    {
        var (client, tenantId) = await CreateTenantWithSubscriptionAsync("pro");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/billing/upgrade",
            new UpgradePlanRequest { Plan = "starter" });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpgradePlan_ToSamePlan_Returns400()
    {
        var (client, tenantId) = await CreateTenantWithSubscriptionAsync("pro");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/billing/upgrade",
            new UpgradePlanRequest { Plan = "pro" });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpgradePlan_UnknownPlan_Returns400()
    {
        var (client, tenantId) = await CreateTenantWithSubscriptionAsync("pro");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/billing/upgrade",
            new UpgradePlanRequest { Plan = "nonexistent" });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpgradePlan_WithoutBillingWritePermission_Returns403()
    {
        var (client, tenantId) = await CreateTenantWithSubscriptionAsync("starter", Roles.Viewer);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/billing/upgrade",
            new UpgradePlanRequest { Plan = "pro" });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpgradePlan_DeletedTenant_Returns409()
    {
        var (client, tenantId) = await CreateTenantWithSubscriptionAsync("starter");
        var repo = _factory.Services.GetRequiredService<ITenantRepository>();
        var tenant = await repo.GetByIdAsync(tenantId);
        await repo.UpdateAsync(tenant! with { Status = "deleted", DeletedAt = DateTime.UtcNow });

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/billing/upgrade",
            new UpgradePlanRequest { Plan = "pro" });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpgradePlan_SendsNotification()
    {
        var (client, tenantId) = await CreateTenantWithSubscriptionAsync("starter");

        await client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/billing/upgrade",
            new UpgradePlanRequest { Plan = "pro" });

        var notifications = _factory.PlanChangeNotification.Notifications;
        notifications.Should().ContainSingle(n => n.Type == "upgraded");
    }

    // ── POST /tenants/{tenantId}/billing/downgrade ─────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DowngradePlan_ValidDowngrade_Returns200WithScheduledChange()
    {
        var (client, tenantId) = await CreateTenantWithSubscriptionAsync("pro");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/billing/downgrade",
            new DowngradePlanRequest { Plan = "starter" });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PlanChangeResponse>();
        body.Should().NotBeNull();
        body!.CurrentPlan.Should().Be("pro"); // Plan doesn't change yet
        body.ScheduledPlan.Should().Be("starter");
        body.ScheduledChangeAt.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DowngradePlan_EnforcesNewLimitsImmediately()
    {
        var (client, tenantId) = await CreateTenantWithSubscriptionAsync("pro");

        await client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/billing/downgrade",
            new DowngradePlanRequest { Plan = "starter" });

        // Verify the tenant's limits were updated to starter plan limits.
        var repo = _factory.Services.GetRequiredService<ITenantRepository>();
        var tenant = await repo.GetByIdAsync(tenantId);
        tenant.Should().NotBeNull();
        tenant!.MaxDomains.Should().Be(PlanCatalog.Get("starter").MaxDomains);
        tenant.MaxLinksPerDomain.Should().Be(PlanCatalog.Get("starter").MaxLinksPerDomain);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DowngradePlan_ToHigherOrEqualPlan_Returns400()
    {
        var (client, tenantId) = await CreateTenantWithSubscriptionAsync("starter");

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/billing/downgrade",
            new DowngradePlanRequest { Plan = "pro" });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DowngradePlan_SendsNotification()
    {
        var (client, tenantId) = await CreateTenantWithSubscriptionAsync("pro");

        await client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantId}/billing/downgrade",
            new DowngradePlanRequest { Plan = "starter" });

        var notifications = _factory.PlanChangeNotification.Notifications;
        notifications.Should().ContainSingle(n => n.Type == "downgrade_scheduled");
    }

    // ── POST /tenants/{tenantId}/billing/cancel ────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CancelSubscription_ValidCancel_Returns200WithPeriodEnd()
    {
        var (client, tenantId) = await CreateTenantWithSubscriptionAsync("pro");

        var response = await client.PostAsync(
            $"/api/v1/tenants/{tenantId}/billing/cancel", null);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CancelSubscriptionResponse>();
        body.Should().NotBeNull();
        body!.PeriodEnd.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CancelSubscription_WithoutSubscription_Returns400()
    {
        var (client, tenantId) = await CreateTenantWithSubscriptionAsync("pro");
        var repo = _factory.Services.GetRequiredService<ITenantRepository>();
        var tenant = await repo.GetByIdAsync(tenantId);
        await repo.UpdateAsync(tenant! with { StripeSubscriptionId = null });

        var response = await client.PostAsync(
            $"/api/v1/tenants/{tenantId}/billing/cancel", null);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CancelSubscription_SendsNotification()
    {
        var (client, tenantId) = await CreateTenantWithSubscriptionAsync("pro");

        await client.PostAsync(
            $"/api/v1/tenants/{tenantId}/billing/cancel", null);

        var notifications = _factory.PlanChangeNotification.Notifications;
        notifications.Should().ContainSingle(n => n.Type == "cancelled");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CancelSubscription_DeletedTenant_Returns409()
    {
        var (client, tenantId) = await CreateTenantWithSubscriptionAsync("pro");
        var repo = _factory.Services.GetRequiredService<ITenantRepository>();
        var tenant = await repo.GetByIdAsync(tenantId);
        await repo.UpdateAsync(tenant! with { Status = "deleted", DeletedAt = DateTime.UtcNow });

        var response = await client.PostAsync(
            $"/api/v1/tenants/{tenantId}/billing/cancel", null);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
    }

    // ── Cleanup ────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
