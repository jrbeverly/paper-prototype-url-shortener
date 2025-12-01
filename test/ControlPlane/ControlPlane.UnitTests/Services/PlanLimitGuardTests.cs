using System.Security.Claims;
using ControlPlane.Api.Authorization;
using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Services;

/// <summary>
/// Unit tests for PlanLimitGuard: grace limit calculation and plan bypass checks.
/// </summary>
public sealed class PlanLimitGuardTests
{
    // ── GraceLimit ──────────────────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(10, 11)]       // 10 + 10% = 11, ceiling
    [InlineData(100, 111)]     // 100 * 1.1 → IEEE 754 → 110.00000000000001 → ceiling → 111
    [InlineData(1000, 1100)]   // 1000 + 10% = 1100
    [InlineData(3, 4)]         // 3 + 10% = 3.3 → ceiling → 4
    [InlineData(1, 2)]         // 1 + 10% = 1.1 → ceiling → 2
    [InlineData(0, 0)]         // edge: zero
    public void GraceLimit_AddsTenPercentCeiling(int limit, int expected)
    {
        PlanLimitGuard.GraceLimit(limit).Should().Be(expected);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GraceLimit_VeryLargeValue_ReturnsIntMax()
    {
        // int.MaxValue / 2 or larger → returns int.MaxValue
        PlanLimitGuard.GraceLimit(int.MaxValue / 2).Should().Be(int.MaxValue);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GraceLimit_IntMaxValue_ReturnsIntMaxValue()
    {
        PlanLimitGuard.GraceLimit(int.MaxValue).Should().Be(int.MaxValue);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GraceLimit_JustBelowThreshold_StillAppliesGrace()
    {
        // Below the int.MaxValue/2 threshold
        var result = PlanLimitGuard.GraceLimit(int.MaxValue / 2 - 1);
        result.Should().BeLessThan(int.MaxValue);
    }

    // ── HasPlanBypass ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void HasPlanBypass_UserWithBypassPermission_ReturnsTrue()
    {
        var user = CreatePrincipal(additionalPermissions: [Permissions.PlanBypass]);

        PlanLimitGuard.HasPlanBypass(user).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void HasPlanBypass_UserWithoutBypassPermission_ReturnsFalse()
    {
        var user = CreatePrincipal(additionalPermissions: []);

        PlanLimitGuard.HasPlanBypass(user).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void HasPlanBypass_NoPermissionsClaim_ReturnsFalse()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity());

        PlanLimitGuard.HasPlanBypass(user).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void HasPlanBypass_EmptyPermissionsClaim_ReturnsFalse()
    {
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim("permissions", ""));
        var user = new ClaimsPrincipal(identity);

        PlanLimitGuard.HasPlanBypass(user).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void HasPlanBypass_BypassAmongOtherPermissions_ReturnsTrue()
    {
        var user = CreatePrincipal(additionalPermissions: ["tenant:read", Permissions.PlanBypass, "tenant:write"]);

        PlanLimitGuard.HasPlanBypass(user).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void HasPlanBypass_CaseInsensitiveMatch_ReturnsTrue()
    {
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim("permissions", "PLAN:BYPASS"));
        var user = new ClaimsPrincipal(identity);

        PlanLimitGuard.HasPlanBypass(user).Should().BeTrue();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static ClaimsPrincipal CreatePrincipal(string[] additionalPermissions)
    {
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim("permissions", string.Join(',', additionalPermissions)));
        return new ClaimsPrincipal(identity);
    }
}
