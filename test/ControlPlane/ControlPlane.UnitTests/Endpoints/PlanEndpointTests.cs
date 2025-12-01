using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Endpoints;

public sealed class PlanEndpointTests : IAsyncDisposable
{
    private readonly UnitTestWebApplicationFactory _factory = new();

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListPlans_NoAuth_Returns200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListPlans_ReturnsAllSixPlans()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");
        var body = await response.Content.ReadFromJsonAsync<PlanListResponse>();

        body.Should().NotBeNull();
        body!.Plans.Should().HaveCount(6);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListPlans_IncludesExpectedPlanIds()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");
        var body = await response.Content.ReadFromJsonAsync<PlanListResponse>();

        var ids = body!.Plans.Select(p => p.Id).ToList();
        ids.Should().Contain(["free", "starter", "pro", "team", "business", "enterprise"]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListPlans_EnterprisePlanHasCustomPricing()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");
        var body = await response.Content.ReadFromJsonAsync<PlanListResponse>();

        var enterprise = body!.Plans.Single(p => p.Id == "enterprise");
        enterprise.IsCustomPricing.Should().BeTrue();
        enterprise.MonthlyPriceCents.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListPlans_FreePlanHasNoTrialAvailable()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");
        var body = await response.Content.ReadFromJsonAsync<PlanListResponse>();

        var free = body!.Plans.Single(p => p.Id == "free");
        free.TrialAvailable.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListPlans_ProPlanHasTrialAvailable()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");
        var body = await response.Content.ReadFromJsonAsync<PlanListResponse>();

        var pro = body!.Plans.Single(p => p.Id == "pro");
        pro.TrialAvailable.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListPlans_IncludesStripeTestPriceIds()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");
        var body = await response.Content.ReadFromJsonAsync<PlanListResponse>();

        // TestStripePlanService returns deterministic test price IDs for paid plans
        var pro = body!.Plans.Single(p => p.Id == "pro");
        pro.StripePriceId.Should().Be("price_test_pro");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListPlans_EachPlanHasLimitsPopulated()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");
        var body = await response.Content.ReadFromJsonAsync<PlanListResponse>();

        foreach (var plan in body!.Plans)
        {
            plan.Limits.Should().NotBeNull();
            plan.Limits.MaxDomains.Should().BeGreaterThan(0);
            plan.Limits.MaxLinksPerDomain.Should().BeGreaterThan(0);
        }
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
