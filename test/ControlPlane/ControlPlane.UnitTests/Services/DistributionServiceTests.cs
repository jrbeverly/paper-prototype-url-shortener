using ControlPlane.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ControlPlane.UnitTests.Services;

public sealed class DistributionServiceTests
{
    private static InMemoryDistributionService CreateService(
        int quotaLimit = 100, int warningThreshold = 80) =>
        new(Options.Create(new DistributionOptions
        {
            TenantQuotaLimit = quotaLimit,
            QuotaWarningThreshold = warningThreshold
        }), NullLogger<InMemoryDistributionService>.Instance);

    // ── CheckQuota ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckQuota_Empty_ReturnsWithinQuota()
    {
        var service = CreateService();

        var result = await service.CheckQuotaAsync();

        result.IsWithinQuota.Should().BeTrue();
        result.CurrentCount.Should().Be(0);
        result.QuotaLimit.Should().Be(100);
        result.IsNearingLimit.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckQuota_AtWarningThreshold_IsNearingLimit()
    {
        var service = CreateService(quotaLimit: 10, warningThreshold: 2);
        for (var i = 0; i < 2; i++)
            await service.CreateDistributionTenantAsync(Guid.NewGuid(), Guid.NewGuid(), $"d{i}.example.com", null);

        var result = await service.CheckQuotaAsync();

        result.IsNearingLimit.Should().BeTrue();
        result.IsWithinQuota.Should().BeTrue();
        result.CurrentCount.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CheckQuota_AtLimit_NotWithinQuota()
    {
        var service = CreateService(quotaLimit: 2);
        await service.CreateDistributionTenantAsync(Guid.NewGuid(), Guid.NewGuid(), "a.example.com", null);
        await service.CreateDistributionTenantAsync(Guid.NewGuid(), Guid.NewGuid(), "b.example.com", null);

        var result = await service.CheckQuotaAsync();

        result.IsWithinQuota.Should().BeFalse();
        result.CurrentCount.Should().Be(2);
    }

    // ── Create ─────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateDistributionTenant_ValidDomain_ReturnsResult()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();
        var domainId = Guid.NewGuid();

        var result = await service.CreateDistributionTenantAsync(tenantId, domainId, "go.example.com", null);

        result.DistributionTenantId.Should().NotBeNullOrEmpty();
        result.Status.Should().Be("InProgress");
        result.ETag.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateDistributionTenant_WithCertificateArn_Succeeds()
    {
        var service = CreateService();
        var arn = "arn:aws:acm:us-east-1:000000000000:certificate/test-cert";

        var result = await service.CreateDistributionTenantAsync(
            Guid.NewGuid(), Guid.NewGuid(), "links.example.com", arn);

        result.DistributionTenantId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateDistributionTenant_AtQuotaLimit_ThrowsInvalidOperation()
    {
        var service = CreateService(quotaLimit: 1);
        await service.CreateDistributionTenantAsync(Guid.NewGuid(), Guid.NewGuid(), "a.example.com", null);

        var act = () => service.CreateDistributionTenantAsync(Guid.NewGuid(), Guid.NewGuid(), "b.example.com", null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*quota*");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateDistributionTenant_MultipleCreates_UniqueIds()
    {
        var service = CreateService();

        var r1 = await service.CreateDistributionTenantAsync(Guid.NewGuid(), Guid.NewGuid(), "a.example.com", null);
        var r2 = await service.CreateDistributionTenantAsync(Guid.NewGuid(), Guid.NewGuid(), "b.example.com", null);

        r1.DistributionTenantId.Should().NotBe(r2.DistributionTenantId);
        r1.ETag.Should().NotBe(r2.ETag);
    }

    // ── GetStatus ──────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_UnknownId_ReturnsNotFound()
    {
        var service = CreateService();

        var result = await service.GetDistributionTenantStatusAsync("dt_unknown");

        result.Status.Should().Be("NotFound");
        result.ETag.Should().BeEmpty();
        result.IsDeployed.Should().BeFalse();
        result.IsFailed.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_NewlyCreated_IsInProgress()
    {
        var service = CreateService();
        var created = await service.CreateDistributionTenantAsync(
            Guid.NewGuid(), Guid.NewGuid(), "go.example.com", null);

        var status = await service.GetDistributionTenantStatusAsync(created.DistributionTenantId);

        status.Status.Should().Be("InProgress");
        status.IsDeployed.Should().BeFalse();
    }

    // ── Update ─────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateDistributionTenant_ExistingTenant_ReturnsNewETag()
    {
        var service = CreateService();
        var created = await service.CreateDistributionTenantAsync(
            Guid.NewGuid(), Guid.NewGuid(), "go.example.com", null);

        var updated = await service.UpdateDistributionTenantAsync(
            created.DistributionTenantId, created.ETag, "links.example.com", null);

        updated.DistributionTenantId.Should().Be(created.DistributionTenantId);
        updated.ETag.Should().NotBe(created.ETag);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateDistributionTenant_UnknownId_ThrowsInvalidOperation()
    {
        var service = CreateService();

        var act = () => service.UpdateDistributionTenantAsync("dt_nonexistent", "E1", "x.example.com", null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    // ── Delete ─────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteDistributionTenant_ExistingTenant_ReturnsTrue()
    {
        var service = CreateService();
        var created = await service.CreateDistributionTenantAsync(
            Guid.NewGuid(), Guid.NewGuid(), "go.example.com", null);

        var deleted = await service.DeleteDistributionTenantAsync(created.DistributionTenantId);

        deleted.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteDistributionTenant_ExistingTenant_ThenGetStatus_ReturnsNotFound()
    {
        var service = CreateService();
        var created = await service.CreateDistributionTenantAsync(
            Guid.NewGuid(), Guid.NewGuid(), "go.example.com", null);

        await service.DeleteDistributionTenantAsync(created.DistributionTenantId);

        var status = await service.GetDistributionTenantStatusAsync(created.DistributionTenantId);
        status.Status.Should().Be("NotFound");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteDistributionTenant_UnknownId_ReturnsFalse()
    {
        var service = CreateService();

        var result = await service.DeleteDistributionTenantAsync("dt_nonexistent");

        result.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteDistributionTenant_ReducesQuotaCount()
    {
        var service = CreateService(quotaLimit: 2);
        var created = await service.CreateDistributionTenantAsync(
            Guid.NewGuid(), Guid.NewGuid(), "go.example.com", null);

        await service.DeleteDistributionTenantAsync(created.DistributionTenantId);

        var quota = await service.CheckQuotaAsync();
        quota.CurrentCount.Should().Be(0);
        quota.IsWithinQuota.Should().BeTrue();
    }
}
