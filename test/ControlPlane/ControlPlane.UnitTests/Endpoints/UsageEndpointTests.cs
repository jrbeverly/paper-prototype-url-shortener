using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Endpoints;

/// <summary>
/// Tests for GET /tenants/{tenantId}/usage.
/// Covers: happy path, click tracking, overage, alert levels, domain/link counts, auth.
/// </summary>
public sealed class UsageEndpointTests : IAsyncDisposable
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

    private static string UsageUrl(Guid tenantId) => $"/api/v1/tenants/{tenantId}/usage";

    private async Task TrackClicksAsync(Guid tenantId, long count)
    {
        var usageRepo = _factory.Services.GetRequiredService<IUsageRepository>();
        var (start, end) = UsageService.GetCurrentBillingPeriod(DateTime.UtcNow);
        await usageRepo.IncrementClicksAsync(tenantId, start, end, count);
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCurrentUsage_NewTenant_Returns200WithZeroClicks()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.GetAsync(UsageUrl(tenant.Id));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UsageResponse>();
        body.Should().NotBeNull();
        body!.TenantId.Should().Be(tenant.Id);
        body.Current.TrackedClicks.Should().Be(0);
        body.Overage.Clicks.Should().Be(0);
        body.AlertLevel.Should().Be("none");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCurrentUsage_ReturnsActivePlan()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.GetAsync(UsageUrl(tenant.Id));

        var body = await response.Content.ReadFromJsonAsync<UsageResponse>();
        // New tenants start on Pro trial
        body!.Plan.Should().Be("pro");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCurrentUsage_PeriodStartIsFirstOfCurrentMonth()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.GetAsync(UsageUrl(tenant.Id));

        var body = await response.Content.ReadFromJsonAsync<UsageResponse>();
        var expectedStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        body!.Period.Start.Should().Be(expectedStart);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCurrentUsage_AfterTrackingClicks_ReturnsTrackedCount()
    {
        var tenant = await CreateTenantAsync();
        await TrackClicksAsync(tenant.Id, 42);
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.GetAsync(UsageUrl(tenant.Id));

        var body = await response.Content.ReadFromJsonAsync<UsageResponse>();
        body!.Current.TrackedClicks.Should().Be(42);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCurrentUsage_IncludesCorrectPlanLimits()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.GetAsync(UsageUrl(tenant.Id));

        var body = await response.Content.ReadFromJsonAsync<UsageResponse>();
        var proPlan = PlanCatalog.Get("pro");
        body!.Limits.MaxTrackedClicksPerMonth.Should().Be(proPlan.MaxTrackedClicksPerMonth);
        body.Limits.MaxDomains.Should().Be(proPlan.MaxDomains);
        body.Limits.MaxLinksPerDomain.Should().Be(proPlan.MaxLinksPerDomain);
    }

    // ── Alert levels ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCurrentUsage_Below80Percent_AlertLevelIsNone()
    {
        var tenant = await CreateTenantAsync();
        var plan = PlanCatalog.Get("pro");
        await TrackClicksAsync(tenant.Id, (long)(plan.MaxTrackedClicksPerMonth * 0.79));
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.GetAsync(UsageUrl(tenant.Id));

        var body = await response.Content.ReadFromJsonAsync<UsageResponse>();
        body!.AlertLevel.Should().Be("none");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCurrentUsage_At80Percent_AlertLevelIsWarning80()
    {
        var tenant = await CreateTenantAsync();
        var plan = PlanCatalog.Get("pro");
        await TrackClicksAsync(tenant.Id, (long)(plan.MaxTrackedClicksPerMonth * 0.80));
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.GetAsync(UsageUrl(tenant.Id));

        var body = await response.Content.ReadFromJsonAsync<UsageResponse>();
        body!.AlertLevel.Should().Be("warning80");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCurrentUsage_At90Percent_AlertLevelIsWarning90()
    {
        var tenant = await CreateTenantAsync();
        var plan = PlanCatalog.Get("pro");
        await TrackClicksAsync(tenant.Id, (long)(plan.MaxTrackedClicksPerMonth * 0.90));
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.GetAsync(UsageUrl(tenant.Id));

        var body = await response.Content.ReadFromJsonAsync<UsageResponse>();
        body!.AlertLevel.Should().Be("warning90");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCurrentUsage_AtLimit_AlertLevelIsLimitReached()
    {
        var tenant = await CreateTenantAsync();
        var plan = PlanCatalog.Get("pro");
        await TrackClicksAsync(tenant.Id, plan.MaxTrackedClicksPerMonth);
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.GetAsync(UsageUrl(tenant.Id));

        var body = await response.Content.ReadFromJsonAsync<UsageResponse>();
        body!.AlertLevel.Should().Be("limitReached");
    }

    // ── Overage ───────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCurrentUsage_WithinLimit_OverageIsZero()
    {
        var tenant = await CreateTenantAsync();
        var plan = PlanCatalog.Get("pro");
        await TrackClicksAsync(tenant.Id, plan.MaxTrackedClicksPerMonth - 1);
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.GetAsync(UsageUrl(tenant.Id));

        var body = await response.Content.ReadFromJsonAsync<UsageResponse>();
        body!.Overage.Clicks.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCurrentUsage_BeyondLimit_OverageIsCorrect()
    {
        var tenant = await CreateTenantAsync();
        var plan = PlanCatalog.Get("pro");
        await TrackClicksAsync(tenant.Id, plan.MaxTrackedClicksPerMonth + 500);
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.GetAsync(UsageUrl(tenant.Id));

        var body = await response.Content.ReadFromJsonAsync<UsageResponse>();
        body!.Overage.Clicks.Should().Be(500);
    }

    // ── Domain and link counts ─────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCurrentUsage_NoDomainsOrLinks_CountsAreZero()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        var response = await client.GetAsync(UsageUrl(tenant.Id));

        var body = await response.Content.ReadFromJsonAsync<UsageResponse>();
        body!.Current.ActiveDomains.Should().Be(0);
        body.Current.ActiveLinks.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCurrentUsage_ReflectsActiveDomainCount()
    {
        var tenant = await CreateTenantAsync();
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        // Create two domains
        await client.PostAsJsonAsync($"/api/v1/tenants/{tenant.Id}/domains",
            new CreateDomainRequest { Hostname = "a.example.com" });
        await client.PostAsJsonAsync($"/api/v1/tenants/{tenant.Id}/domains",
            new CreateDomainRequest { Hostname = "b.example.com" });

        var response = await client.GetAsync(UsageUrl(tenant.Id));

        var body = await response.Content.ReadFromJsonAsync<UsageResponse>();
        body!.Current.ActiveDomains.Should().Be(2);
    }

    // ── Usage alerts are sent ──────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCurrentUsage_At80Percent_SendsWarning80Alert()
    {
        var tenant = await CreateTenantAsync();
        var plan = PlanCatalog.Get("pro");
        await TrackClicksAsync(tenant.Id, (long)(plan.MaxTrackedClicksPerMonth * 0.80));
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        await client.GetAsync(UsageUrl(tenant.Id));

        var sentAlerts = _factory.UsageNotification.SentAlerts;
        sentAlerts.Should().Contain(a => a.TenantId == tenant.Id && a.Level == UsageAlertLevel.Warning80);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCurrentUsage_AlertNotDuplicated_OnRepeatedCalls()
    {
        var tenant = await CreateTenantAsync();
        var plan = PlanCatalog.Get("pro");
        await TrackClicksAsync(tenant.Id, (long)(plan.MaxTrackedClicksPerMonth * 0.80));
        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);

        // Call three times — alert should fire exactly once
        await client.GetAsync(UsageUrl(tenant.Id));
        await client.GetAsync(UsageUrl(tenant.Id));
        await client.GetAsync(UsageUrl(tenant.Id));

        var sentAlerts = _factory.UsageNotification.SentAlerts
            .Where(a => a.TenantId == tenant.Id && a.Level == UsageAlertLevel.Warning80)
            .ToList();
        sentAlerts.Should().HaveCount(1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TrackClick_Via_UsageService_IncrementsCount()
    {
        var tenant = await CreateTenantAsync();
        var usageService = _factory.Services.GetRequiredService<IUsageService>();

        await usageService.TrackClickAsync(tenant.Id, DateTime.UtcNow);
        await usageService.TrackClickAsync(tenant.Id, DateTime.UtcNow);
        await usageService.TrackClickAsync(tenant.Id, DateTime.UtcNow);

        var (client, _) = _factory.CreateAuthenticatedClient(tenant.Id.ToString(), Roles.Owner);
        var response = await client.GetAsync(UsageUrl(tenant.Id));
        var body = await response.Content.ReadFromJsonAsync<UsageResponse>();
        body!.Current.TrackedClicks.Should().Be(3);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TrackClick_At80Percent_SendsAlert()
    {
        var tenant = await CreateTenantAsync();
        var plan = PlanCatalog.Get("pro");
        var usageService = _factory.Services.GetRequiredService<IUsageService>();

        // Track exactly at 80% threshold
        var usageRepo = _factory.Services.GetRequiredService<IUsageRepository>();
        var (start, end) = UsageService.GetCurrentBillingPeriod(DateTime.UtcNow);
        await usageRepo.IncrementClicksAsync(tenant.Id, start, end, (long)(plan.MaxTrackedClicksPerMonth * 0.80) - 1);

        // This one click should cross 80%
        await usageService.TrackClickAsync(tenant.Id, DateTime.UtcNow);

        _factory.UsageNotification.SentAlerts
            .Should().Contain(a => a.TenantId == tenant.Id && a.Level == UsageAlertLevel.Warning80);
    }

    // ── Error cases ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCurrentUsage_UnknownTenant_Returns404()
    {
        var unknownId = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(unknownId.ToString(), Roles.Owner);

        var response = await client.GetAsync(UsageUrl(unknownId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Authorization ─────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCurrentUsage_WithoutAuth_Returns401()
    {
        var tenant = await CreateTenantAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync(UsageUrl(tenant.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCurrentUsage_CrossTenant_Returns403()
    {
        var tenant = await CreateTenantAsync();
        var otherTenantId = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(otherTenantId.ToString(), Roles.Owner);

        var response = await client.GetAsync(UsageUrl(tenant.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
