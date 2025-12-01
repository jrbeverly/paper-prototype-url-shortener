using ControlPlane.Api.Services;
using Microsoft.Extensions.Options;

namespace ControlPlane.UnitTests.Services;

public sealed class LinkRateLimiterTests
{
    private static InMemoryLinkRateLimiter CreateLimiter(int ipLimit = 60, params string[] exemptIps)
    {
        var options = Options.Create(new RateLimitOptions
        {
            IpLimitPerMinute = ipLimit,
            ExemptIps = exemptIps
        });
        return new InMemoryLinkRateLimiter(options);
    }

    // ── CheckTenant ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void CheckTenant_FirstRequest_Allowed()
    {
        using var limiter = CreateLimiter();
        var result = limiter.CheckTenant(Guid.NewGuid(), 10);
        result.IsAllowed.Should().BeTrue();
        result.Remaining.Should().Be(9);
        result.Limit.Should().Be(10);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CheckTenant_UnderLimit_AllAllowed()
    {
        using var limiter = CreateLimiter();
        var tenantId = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
            limiter.CheckTenant(tenantId, 5).IsAllowed.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CheckTenant_AtLimit_NextRequestBlocked()
    {
        using var limiter = CreateLimiter();
        var tenantId = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
            limiter.CheckTenant(tenantId, 5);

        var blocked = limiter.CheckTenant(tenantId, 5);
        blocked.IsAllowed.Should().BeFalse();
        blocked.Remaining.Should().Be(0);
        blocked.RetryAfterSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CheckTenant_DifferentTenants_IndependentLimits()
    {
        using var limiter = CreateLimiter();
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        // Fill tenant 1
        for (var i = 0; i < 3; i++)
            limiter.CheckTenant(t1, 3);

        // Tenant 2 should still have its full limit
        limiter.CheckTenant(t2, 3).IsAllowed.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CheckTenant_RemainingDecrements()
    {
        using var limiter = CreateLimiter();
        var tenantId = Guid.NewGuid();
        var first = limiter.CheckTenant(tenantId, 5);
        var second = limiter.CheckTenant(tenantId, 5);
        first.Remaining.Should().Be(4);
        second.Remaining.Should().Be(3);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CheckTenant_ResetTimeIsNextMinuteBoundary()
    {
        using var limiter = CreateLimiter();
        var result = limiter.CheckTenant(Guid.NewGuid(), 10);
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expectedReset = (nowSeconds / 60 + 1) * 60;
        result.ResetUnixSeconds.Should().Be(expectedReset);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CheckTenant_UnlimitedPlan_AlwaysAllowed()
    {
        using var limiter = CreateLimiter();
        var tenantId = Guid.NewGuid();
        for (var i = 0; i < 200; i++)
            limiter.CheckTenant(tenantId, int.MaxValue).IsAllowed.Should().BeTrue();
    }

    // ── CheckIp ────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void CheckIp_UnderLimit_Allowed()
    {
        using var limiter = CreateLimiter(ipLimit: 3);
        for (var i = 0; i < 3; i++)
            limiter.CheckIp("1.2.3.4").IsAllowed.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CheckIp_AtLimit_Blocked()
    {
        using var limiter = CreateLimiter(ipLimit: 2);
        limiter.CheckIp("1.2.3.4");
        limiter.CheckIp("1.2.3.4");
        limiter.CheckIp("1.2.3.4").IsAllowed.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CheckIp_DifferentIps_IndependentCounters()
    {
        using var limiter = CreateLimiter(ipLimit: 1);
        limiter.CheckIp("1.1.1.1");  // exhausts limit for 1.1.1.1
        limiter.CheckIp("2.2.2.2").IsAllowed.Should().BeTrue();
    }

    // ── IsExemptIp ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void IsExemptIp_ConfiguredIp_ReturnsTrue()
    {
        using var limiter = CreateLimiter(exemptIps: ["10.0.0.1", "127.0.0.1"]);
        limiter.IsExemptIp("10.0.0.1").Should().BeTrue();
        limiter.IsExemptIp("127.0.0.1").Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsExemptIp_UnknownIp_ReturnsFalse()
    {
        using var limiter = CreateLimiter(exemptIps: ["10.0.0.1"]);
        limiter.IsExemptIp("8.8.8.8").Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsExemptIp_CaseInsensitive()
    {
        using var limiter = CreateLimiter(exemptIps: ["::1"]);
        limiter.IsExemptIp("::1").Should().BeTrue();
        limiter.IsExemptIp("::1").Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsExemptIp_EmptyList_AlwaysFalse()
    {
        using var limiter = CreateLimiter();
        limiter.IsExemptIp("127.0.0.1").Should().BeFalse();
    }
}
