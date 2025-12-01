using ControlPlane.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ControlPlane.UnitTests.Services;

public sealed class DomainActivationServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DomainActivationService CreateService(
        InMemoryDomainRepository? domainRepo = null,
        ICertificateService? certService = null,
        IDistributionService? distService = null,
        InMemoryAuditLogService? auditLog = null,
        IDomainStatusNotificationService? notification = null)
    {
        return new DomainActivationService(
            domainRepo ?? new InMemoryDomainRepository(),
            certService ?? new InMemoryCertificateService(
                Options.Create(new CertificateOptions()), NullLogger<InMemoryCertificateService>.Instance),
            distService ?? new InMemoryDistributionService(
                Options.Create(new DistributionOptions()), NullLogger<InMemoryDistributionService>.Instance),
            auditLog ?? new InMemoryAuditLogService(),
            notification ?? new SpyNotificationService(),
            NullLogger<DomainActivationService>.Instance);
    }

    private static DomainEntity CreateDomain(string status = "verifying")
    {
        return new DomainEntity
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Hostname = "links.example.com",
            Status = status,
            VerificationCode = "abc123",
            CnameTarget = "domains.short.io",
            CreatedAt = DateTime.UtcNow
        };
    }

    // ── State machine: successful path ────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Activate_Success_TransitionsThroughCertificateProvisioningToActive()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);

        var service = CreateService(domainRepo: repo);
        var result = await service.ActivateAsync(domain, actorId: "test");

        result.Succeeded.Should().BeTrue();
        result.Domain.Status.Should().Be("active");

        var stored = await repo.GetByIdAsync(domain.TenantId, domain.Id);
        stored!.Status.Should().Be("active");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Activate_Success_SetsCertificateProvisioningBeforeCertRequest()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);

        // Use a cert service that captures the domain status at the time of the cert request.
        string? statusAtCertRequest = null;
        var certSpy = new SpyCertificateService(onRequest: async (tenantId, domainId, hostname) =>
        {
            var current = await repo.GetByIdAsync(tenantId, domainId);
            statusAtCertRequest = current?.Status;
            return new CertificateRequestResult
            {
                CertificateArn = "arn:aws:acm:us-east-1:123:certificate/test",
                Status = "pending",
                ValidationRecords = []
            };
        });

        var service = CreateService(domainRepo: repo, certService: certSpy);
        await service.ActivateAsync(domain, actorId: "test");

        statusAtCertRequest.Should().Be("certificate_provisioning");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Activate_Success_ProvisionsCertificate()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);

        var service = CreateService(domainRepo: repo);
        var result = await service.ActivateAsync(domain, actorId: "test");

        result.Domain.CertificateArn.Should().NotBeNullOrEmpty();
        result.Domain.CertificateStatus.Should().NotBeNullOrEmpty();

        var stored = await repo.GetByIdAsync(domain.TenantId, domain.Id);
        stored!.CertificateArn.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Activate_Success_CreatesDistributionTenant()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);

        var service = CreateService(domainRepo: repo);
        var result = await service.ActivateAsync(domain, actorId: "test");

        result.Domain.DistributionTenantId.Should().NotBeNullOrEmpty();
        result.Domain.DistributionTenantStatus.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Activate_Success_SetsLastVerifiedAt()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);

        var before = DateTime.UtcNow;
        var service = CreateService(domainRepo: repo);
        var result = await service.ActivateAsync(domain, actorId: "test");

        result.Domain.LastVerifiedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Activate_Success_UpdatedAtIsSet()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);

        var before = DateTime.UtcNow;
        var service = CreateService(domainRepo: repo);
        var result = await service.ActivateAsync(domain, actorId: "test");

        result.Domain.UpdatedAt.Should().BeOnOrAfter(before);
    }

    // ── State machine: certificate failure ────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Activate_CertificateFails_ReturnsCertificateFailedDomain()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);

        var service = CreateService(domainRepo: repo, certService: new ThrowingCertificateService());
        var result = await service.ActivateAsync(domain, actorId: "test");

        result.Succeeded.Should().BeFalse();
        result.Domain.Status.Should().Be("certificate_failed");
        result.FailureReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Activate_CertificateFails_PersistsCertificateFailedStatus()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);

        var service = CreateService(domainRepo: repo, certService: new ThrowingCertificateService());
        await service.ActivateAsync(domain, actorId: "test");

        var stored = await repo.GetByIdAsync(domain.TenantId, domain.Id);
        stored!.Status.Should().Be("certificate_failed");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Activate_CertificateFails_DoesNotCreateDistributionTenant()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);

        var service = CreateService(domainRepo: repo, certService: new ThrowingCertificateService());
        var result = await service.ActivateAsync(domain, actorId: "test");

        result.Domain.DistributionTenantId.Should().BeNull();

        var stored = await repo.GetByIdAsync(domain.TenantId, domain.Id);
        stored!.DistributionTenantId.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Activate_CertificateFails_DoesNotSendNotification()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);
        var spy = new SpyNotificationService();

        var service = CreateService(
            domainRepo: repo,
            certService: new ThrowingCertificateService(),
            notification: spy);
        await service.ActivateAsync(domain, actorId: "test");

        spy.VerifiedDomainIds.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Activate_CertificateFails_LogsCertificateFailedAuditEntry()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);
        var auditLog = new InMemoryAuditLogService();

        var service = CreateService(
            domainRepo: repo,
            certService: new ThrowingCertificateService(),
            auditLog: auditLog);
        await service.ActivateAsync(domain, actorId: "test");

        var entries = await auditLog.QueryAsync(domain.TenantId, action: "domain.certificate_failed");
        entries.TotalCount.Should().Be(1);
        entries.Items[0].ActorId.Should().Be("test");
        entries.Items[0].ActorType.Should().Be("system");
    }

    // ── State machine: distribution failure (non-fatal) ───────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Activate_DistributionFails_StillActivatesDomain()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);

        var service = CreateService(domainRepo: repo, distService: new ThrowingDistributionService());
        var result = await service.ActivateAsync(domain, actorId: "test");

        result.Succeeded.Should().BeTrue();
        result.Domain.Status.Should().Be("active");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Activate_DistributionFails_NoCertificateArnLost()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);

        var service = CreateService(domainRepo: repo, distService: new ThrowingDistributionService());
        var result = await service.ActivateAsync(domain, actorId: "test");

        result.Domain.CertificateArn.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Activate_DistributionFails_SendsNotificationAnyway()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);
        var spy = new SpyNotificationService();

        var service = CreateService(
            domainRepo: repo,
            distService: new ThrowingDistributionService(),
            notification: spy);
        await service.ActivateAsync(domain, actorId: "test");

        spy.VerifiedDomainIds.Should().Contain(domain.Id);
    }

    // ── Notifications ─────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Activate_Success_SendsVerifiedNotification()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);
        var spy = new SpyNotificationService();

        var service = CreateService(domainRepo: repo, notification: spy);
        await service.ActivateAsync(domain, actorId: "test");

        spy.VerifiedDomainIds.Should().Contain(domain.Id);
        spy.TimedOutDomainIds.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Activate_NotificationThrows_StillReturnsSuccess()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);

        var service = CreateService(domainRepo: repo, notification: new ThrowingNotificationService());
        var result = await service.ActivateAsync(domain, actorId: "test");

        result.Succeeded.Should().BeTrue();
        result.Domain.Status.Should().Be("active");
    }

    // ── Audit log ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Activate_Success_LogsCertificateRequestedEntry()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);
        var auditLog = new InMemoryAuditLogService();

        var service = CreateService(domainRepo: repo, auditLog: auditLog);
        await service.ActivateAsync(domain, actorId: "test");

        var entries = await auditLog.QueryAsync(domain.TenantId, action: "domain.certificate_requested");
        entries.TotalCount.Should().Be(1);
        entries.Items[0].ActorType.Should().Be("system");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Activate_Success_LogsActivatedEntry()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);
        var auditLog = new InMemoryAuditLogService();

        var service = CreateService(domainRepo: repo, auditLog: auditLog);
        await service.ActivateAsync(domain, actorId: "dns-polling");

        var entries = await auditLog.QueryAsync(domain.TenantId, action: "domain.activated");
        entries.TotalCount.Should().Be(1);
        entries.Items[0].ActorId.Should().Be("dns-polling");
        entries.Items[0].ActorType.Should().Be("system");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Activate_Success_ActorIdIsPreservedInAuditLog()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);
        var auditLog = new InMemoryAuditLogService();

        var service = CreateService(domainRepo: repo, auditLog: auditLog);
        await service.ActivateAsync(domain, actorId: "manual-verify");

        var entries = await auditLog.QueryAsync(domain.TenantId, action: "domain.activated");
        entries.Items[0].ActorId.Should().Be("manual-verify");
    }

    // ── Idempotency / edge cases ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Activate_CancelledMidWay_PropagatesOperationCancelledException()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var service = CreateService(domainRepo: repo);
        var act = () => service.ActivateAsync(domain, actorId: "test", ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Test doubles ──────────────────────────────────────────────────────────

    private sealed class SpyCertificateService(
        Func<Guid, Guid, string, Task<CertificateRequestResult>>? onRequest = null)
        : ICertificateService
    {
        public Task<CertificateRequestResult> RequestCertificateAsync(
            Guid tenantId, Guid domainId, string hostname) =>
            onRequest != null
                ? onRequest(tenantId, domainId, hostname)
                : Task.FromResult(new CertificateRequestResult
                {
                    CertificateArn = "arn:aws:acm:us-east-1:123:certificate/spy",
                    Status = "pending",
                    ValidationRecords = []
                });

        public Task<CertificateStatusResult> GetCertificateStatusAsync(string certificateArn) =>
            Task.FromResult(new CertificateStatusResult
            {
                CertificateArn = certificateArn,
                Status = "pending",
                SubjectAlternativeNames = []
            });

        public Task<bool> DeleteCertificateAsync(string certificateArn) =>
            Task.FromResult(true);
    }

    private sealed class ThrowingCertificateService : ICertificateService
    {
        public Task<CertificateRequestResult> RequestCertificateAsync(
            Guid tenantId, Guid domainId, string hostname) =>
            throw new InvalidOperationException("Simulated ACM failure");

        public Task<CertificateStatusResult> GetCertificateStatusAsync(string certificateArn) =>
            throw new InvalidOperationException("Simulated ACM failure");

        public Task<bool> DeleteCertificateAsync(string certificateArn) =>
            Task.FromResult(false);
    }

    private sealed class ThrowingDistributionService : IDistributionService
    {
        public Task<QuotaCheckResult> CheckQuotaAsync() =>
            Task.FromResult(new QuotaCheckResult
            {
                IsWithinQuota = true,
                CurrentCount = 1,
                QuotaLimit = 10000,
                IsNearingLimit = false
            });

        public Task<DistributionTenantResult> CreateDistributionTenantAsync(
            Guid tenantId, Guid domainId, string hostname, string? certificateArn) =>
            throw new InvalidOperationException("Simulated CloudFront failure");

        public Task<DistributionTenantResult> UpdateDistributionTenantAsync(
            string distributionTenantId, string etag, string hostname, string? certificateArn) =>
            throw new InvalidOperationException("Simulated CloudFront failure");

        public Task<bool> DeleteDistributionTenantAsync(string distributionTenantId) =>
            Task.FromResult(true);

        public Task<DistributionTenantStatusResult> GetDistributionTenantStatusAsync(
            string distributionTenantId) =>
            throw new InvalidOperationException("Simulated CloudFront failure");
    }

    private sealed class SpyNotificationService : IDomainStatusNotificationService
    {
        public List<Guid> VerifiedDomainIds { get; } = [];
        public List<Guid> TimedOutDomainIds { get; } = [];

        public Task NotifyVerifiedAsync(DomainEntity domain, CancellationToken ct = default)
        {
            VerifiedDomainIds.Add(domain.Id);
            return Task.CompletedTask;
        }

        public Task NotifyVerificationTimedOutAsync(DomainEntity domain, CancellationToken ct = default)
        {
            TimedOutDomainIds.Add(domain.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingNotificationService : IDomainStatusNotificationService
    {
        public Task NotifyVerifiedAsync(DomainEntity domain, CancellationToken ct = default) =>
            throw new InvalidOperationException("Simulated notification failure");

        public Task NotifyVerificationTimedOutAsync(DomainEntity domain, CancellationToken ct = default) =>
            throw new InvalidOperationException("Simulated notification failure");
    }
}
