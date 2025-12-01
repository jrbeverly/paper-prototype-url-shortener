using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Services;

/// <summary>
/// Unit tests for UsageService static calculation methods: alert levels, overage, and billing period boundaries.
/// </summary>
public sealed class UsageServiceTests
{
    // ── CalculateAlertLevel ───────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(0, 1000, "None")]
    [InlineData(799, 1000, "None")]
    [InlineData(800, 1000, "Warning80")]
    [InlineData(899, 1000, "Warning80")]
    [InlineData(900, 1000, "Warning90")]
    [InlineData(999, 1000, "Warning90")]
    [InlineData(1000, 1000, "LimitReached")]
    [InlineData(1500, 1000, "LimitReached")]
    public void CalculateAlertLevel_ReturnsExpectedLevel(long clicks, int limit, string expectedLevel)
    {
        var expected = Enum.Parse<UsageAlertLevel>(expectedLevel);
        var result = UsageService.CalculateAlertLevel(clicks, limit);
        result.Should().Be(expected);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CalculateAlertLevel_Enterprise_AlwaysNone()
    {
        // Enterprise plan has int.MaxValue clicks — should never trigger alerts
        var result = UsageService.CalculateAlertLevel(long.MaxValue / 2, int.MaxValue);
        result.Should().Be(UsageAlertLevel.None);
    }

    // ── CalculateOverage ──────────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(0, 1000, 0)]
    [InlineData(999, 1000, 0)]
    [InlineData(1000, 1000, 0)]
    [InlineData(1001, 1000, 1)]
    [InlineData(1500, 1000, 500)]
    public void CalculateOverage_ReturnsExpectedOverage(long clicks, int limit, long expectedOverage)
    {
        var result = UsageService.CalculateOverage(clicks, limit);
        result.Should().Be(expectedOverage);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CalculateOverage_Enterprise_AlwaysZero()
    {
        var result = UsageService.CalculateOverage(long.MaxValue / 2, int.MaxValue);
        result.Should().Be(0);
    }

    // ── GetCurrentBillingPeriod ───────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void GetCurrentBillingPeriod_StartIsFirstOfMonth()
    {
        var input = new DateTime(2026, 6, 15, 14, 30, 0, DateTimeKind.Utc);
        var (start, _) = UsageService.GetCurrentBillingPeriod(input);
        start.Should().Be(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetCurrentBillingPeriod_EndIsLastMomentOfMonth()
    {
        var input = new DateTime(2026, 6, 15, 14, 30, 0, DateTimeKind.Utc);
        var (_, end) = UsageService.GetCurrentBillingPeriod(input);
        // End should be one tick before July 1st
        end.Should().Be(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(-1));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetCurrentBillingPeriod_DifferentMonths_HaveDifferentPeriods()
    {
        var june = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var july = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        var (juneStart, _) = UsageService.GetCurrentBillingPeriod(june);
        var (julyStart, _) = UsageService.GetCurrentBillingPeriod(july);

        juneStart.Should().NotBe(julyStart);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetCurrentBillingPeriod_EndOfMonth_StillInSamePeriod()
    {
        var lastDayOfJune = new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc);
        var firstDayOfJuly = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        var (juneStart, _) = UsageService.GetCurrentBillingPeriod(lastDayOfJune);
        var (julyStart, _) = UsageService.GetCurrentBillingPeriod(firstDayOfJuly);

        juneStart.Month.Should().Be(6);
        julyStart.Month.Should().Be(7);
    }
}
