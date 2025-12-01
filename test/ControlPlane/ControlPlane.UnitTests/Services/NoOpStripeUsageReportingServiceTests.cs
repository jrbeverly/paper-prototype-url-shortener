using ControlPlane.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlPlane.UnitTests.Services;

/// <summary>
/// Unit tests for NoOpStripeUsageReportingService.
/// </summary>
public sealed class NoOpStripeUsageReportingServiceTests
{
    private readonly NoOpStripeUsageReportingService _sut = new(NullLogger<NoOpStripeUsageReportingService>.Instance);

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ReportClicks_CompletesSuccessfully()
    {
        var tenant = new TenantEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Email = "test@example.com",
            Plan = "pro",
            Status = "active",
            StripeSubscriptionId = "sub_test_123",
            MaxDomains = 50,
            MaxLinksPerDomain = 10_000,
            CreatedAt = DateTime.UtcNow
        };

        var act = () => _sut.ReportClicksAsync(tenant, 1000, DateTime.UtcNow);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ReportClicks_NoSubscription_CompletesWithoutLoggingSubscriptionId()
    {
        var tenant = new TenantEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Email = "test@example.com",
            Plan = "free",
            Status = "active",
            StripeSubscriptionId = null,
            MaxDomains = 3,
            MaxLinksPerDomain = 100,
            CreatedAt = DateTime.UtcNow
        };

        var act = () => _sut.ReportClicksAsync(tenant, 10, DateTime.UtcNow);

        await act.Should().NotThrowAsync();
    }
}
