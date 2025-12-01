using ControlPlane.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ControlPlane.UnitTests.Services;

public sealed class LinkReScanServiceTests
{
    private static LinkReScanService CreateService(
        InMemoryLinkRepository? linkRepo = null,
        IUrlSafetyService? safetyService = null,
        InMemoryAuditLogService? auditLog = null,
        LinkReScanOptions? options = null)
    {
        return new LinkReScanService(
            linkRepo ?? new InMemoryLinkRepository(),
            safetyService ?? new TestUrlSafetyService(),
            auditLog ?? new InMemoryAuditLogService(),
            Options.Create(options ?? new LinkReScanOptions()),
            NullLogger<LinkReScanService>.Instance);
    }

    private static LinkEntity CreateLink(
        Guid id, Guid tenantId, string destinationUrl, string status = "active", long clickCount = 0)
    {
        return new LinkEntity
        {
            Id = id,
            TenantId = tenantId,
            DomainId = Guid.NewGuid(),
            DestinationUrl = destinationUrl,
            Slug = "test-" + id.ToString("N")[..6],
            Status = status,
            ClickCount = clickCount,
            CreatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunScanCycle_MaliciousDestination_QuarantinesLink()
    {
        var linkRepo = new InMemoryLinkRepository();
        var link = CreateLink(Guid.NewGuid(), Guid.NewGuid(), "https://evil.com");
        await linkRepo.CreateAsync(link);

        var safetyService = new TestUrlSafetyService
        {
            Verdict = UrlSafetyVerdict.Malicious,
            Reason = "blocked host",
            Source = "blocklist"
        };
        var auditLog = new InMemoryAuditLogService();

        var service = CreateService(linkRepo, safetyService, auditLog);

        await service.RunScanCycleAsync(CancellationToken.None);

        var updated = await linkRepo.GetByIdAsync(link.TenantId, link.Id);
        updated.Should().NotBeNull();
        updated!.Status.Should().Be("quarantined");

        var entries = await auditLog.QueryAsync(link.TenantId, action: "link.quarantined_by_rescan");
        entries.TotalCount.Should().Be(1);
        entries.Items[0].ActorType.Should().Be("system");
        entries.Items[0].ActorId.Should().Be("rescan");
        entries.Items[0].Details.Should().Contain("evil.com");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunScanCycle_SuspiciousDestinationOnActiveLink_QuarantinesLink()
    {
        var linkRepo = new InMemoryLinkRepository();
        var link = CreateLink(Guid.NewGuid(), Guid.NewGuid(), "https://suspicious.example.com");
        await linkRepo.CreateAsync(link);

        var safetyService = new TestUrlSafetyService
        {
            Verdict = UrlSafetyVerdict.Suspicious,
            Reason = "suspicious pattern",
            Source = "suspicious_patterns"
        };
        var auditLog = new InMemoryAuditLogService();

        var service = CreateService(linkRepo, safetyService, auditLog);

        await service.RunScanCycleAsync(CancellationToken.None);

        var updated = await linkRepo.GetByIdAsync(link.TenantId, link.Id);
        updated!.Status.Should().Be("quarantined");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunScanCycle_SafeDestinationOnQuarantinedLink_UnquarantinesLink()
    {
        var linkRepo = new InMemoryLinkRepository();
        var link = CreateLink(Guid.NewGuid(), Guid.NewGuid(), "https://now-clean.example.com", status: "quarantined");
        await linkRepo.CreateAsync(link);

        var safetyService = new TestUrlSafetyService
        {
            Verdict = UrlSafetyVerdict.Safe,
            Reason = "no threats",
            Source = "local"
        };
        var auditLog = new InMemoryAuditLogService();

        var service = CreateService(linkRepo, safetyService, auditLog);

        await service.RunScanCycleAsync(CancellationToken.None);

        var updated = await linkRepo.GetByIdAsync(link.TenantId, link.Id);
        updated!.Status.Should().Be("active");

        var entries = await auditLog.QueryAsync(link.TenantId, action: "link.unquarantined_by_rescan");
        entries.TotalCount.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunScanCycle_SafeDestinationOnActiveLink_NoChange()
    {
        var linkRepo = new InMemoryLinkRepository();
        var link = CreateLink(Guid.NewGuid(), Guid.NewGuid(), "https://clean.example.com");
        await linkRepo.CreateAsync(link);

        var safetyService = new TestUrlSafetyService { Verdict = UrlSafetyVerdict.Safe };
        var auditLog = new InMemoryAuditLogService();

        var service = CreateService(linkRepo, safetyService, auditLog);

        await service.RunScanCycleAsync(CancellationToken.None);

        var updated = await linkRepo.GetByIdAsync(link.TenantId, link.Id);
        updated!.Status.Should().Be("active");

        var entries = await auditLog.QueryAsync(link.TenantId);
        entries.TotalCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunScanCycle_AlreadyQuarantinedWithMalicious_NoDuplicateQuarantine()
    {
        var linkRepo = new InMemoryLinkRepository();
        var link = CreateLink(Guid.NewGuid(), Guid.NewGuid(), "https://still-evil.com", status: "quarantined");
        await linkRepo.CreateAsync(link);

        var safetyService = new TestUrlSafetyService
        {
            Verdict = UrlSafetyVerdict.Malicious,
            Reason = "still blocked",
            Source = "blocklist"
        };
        var auditLog = new InMemoryAuditLogService();

        var service = CreateService(linkRepo, safetyService, auditLog);

        await service.RunScanCycleAsync(CancellationToken.None);

        var updated = await linkRepo.GetByIdAsync(link.TenantId, link.Id);
        updated!.Status.Should().Be("quarantined");

        var entries = await auditLog.QueryAsync(link.TenantId, action: "link.quarantined_by_rescan");
        entries.TotalCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunScanCycle_HighTrafficLinksScannedFirst()
    {
        var linkRepo = new InMemoryLinkRepository();
        var tenantId = Guid.NewGuid();
        var lowTraffic = CreateLink(Guid.NewGuid(), tenantId, "https://low.example.com", clickCount: 10);
        var highTraffic = CreateLink(Guid.NewGuid(), tenantId, "https://high.example.com", clickCount: 5000);

        // Add low-traffic first so insertion order doesn't influence the result
        await linkRepo.CreateAsync(lowTraffic);
        await linkRepo.CreateAsync(highTraffic);

        // Return different verdicts so we can verify both were scanned
        var safetyService = new TestUrlSafetyService
        {
            Verdict = UrlSafetyVerdict.Malicious,
            Reason = "test",
            Source = "test-double"
        };
        var auditLog = new InMemoryAuditLogService();

        var service = CreateService(linkRepo, safetyService, auditLog);

        await service.RunScanCycleAsync(CancellationToken.None);

        // Both should be quarantined (both scanned)
        var high = await linkRepo.GetByIdAsync(tenantId, highTraffic.Id);
        high!.Status.Should().Be("quarantined");

        var low = await linkRepo.GetByIdAsync(tenantId, lowTraffic.Id);
        low!.Status.Should().Be("quarantined");

        // Verify audit entries exist for both
        var entries = await auditLog.QueryAsync(tenantId, action: "link.quarantined_by_rescan");
        entries.TotalCount.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunScanCycle_ScanFailure_LinkStatusUnchanged()
    {
        var linkRepo = new InMemoryLinkRepository();
        var link = CreateLink(Guid.NewGuid(), Guid.NewGuid(), "https://fail.example.com");
        await linkRepo.CreateAsync(link);

        var failingService = new FailingUrlSafetyService();
        var service = CreateService(linkRepo, failingService);

        await service.RunScanCycleAsync(CancellationToken.None);

        var updated = await linkRepo.GetByIdAsync(link.TenantId, link.Id);
        updated!.Status.Should().Be("active");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunScanCycle_EmptyLinkSet_CompletesCleanly()
    {
        var linkRepo = new InMemoryLinkRepository();
        var service = CreateService(linkRepo);

        var act = () => service.RunScanCycleAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunScanCycle_DeletedLinks_ExcludedFromScan()
    {
        var linkRepo = new InMemoryLinkRepository();
        var activeLink = CreateLink(Guid.NewGuid(), Guid.NewGuid(), "https://active.example.com");
        var deletedLink = CreateLink(Guid.NewGuid(), Guid.NewGuid(), "https://deleted.example.com");

        await linkRepo.CreateAsync(activeLink);
        await linkRepo.CreateAsync(deletedLink);
        await linkRepo.SoftDeleteAsync(deletedLink.TenantId, deletedLink.Id);

        var safetyService = new TestUrlSafetyService
        {
            Verdict = UrlSafetyVerdict.Malicious,
            Reason = "test",
            Source = "test-double"
        };
        var auditLog = new InMemoryAuditLogService();

        var service = CreateService(linkRepo, safetyService, auditLog);

        await service.RunScanCycleAsync(CancellationToken.None);

        // Active link should be quarantined
        var active = await linkRepo.GetByIdAsync(activeLink.TenantId, activeLink.Id);
        active!.Status.Should().Be("quarantined");

        // Deleted link should NOT be in the scan results (it's deleted, not quarantined by scan)
        var entries = await auditLog.QueryAsync(activeLink.TenantId, action: "link.quarantined_by_rescan");
        entries.TotalCount.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunScanCycle_CancelledDuringScan_StopsCleanly()
    {
        var linkRepo = new InMemoryLinkRepository();
        var link = CreateLink(Guid.NewGuid(), Guid.NewGuid(), "https://slow.example.com");
        await linkRepo.CreateAsync(link);

        var safetyService = new TestUrlSafetyService();

        var service = CreateService(linkRepo, safetyService);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.RunScanCycleAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunScanCycle_RespectsBatchSize()
    {
        var linkRepo = new InMemoryLinkRepository();
        var tenantId = Guid.NewGuid();
        for (int i = 0; i < 10; i++)
        {
            await linkRepo.CreateAsync(CreateLink(
                Guid.NewGuid(), tenantId, $"https://link-{i}.example.com", clickCount: i));
        }

        var safetyService = new TestUrlSafetyService
        {
            Verdict = UrlSafetyVerdict.Malicious,
            Reason = "test",
            Source = "test-double"
        };
        var auditLog = new InMemoryAuditLogService();

        var options = new LinkReScanOptions { ScanBatchSize = 3 };
        var service = CreateService(linkRepo, safetyService, auditLog, options);

        await service.RunScanCycleAsync(CancellationToken.None);

        var entries = await auditLog.QueryAsync(tenantId, action: "link.quarantined_by_rescan");
        entries.TotalCount.Should().Be(3);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunScanCycle_SuspendedLinks_ExcludedFromScan()
    {
        var linkRepo = new InMemoryLinkRepository();
        var activeLink = CreateLink(Guid.NewGuid(), Guid.NewGuid(), "https://active.example.com");
        var suspendedLink = CreateLink(Guid.NewGuid(), Guid.NewGuid(), "https://suspended.example.com", status: "suspended");

        await linkRepo.CreateAsync(activeLink);
        await linkRepo.CreateAsync(suspendedLink);

        var safetyService = new TestUrlSafetyService
        {
            Verdict = UrlSafetyVerdict.Malicious,
            Reason = "test",
            Source = "test-double"
        };
        var auditLog = new InMemoryAuditLogService();

        var service = CreateService(linkRepo, safetyService, auditLog);

        await service.RunScanCycleAsync(CancellationToken.None);

        var entries = await auditLog.QueryAsync(activeLink.TenantId, action: "link.quarantined_by_rescan");
        entries.TotalCount.Should().Be(1);
    }

    /// <summary>Test double that throws on every scan to simulate failures.</summary>
    private sealed class FailingUrlSafetyService : IUrlSafetyService
    {
        public Task<UrlSafetyResult> ScanAsync(string url, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Simulated scan failure");
        }
    }
}
