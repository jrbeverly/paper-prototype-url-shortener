using ControlPlane.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ControlPlane.UnitTests.Services;

public sealed class InMemoryCertificateServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static InMemoryCertificateService CreateService(
        int maxPendingCertificates = 10,
        int maxRequestsPerMinute = 6,
        string region = "us-east-1") =>
        new(Options.Create(new CertificateOptions
        {
            MaxPendingCertificates = maxPendingCertificates,
            MaxRequestsPerMinute = maxRequestsPerMinute,
            Region = region
        }), NullLogger<InMemoryCertificateService>.Instance);

    // ── RequestCertificateAsync ───────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Request_ValidInputs_ReturnsPendingValidationStatus()
    {
        var service = CreateService();

        var result = await service.RequestCertificateAsync(
            Guid.NewGuid(), Guid.NewGuid(), "links.example.com");

        result.Status.Should().Be("pending_validation");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Request_ValidInputs_ReturnsNonEmptyArn()
    {
        var service = CreateService();

        var result = await service.RequestCertificateAsync(
            Guid.NewGuid(), Guid.NewGuid(), "links.example.com");

        result.CertificateArn.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Request_Arn_ContainsConfiguredRegion()
    {
        var service = CreateService(region: "eu-west-1");

        var result = await service.RequestCertificateAsync(
            Guid.NewGuid(), Guid.NewGuid(), "links.example.com");

        result.CertificateArn.Should().Contain("eu-west-1");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Request_ValidInputs_ReturnsValidationRecords()
    {
        var service = CreateService();

        var result = await service.RequestCertificateAsync(
            Guid.NewGuid(), Guid.NewGuid(), "links.example.com");

        result.ValidationRecords.Should().NotBeEmpty();
        result.ValidationRecords[0].Name.Should().NotBeNullOrEmpty();
        result.ValidationRecords[0].Value.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Request_MultipleRequests_EachHasUniqueArn()
    {
        var service = CreateService();
        var tenantId = Guid.NewGuid();

        var r1 = await service.RequestCertificateAsync(tenantId, Guid.NewGuid(), "a.example.com");
        var r2 = await service.RequestCertificateAsync(tenantId, Guid.NewGuid(), "b.example.com");

        r1.CertificateArn.Should().NotBe(r2.CertificateArn);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Request_MaxPendingCertificatesReached_ThrowsInvalidOperation()
    {
        var tenantId = Guid.NewGuid();
        var service = CreateService(maxPendingCertificates: 1);
        await service.RequestCertificateAsync(tenantId, Guid.NewGuid(), "a.example.com");

        var act = () => service.RequestCertificateAsync(tenantId, Guid.NewGuid(), "b.example.com");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*pending*");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Request_MaxRequestsPerMinuteReached_ThrowsInvalidOperation()
    {
        var tenantId = Guid.NewGuid();
        var service = CreateService(maxRequestsPerMinute: 1);
        await service.RequestCertificateAsync(tenantId, Guid.NewGuid(), "a.example.com");

        var act = () => service.RequestCertificateAsync(tenantId, Guid.NewGuid(), "b.example.com");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*rate limit*");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Request_PendingLimit_IsScopedToTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var service = CreateService(maxPendingCertificates: 1);

        // Tenant A exhausts its limit.
        await service.RequestCertificateAsync(tenantA, Guid.NewGuid(), "a.example.com");

        // Tenant B is unaffected.
        var act = () => service.RequestCertificateAsync(tenantB, Guid.NewGuid(), "b.example.com");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Request_RateLimit_IsScopedToTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var service = CreateService(maxRequestsPerMinute: 1);

        // Tenant A exhausts its rate quota.
        await service.RequestCertificateAsync(tenantA, Guid.NewGuid(), "a.example.com");

        // Tenant B is unaffected.
        var act = () => service.RequestCertificateAsync(tenantB, Guid.NewGuid(), "b.example.com");
        await act.Should().NotThrowAsync();
    }

    // ── GetCertificateStatusAsync ─────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_NewlyRequestedCert_ReturnsPendingValidation()
    {
        var service = CreateService();
        var requested = await service.RequestCertificateAsync(
            Guid.NewGuid(), Guid.NewGuid(), "links.example.com");

        // Freshly requested — well under the 2-second auto-issue threshold.
        var status = await service.GetCertificateStatusAsync(requested.CertificateArn);

        status.Status.Should().Be("pending_validation");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_NewlyRequestedCert_ArnMatchesRequest()
    {
        var service = CreateService();
        var requested = await service.RequestCertificateAsync(
            Guid.NewGuid(), Guid.NewGuid(), "links.example.com");

        var status = await service.GetCertificateStatusAsync(requested.CertificateArn);

        status.CertificateArn.Should().Be(requested.CertificateArn);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_NewlyRequestedCert_SubjectAlternativeNamesContainsHostname()
    {
        var service = CreateService();
        var requested = await service.RequestCertificateAsync(
            Guid.NewGuid(), Guid.NewGuid(), "links.example.com");

        var status = await service.GetCertificateStatusAsync(requested.CertificateArn);

        status.SubjectAlternativeNames.Should().Contain("links.example.com");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_NonExistentArn_ReturnsFailedWithReason()
    {
        var service = CreateService();

        var status = await service.GetCertificateStatusAsync(
            "arn:aws:acm:us-east-1:123:certificate/does-not-exist");

        status.Status.Should().Be("failed");
        status.FailureReason.Should().NotBeNullOrWhiteSpace();
    }

    // ── DeleteCertificateAsync ────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Delete_ExistingCert_ReturnsTrue()
    {
        var service = CreateService();
        var requested = await service.RequestCertificateAsync(
            Guid.NewGuid(), Guid.NewGuid(), "links.example.com");

        var deleted = await service.DeleteCertificateAsync(requested.CertificateArn);

        deleted.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Delete_ExistingCert_SubsequentGetStatusReturnsFailed()
    {
        var service = CreateService();
        var requested = await service.RequestCertificateAsync(
            Guid.NewGuid(), Guid.NewGuid(), "links.example.com");
        await service.DeleteCertificateAsync(requested.CertificateArn);

        var status = await service.GetCertificateStatusAsync(requested.CertificateArn);

        status.Status.Should().Be("failed");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Delete_ExistingCert_FreesSlotForNewPendingCert()
    {
        // With limit = 1, deleting the cert allows a new request to succeed.
        var tenantId = Guid.NewGuid();
        var service = CreateService(maxPendingCertificates: 1);
        var requested = await service.RequestCertificateAsync(tenantId, Guid.NewGuid(), "a.example.com");

        await service.DeleteCertificateAsync(requested.CertificateArn);

        // Slot freed — should not throw.
        var act = () => service.RequestCertificateAsync(tenantId, Guid.NewGuid(), "b.example.com");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Delete_NonExistentArn_ReturnsFalse()
    {
        var service = CreateService();

        var result = await service.DeleteCertificateAsync(
            "arn:aws:acm:us-east-1:123:certificate/does-not-exist");

        result.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Delete_SameArnTwice_SecondCallReturnsFalse()
    {
        var service = CreateService();
        var requested = await service.RequestCertificateAsync(
            Guid.NewGuid(), Guid.NewGuid(), "links.example.com");

        await service.DeleteCertificateAsync(requested.CertificateArn);
        var second = await service.DeleteCertificateAsync(requested.CertificateArn);

        second.Should().BeFalse();
    }
}
