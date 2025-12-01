using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.Tests.Endpoints;

[Collection("DynamoDB")]
public sealed class PlanEndpointTests : IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;

    public PlanEndpointTests(LocalStackFixture localStack)
    {
        _factory = new CustomWebApplicationFactory(localStack.DynamoDb, localStack.TableName);
    }

    // ── GET /plans ───────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetPlans_Returns200WithAllSixPlans()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PlanListResponse>();
        body.Should().NotBeNull();
        body!.Plans.Should().HaveCount(6);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetPlans_ReturnsAllExpectedPlanIds()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");

        var body = await response.Content.ReadFromJsonAsync<PlanListResponse>();
        var ids = body!.Plans.Select(p => p.Id).ToList();
        ids.Should().Contain(["free", "starter", "pro", "team", "business", "enterprise"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetPlans_OrderedFreeToEnterprise()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");

        var body = await response.Content.ReadFromJsonAsync<PlanListResponse>();
        var ids = body!.Plans.Select(p => p.Id).ToList();
        ids.Should().ContainInOrder("free", "starter", "pro", "team", "business", "enterprise");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetPlans_NoAuthRequired()
    {
        // Call without any auth token — must succeed.
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Plan limits ──────────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("free", 0, 3, 100, 1_000, 30)]
    [InlineData("starter", 900, 10, 1_000, 10_000, 90)]
    [InlineData("pro", 2_900, 50, 10_000, 100_000, 180)]
    [InlineData("team", 7_900, 100, 50_000, 500_000, 365)]
    [InlineData("business", 24_900, 500, 200_000, 2_000_000, 730)]
    public async Task GetPlans_PaidPlan_HasCorrectPriceAndLimits(
        string planId, int expectedPriceCents,
        int expectedMaxDomains, int expectedMaxLinks,
        int expectedMaxClicks, int expectedRetentionDays)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");
        var body = await response.Content.ReadFromJsonAsync<PlanListResponse>();
        var plan = body!.Plans.Single(p => p.Id == planId);

        plan.MonthlyPriceCents.Should().Be(expectedPriceCents);
        plan.IsCustomPricing.Should().BeFalse();
        plan.Limits.MaxDomains.Should().Be(expectedMaxDomains);
        plan.Limits.MaxLinksPerDomain.Should().Be(expectedMaxLinks);
        plan.Limits.MaxTrackedClicksPerMonth.Should().Be(expectedMaxClicks);
        plan.Limits.AnalyticsRetentionDays.Should().Be(expectedRetentionDays);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetPlans_EnterprisePlan_IsCustomPricing()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");
        var body = await response.Content.ReadFromJsonAsync<PlanListResponse>();
        var enterprise = body!.Plans.Single(p => p.Id == "enterprise");

        enterprise.IsCustomPricing.Should().BeTrue();
        enterprise.MonthlyPriceCents.Should().BeNull();
    }

    // ── Features ─────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetPlans_FreePlan_HasNoFeatures()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");
        var body = await response.Content.ReadFromJsonAsync<PlanListResponse>();
        var free = body!.Plans.Single(p => p.Id == "free");

        free.Features.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetPlans_StarterPlan_HasApiAccessAndAnalyticsExport()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");
        var body = await response.Content.ReadFromJsonAsync<PlanListResponse>();
        var starter = body!.Plans.Single(p => p.Id == "starter");

        starter.Features.Should().Contain(PlanFeatures.ApiAccess);
        starter.Features.Should().Contain(PlanFeatures.AnalyticsExport);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetPlans_TeamPlan_HasTeamSeats()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");
        var body = await response.Content.ReadFromJsonAsync<PlanListResponse>();
        var team = body!.Plans.Single(p => p.Id == "team");

        team.Features.Should().Contain(PlanFeatures.TeamSeats);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetPlans_BusinessPlan_HasAllEnterpriseFeatures()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");
        var body = await response.Content.ReadFromJsonAsync<PlanListResponse>();
        var business = body!.Plans.Single(p => p.Id == "business");

        business.Features.Should().Contain(PlanFeatures.Sso);
        business.Features.Should().Contain(PlanFeatures.WhiteLabel);
        business.Features.Should().Contain(PlanFeatures.PrioritySupport);
    }

    // ── Trial availability ───────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("starter", true)]
    [InlineData("pro", true)]
    [InlineData("team", true)]
    [InlineData("free", false)]
    [InlineData("business", false)]
    [InlineData("enterprise", false)]
    public async Task GetPlans_TrialAvailability_MatchesCatalog(string planId, bool expectedTrial)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");
        var body = await response.Content.ReadFromJsonAsync<PlanListResponse>();
        var plan = body!.Plans.Single(p => p.Id == planId);

        plan.TrialAvailable.Should().Be(expectedTrial);
    }

    // ── Stripe Price IDs ─────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("starter")]
    [InlineData("pro")]
    [InlineData("team")]
    [InlineData("business")]
    public async Task GetPlans_PaidPlan_HasStripePriceId(string planId)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");
        var body = await response.Content.ReadFromJsonAsync<PlanListResponse>();
        var plan = body!.Plans.Single(p => p.Id == planId);

        plan.StripePriceId.Should().Be($"price_test_{planId}");
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("free")]
    [InlineData("enterprise")]
    public async Task GetPlans_FreeAndEnterprisePlan_HasNoStripePriceId(string planId)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");
        var body = await response.Content.ReadFromJsonAsync<PlanListResponse>();
        var plan = body!.Plans.Single(p => p.Id == planId);

        plan.StripePriceId.Should().BeNull();
    }

    // ── HasFeature extension ─────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateTenant_OnPro_HasFeatureReturnsTrue()
    {
        // Verify tenant.HasFeature() works correctly by creating a tenant and checking
        // the HasFeature extension against the repository entity.
        var client = _factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Feature Corp", Email = "feature@example.com", Plan = "pro" });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await createResponse.Content.ReadFromJsonAsync<CreateTenantResponse>();

        var repo = _factory.Services.GetRequiredService<ControlPlane.Api.Services.ITenantRepository>();
        var tenant = (await repo.GetByIdAsync(body!.Id))!;

        tenant.HasFeature(PlanFeatures.AdvancedAnalytics).Should().BeTrue();
        tenant.HasFeature(PlanFeatures.ApiAccess).Should().BeTrue();
        tenant.HasFeature(PlanFeatures.TeamSeats).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateTenant_OnProTrial_HasProFeatures()
    {
        // All new tenants start on a Pro trial, so they have Pro features immediately.
        var client = _factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/v1/tenants",
            new CreateTenantRequest { Name = "Free Feature Corp", Email = "freefeat@example.com" });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await createResponse.Content.ReadFromJsonAsync<CreateTenantResponse>();

        var repo = _factory.Services.GetRequiredService<ControlPlane.Api.Services.ITenantRepository>();
        var tenant = (await repo.GetByIdAsync(body!.Id))!;

        // On Pro trial — Pro features are active.
        tenant.HasFeature(PlanFeatures.ApiAccess).Should().BeTrue();
        tenant.HasFeature(PlanFeatures.AdvancedAnalytics).Should().BeTrue();
        // Team-only features are not yet included.
        tenant.HasFeature(PlanFeatures.TeamSeats).Should().BeFalse();
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
