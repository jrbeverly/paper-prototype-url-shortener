using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Services;

public sealed class StripeBillingServiceTests
{
    // ── TierOf ─────────────────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("free", 0)]
    [InlineData("starter", 1)]
    [InlineData("pro", 2)]
    [InlineData("team", 3)]
    [InlineData("business", 4)]
    [InlineData("enterprise", 5)]
    [InlineData("FREE", 0)]
    [InlineData("PRO", 2)]
    public void TierOf_ValidPlan_ReturnsCorrectTier(string planId, int expectedTier)
    {
        var tier = StripeBillingService.TierOf(planId);
        tier.Should().Be(expectedTier);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TierOf_UnknownPlan_ThrowsBadRequest()
    {
        var act = () => StripeBillingService.TierOf("nonexistent");
        act.Should().Throw<Common.ErrorHandling.BadRequestException>()
            .WithMessage("*Unknown plan*nonexistent*");
    }

    // ── Plan tier comparison (validation logic) ────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("free", "starter")]    // free < starter
    [InlineData("starter", "pro")]     // starter < pro
    [InlineData("pro", "team")]        // pro < team
    [InlineData("team", "business")]   // team < business
    [InlineData("business", "enterprise")] // business < enterprise
    [InlineData("free", "enterprise")] // free < enterprise
    public void TierOf_LowerPlan_HasLowerTier(string lowerPlan, string higherPlan)
    {
        var lowerTier = StripeBillingService.TierOf(lowerPlan);
        var higherTier = StripeBillingService.TierOf(higherPlan);
        lowerTier.Should().BeLessThan(higherTier);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("enterprise", "business")] // enterprise > business
    [InlineData("business", "team")]      // business > team
    [InlineData("team", "pro")]           // team > pro
    [InlineData("pro", "starter")]        // pro > starter
    [InlineData("starter", "free")]       // starter > free
    [InlineData("enterprise", "free")]    // enterprise > free
    public void TierOf_HigherPlan_HasHigherTier(string higherPlan, string lowerPlan)
    {
        var higherTier = StripeBillingService.TierOf(higherPlan);
        var lowerTier = StripeBillingService.TierOf(lowerPlan);
        higherTier.Should().BeGreaterThan(lowerTier);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TierOf_SamePlan_HasEqualTier()
    {
        var tier1 = StripeBillingService.TierOf("pro");
        var tier2 = StripeBillingService.TierOf("PRO");
        tier1.Should().Be(tier2);
    }
}
