using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Services;

/// <summary>
/// Unit tests for PlanCatalog: plan retrieval, limits, and catalog integrity.
/// </summary>
public sealed class PlanCatalogTests
{
    // ── Get ─────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void Get_KnownPlan_ReturnsDefinition()
    {
        var plan = PlanCatalog.Get("pro");

        plan.Id.Should().Be("pro");
        plan.MaxDomains.Should().Be(50);
        plan.MonthlyPriceCents.Should().Be(2_900);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Get_UnknownPlan_ThrowsInvalidOperationException()
    {
        var act = () => PlanCatalog.Get("nonexistent");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unknown plan*");
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("free")]
    [InlineData("starter")]
    [InlineData("pro")]
    [InlineData("team")]
    [InlineData("business")]
    [InlineData("enterprise")]
    public void Get_AllKnownPlans_ReturnNonNull(string planId)
    {
        var plan = PlanCatalog.Get(planId);
        plan.Should().NotBeNull();
        plan.Id.Should().Be(planId);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("Free")]
    [InlineData("FREE")]
    [InlineData("Pro")]
    [InlineData("PRO")]
    public void Get_CaseInsensitive_ReturnsMatch(string planId)
    {
        var plan = PlanCatalog.Get(planId);

        plan.Should().NotBeNull();
        plan.Id.Should().Be(planId.ToLowerInvariant());
    }

    // ── TryGet ──────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void TryGet_KnownPlan_ReturnsDefinition()
    {
        var plan = PlanCatalog.TryGet("team");

        plan.Should().NotBeNull();
        plan!.Id.Should().Be("team");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TryGet_UnknownPlan_ReturnsNull()
    {
        var plan = PlanCatalog.TryGet("nonexistent");

        plan.Should().BeNull();
    }

    // ── GetLimits ───────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void GetLimits_KnownPlan_ReturnsCorrectLimits()
    {
        var (maxDomains, maxLinksPerDomain) = PlanCatalog.GetLimits("pro");

        maxDomains.Should().Be(50);
        maxLinksPerDomain.Should().Be(10_000);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetLimits_UnknownPlan_FallsBackToFreeLimits()
    {
        var (maxDomains, maxLinksPerDomain) = PlanCatalog.GetLimits("nonexistent");

        maxDomains.Should().Be(PlanCatalog.Get("free").MaxDomains);
        maxLinksPerDomain.Should().Be(PlanCatalog.Get("free").MaxLinksPerDomain);
    }

    // ── All ─────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void All_ContainsAllSixPlansInOrder()
    {
        var plans = PlanCatalog.All;

        plans.Should().HaveCount(6);
        plans.Select(p => p.Id).Should().Equal("free", "starter", "pro", "team", "business", "enterprise");
    }

    // ── ValidIds ────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void ValidIds_ContainsAllSixPlanIds()
    {
        PlanCatalog.ValidIds.Should().HaveCount(6);
        PlanCatalog.ValidIds.Should().Contain(["free", "starter", "pro", "team", "business", "enterprise"]);
    }

    // ── Plan properties ─────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void FreePlan_HasZeroPrice()
    {
        var plan = PlanCatalog.Get("free");

        plan.MonthlyPriceCents.Should().Be(0);
        plan.IsCustomPricing.Should().BeFalse();
        plan.TrialAvailable.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EnterprisePlan_IsCustomPricing()
    {
        var plan = PlanCatalog.Get("enterprise");

        plan.IsCustomPricing.Should().BeTrue();
        plan.MonthlyPriceCents.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EnterprisePlan_HasUnlimitedClicks()
    {
        var plan = PlanCatalog.Get("enterprise");

        plan.MaxTrackedClicksPerMonth.Should().Be(int.MaxValue);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("starter", true)]
    [InlineData("pro", true)]
    [InlineData("team", true)]
    [InlineData("free", false)]
    [InlineData("business", false)]
    [InlineData("enterprise", false)]
    public void TrialAvailable_MatchesExpected(string planId, bool expected)
    {
        PlanCatalog.Get(planId).TrialAvailable.Should().Be(expected);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ProPlan_HasExpectedFeatures()
    {
        var plan = PlanCatalog.Get("pro");

        plan.Features.Should().Contain(PlanFeatures.ApiAccess);
        plan.Features.Should().Contain(PlanFeatures.AnalyticsExport);
        plan.Features.Should().Contain(PlanFeatures.AdvancedAnalytics);
        plan.Features.Should().Contain(PlanFeatures.BulkImport);
        plan.Features.Should().Contain(PlanFeatures.CustomQrCodes);
        plan.Features.Should().Contain(PlanFeatures.PasswordLinks);
        plan.Features.Should().NotContain(PlanFeatures.TeamSeats);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FreePlan_HasNoFeatures()
    {
        var plan = PlanCatalog.Get("free");

        plan.Features.Should().BeEmpty();
    }
}

