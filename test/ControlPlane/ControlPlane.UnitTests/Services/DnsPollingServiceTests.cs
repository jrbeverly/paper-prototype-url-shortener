using ControlPlane.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ControlPlane.UnitTests.Services;

public sealed class DnsPollingServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DnsPollingService CreateService(
        InMemoryDomainRepository? domainRepo = null,
        IDnsVerificationService? dns = null,
        ICertificateService? certService = null,
        IDistributionService? distService = null,
        InMemoryAuditLogService? auditLog = null,
        IDomainStatusNotificationService? notification = null,
        DnsPollingOptions? options = null,
        IDomainActivationService? activation = null)
    {
        domainRepo ??= new InMemoryDomainRepository();
        auditLog ??= new InMemoryAuditLogService();
        notification ??= new SpyNotificationService();

        // Build the activation service from the same cert/dist/notification doubles so that
        // tests which pass specific test doubles continue to exercise them through the service.
        activation ??= new DomainActivationService(
            domainRepo,
            certService ?? new InMemoryCertificateService(
                Options.Create(new CertificateOptions()), NullLogger<InMemoryCertificateService>.Instance),
            distService ?? new InMemoryDistributionService(
                Options.Create(new DistributionOptions()), NullLogger<InMemoryDistributionService>.Instance),
            auditLog,
            notification,
            NullLogger<DomainActivationService>.Instance);

        return new DnsPollingService(
            domainRepo,
            dns ?? PassDns(),
            activation,
            auditLog,
            notification,
            Options.Create(options ?? new DnsPollingOptions()),
            NullLogger<DnsPollingService>.Instance);
    }

    private static DomainEntity CreateDomain(
        string status = "pending_verification",
        DateTime? createdAt = null,
        DateTime? lastVerifiedAt = null,
        string hostname = "links.example.com")
    {
        return new DomainEntity
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Hostname = hostname,
            Status = status,
            VerificationCode = "abc123",
            CnameTarget = "domains.short.io",
            CreatedAt = createdAt ?? DateTime.UtcNow,
            LastVerifiedAt = lastVerifiedAt
        };
    }

    private static IDnsVerificationService PassDns() => new StubDnsService(pass: true);
    private static IDnsVerificationService FailDns() => new StubDnsService(pass: false);

    // ── Activation ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_DnsPassesPendingDomain_TransitionsToActive()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);

        var service = CreateService(domainRepo: repo, dns: PassDns());
        await service.RunPollCycleAsync(CancellationToken.None);

        var updated = await repo.GetByIdAsync(domain.TenantId, domain.Id);
        updated!.Status.Should().Be("active");
        updated.LastVerifiedAt.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_DnsPassesVerifyingDomain_TransitionsToActive()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain(status: "verifying");
        await repo.CreateAsync(domain);

        var service = CreateService(domainRepo: repo, dns: PassDns());
        await service.RunPollCycleAsync(CancellationToken.None);

        var updated = await repo.GetByIdAsync(domain.TenantId, domain.Id);
        updated!.Status.Should().Be("active");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_DnsPassesDomain_ProvisionsCertificate()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);
        var auditLog = new InMemoryAuditLogService();

        var service = CreateService(domainRepo: repo, dns: PassDns(), auditLog: auditLog);
        await service.RunPollCycleAsync(CancellationToken.None);

        var updated = await repo.GetByIdAsync(domain.TenantId, domain.Id);
        updated!.CertificateArn.Should().NotBeNullOrEmpty();

        var certEntry = await auditLog.QueryAsync(domain.TenantId, action: "domain.certificate_requested");
        certEntry.TotalCount.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_DnsPassesDomain_CreatesDistributionTenant()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);

        var service = CreateService(domainRepo: repo, dns: PassDns());
        await service.RunPollCycleAsync(CancellationToken.None);

        var updated = await repo.GetByIdAsync(domain.TenantId, domain.Id);
        updated!.DistributionTenantId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_DnsPassesDomain_LogsVerifiedAuditEntry()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);
        var auditLog = new InMemoryAuditLogService();

        var service = CreateService(domainRepo: repo, dns: PassDns(), auditLog: auditLog);
        await service.RunPollCycleAsync(CancellationToken.None);

        var entries = await auditLog.QueryAsync(domain.TenantId, action: "domain.verified");
        entries.TotalCount.Should().Be(1);
        entries.Items[0].ActorId.Should().Be("dns-polling");
        entries.Items[0].ActorType.Should().Be("system");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_DnsPassesDomain_SendsVerifiedNotification()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);
        var spy = new SpyNotificationService();

        var service = CreateService(domainRepo: repo, dns: PassDns(), notification: spy);
        await service.RunPollCycleAsync(CancellationToken.None);

        spy.VerifiedDomainIds.Should().Contain(domain.Id);
        spy.TimedOutDomainIds.Should().BeEmpty();
    }

    // ── Failure (DNS not yet propagated) ─────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_DnsFails_StatusRemainsInPending()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);

        var service = CreateService(domainRepo: repo, dns: FailDns());
        await service.RunPollCycleAsync(CancellationToken.None);

        var updated = await repo.GetByIdAsync(domain.TenantId, domain.Id);
        updated!.Status.Should().Be("pending_verification");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_DnsFails_UpdatesLastVerifiedAt()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain(lastVerifiedAt: null);
        await repo.CreateAsync(domain);

        var before = DateTime.UtcNow;
        var service = CreateService(domainRepo: repo, dns: FailDns());
        await service.RunPollCycleAsync(CancellationToken.None);

        var updated = await repo.GetByIdAsync(domain.TenantId, domain.Id);
        updated!.LastVerifiedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_DnsFails_LogsPollFailedAuditEntry()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);
        var auditLog = new InMemoryAuditLogService();

        var service = CreateService(domainRepo: repo, dns: FailDns(), auditLog: auditLog);
        await service.RunPollCycleAsync(CancellationToken.None);

        var entries = await auditLog.QueryAsync(domain.TenantId, action: "domain.dns_poll_failed");
        entries.TotalCount.Should().Be(1);
        entries.Items[0].ActorType.Should().Be("system");
    }

    // ── Timeout ───────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_DomainExceedsMaxWindow_TransitionsToTimeout()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain(createdAt: DateTime.UtcNow.AddHours(-(DnsPollingOptions.MaxPollingHours + 1)));
        await repo.CreateAsync(domain);

        var service = CreateService(domainRepo: repo, dns: PassDns());
        await service.RunPollCycleAsync(CancellationToken.None);

        var updated = await repo.GetByIdAsync(domain.TenantId, domain.Id);
        updated!.Status.Should().Be("verification_timeout");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_DomainExceedsMaxWindow_DoesNotActivateEvenIfDnsPasses()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain(createdAt: DateTime.UtcNow.AddHours(-(DnsPollingOptions.MaxPollingHours + 1)));
        await repo.CreateAsync(domain);

        // DNS passes but domain is too old — should time out, not activate.
        var service = CreateService(domainRepo: repo, dns: PassDns());
        await service.RunPollCycleAsync(CancellationToken.None);

        var updated = await repo.GetByIdAsync(domain.TenantId, domain.Id);
        updated!.Status.Should().Be("verification_timeout");
        updated.CertificateArn.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_DomainExceedsMaxWindow_LogsTimeoutAuditEntry()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain(createdAt: DateTime.UtcNow.AddHours(-(DnsPollingOptions.MaxPollingHours + 1)));
        await repo.CreateAsync(domain);
        var auditLog = new InMemoryAuditLogService();

        var service = CreateService(domainRepo: repo, dns: FailDns(), auditLog: auditLog);
        await service.RunPollCycleAsync(CancellationToken.None);

        var entries = await auditLog.QueryAsync(domain.TenantId, action: "domain.verification_timeout");
        entries.TotalCount.Should().Be(1);
        entries.Items[0].ActorId.Should().Be("dns-polling");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_DomainExceedsMaxWindow_SendsTimeoutNotification()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain(createdAt: DateTime.UtcNow.AddHours(-(DnsPollingOptions.MaxPollingHours + 1)));
        await repo.CreateAsync(domain);
        var spy = new SpyNotificationService();

        var service = CreateService(domainRepo: repo, dns: FailDns(), notification: spy);
        await service.RunPollCycleAsync(CancellationToken.None);

        spy.TimedOutDomainIds.Should().Contain(domain.Id);
        spy.VerifiedDomainIds.Should().BeEmpty();
    }

    // ── Backoff / skipping ────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_DomainCheckedRecently_IsSkipped()
    {
        var repo = new InMemoryDomainRepository();
        // Checked 30 seconds ago; early interval is 5 min → too soon.
        var domain = CreateDomain(lastVerifiedAt: DateTime.UtcNow.AddSeconds(-30));
        await repo.CreateAsync(domain);

        var spy = new CountingDnsService();
        var service = CreateService(domainRepo: repo, dns: spy);
        await service.RunPollCycleAsync(CancellationToken.None);

        spy.CallCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_DomainNeverChecked_IsPolledImmediately()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain(lastVerifiedAt: null);
        await repo.CreateAsync(domain);

        var spy = new CountingDnsService();
        var service = CreateService(domainRepo: repo, dns: spy);
        await service.RunPollCycleAsync(CancellationToken.None);

        spy.CallCount.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_DomainCheckedLongEnoughAgo_IsPolledAgain()
    {
        var repo = new InMemoryDomainRepository();
        // Checked 10 minutes ago; early interval is 5 min → eligible.
        var domain = CreateDomain(lastVerifiedAt: DateTime.UtcNow.AddMinutes(-10));
        await repo.CreateAsync(domain);

        var spy = new CountingDnsService();
        var service = CreateService(domainRepo: repo, dns: spy);
        await service.RunPollCycleAsync(CancellationToken.None);

        spy.CallCount.Should().Be(1);
    }

    // ── GetPollInterval backoff tiers ─────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void GetPollInterval_AgeWithinEarlyWindow_ReturnsEarlyInterval()
    {
        var options = new DnsPollingOptions { EarlyWindowMinutes = 30, EarlyPollIntervalMinutes = 5 };
        var service = CreateService(options: options);

        var interval = service.GetPollInterval(TimeSpan.FromMinutes(10));

        interval.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetPollInterval_AgeWithinMidWindow_ReturnsMidInterval()
    {
        var options = new DnsPollingOptions
        {
            EarlyWindowMinutes = 30, EarlyPollIntervalMinutes = 5,
            MidWindowMinutes = 120, MidPollIntervalMinutes = 15
        };
        var service = CreateService(options: options);

        var interval = service.GetPollInterval(TimeSpan.FromMinutes(60));

        interval.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetPollInterval_AgeWithinLateWindow_ReturnsLateInterval()
    {
        var options = new DnsPollingOptions
        {
            EarlyWindowMinutes = 30, MidWindowMinutes = 120,
            LateWindowMinutes = 720, LatePollIntervalMinutes = 30
        };
        var service = CreateService(options: options);

        var interval = service.GetPollInterval(TimeSpan.FromHours(6));

        interval.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void GetPollInterval_AgeBeyondLateWindow_ReturnsFinalInterval()
    {
        var options = new DnsPollingOptions
        {
            EarlyWindowMinutes = 30, MidWindowMinutes = 120,
            LateWindowMinutes = 720, FinalPollIntervalMinutes = 60
        };
        var service = CreateService(options: options);

        var interval = service.GetPollInterval(TimeSpan.FromHours(20));

        interval.Should().Be(TimeSpan.FromMinutes(60));
    }

    // ── Exclusions ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_ActiveDomain_IsNotPolled()
    {
        var repo = new InMemoryDomainRepository();
        var activeDomain = CreateDomain(status: "active");
        await repo.CreateAsync(activeDomain);

        var spy = new CountingDnsService();
        var service = CreateService(domainRepo: repo, dns: spy);
        await service.RunPollCycleAsync(CancellationToken.None);

        spy.CallCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_DeletedDomain_IsNotPolled()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);
        await repo.SoftDeleteAsync(domain.TenantId, domain.Id);

        var spy = new CountingDnsService();
        var service = CreateService(domainRepo: repo, dns: spy);
        await service.RunPollCycleAsync(CancellationToken.None);

        spy.CallCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_TimedOutDomain_IsNotPolledAgain()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain(status: "verification_timeout");
        await repo.CreateAsync(domain);

        var spy = new CountingDnsService();
        var service = CreateService(domainRepo: repo, dns: spy);
        await service.RunPollCycleAsync(CancellationToken.None);

        spy.CallCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_NoPendingDomains_CompletesCleanly()
    {
        var repo = new InMemoryDomainRepository();
        var service = CreateService(domainRepo: repo);

        var act = () => service.RunPollCycleAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_DnsCheckThrows_StatusUnchanged()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);

        var service = CreateService(domainRepo: repo, dns: new ThrowingDnsService());
        await service.RunPollCycleAsync(CancellationToken.None);

        // Status should be unchanged (pending_verification), not crashed.
        var updated = await repo.GetByIdAsync(domain.TenantId, domain.Id);
        updated!.Status.Should().Be("pending_verification");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_CancelledBeforeProcessing_ThrowsOperationCancelled()
    {
        var repo = new InMemoryDomainRepository();
        var domain = CreateDomain();
        await repo.CreateAsync(domain);

        var service = CreateService(domainRepo: repo, dns: PassDns());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.RunPollCycleAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Multiple domains ──────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_MultiplePendingDomains_AllAreProcessed()
    {
        var repo = new InMemoryDomainRepository();
        var d1 = CreateDomain(hostname: "a.example.com");
        var d2 = CreateDomain(hostname: "b.example.com");
        await repo.CreateAsync(d1);
        await repo.CreateAsync(d2);

        var spy = new CountingDnsService();
        var service = CreateService(domainRepo: repo, dns: spy);
        await service.RunPollCycleAsync(CancellationToken.None);

        spy.CallCount.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunPollCycle_MixedEligibility_OnlyEligibleDomainsPolled()
    {
        var repo = new InMemoryDomainRepository();
        // Eligible: never checked
        var eligible = CreateDomain(hostname: "new.example.com", lastVerifiedAt: null);
        // Not eligible: checked 10 seconds ago (within early 5-min window)
        var notEligible = CreateDomain(hostname: "recent.example.com",
            lastVerifiedAt: DateTime.UtcNow.AddSeconds(-10));

        await repo.CreateAsync(eligible);
        await repo.CreateAsync(notEligible);

        var spy = new CountingDnsService();
        var service = CreateService(domainRepo: repo, dns: spy);
        await service.RunPollCycleAsync(CancellationToken.None);

        spy.CallCount.Should().Be(1);
    }

    // ── Test doubles ──────────────────────────────────────────────────────────

    private sealed class StubDnsService(bool pass) : IDnsVerificationService
    {
        public Task<DnsVerificationResult> VerifyAsync(
            string hostname, string expectedTxtValue, string expectedCnameValue) =>
            Task.FromResult(new DnsVerificationResult
            {
                TxtPassed = pass,
                ExpectedTxtValue = expectedTxtValue,
                ActualTxtValue = pass ? expectedTxtValue : null,
                CnamePassed = pass,
                ExpectedCnameValue = expectedCnameValue,
                ActualCnameValue = pass ? expectedCnameValue : null
            });
    }

    private sealed class CountingDnsService : IDnsVerificationService
    {
        public int CallCount { get; private set; }

        public Task<DnsVerificationResult> VerifyAsync(
            string hostname, string expectedTxtValue, string expectedCnameValue)
        {
            CallCount++;
            return Task.FromResult(new DnsVerificationResult
            {
                TxtPassed = true,
                ExpectedTxtValue = expectedTxtValue,
                ActualTxtValue = expectedTxtValue,
                CnamePassed = true,
                ExpectedCnameValue = expectedCnameValue,
                ActualCnameValue = expectedCnameValue
            });
        }
    }

    private sealed class ThrowingDnsService : IDnsVerificationService
    {
        public Task<DnsVerificationResult> VerifyAsync(
            string hostname, string expectedTxtValue, string expectedCnameValue) =>
            throw new InvalidOperationException("Simulated DNS failure");
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
}
