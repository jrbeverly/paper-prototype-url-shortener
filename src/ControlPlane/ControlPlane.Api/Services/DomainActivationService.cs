namespace ControlPlane.Api.Services;

/// <summary>
/// Orchestrates the domain activation pipeline after DNS verification succeeds.
/// State machine: DNS-verified → <c>certificate_provisioning</c>
///   → (on cert failure) <c>certificate_failed</c>
///   → (on cert success) <c>active</c>
/// </summary>
public sealed class DomainActivationService : IDomainActivationService
{
    private readonly IDomainRepository _domainRepository;
    private readonly ICertificateService _certificateService;
    private readonly IDistributionService _distributionService;
    private readonly IAuditLogService _auditLog;
    private readonly IDomainStatusNotificationService _notification;
    private readonly ILogger<DomainActivationService> _logger;

    public DomainActivationService(
        IDomainRepository domainRepository,
        ICertificateService certificateService,
        IDistributionService distributionService,
        IAuditLogService auditLog,
        IDomainStatusNotificationService notification,
        ILogger<DomainActivationService> logger)
    {
        _domainRepository = domainRepository;
        _certificateService = certificateService;
        _distributionService = distributionService;
        _auditLog = auditLog;
        _notification = notification;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DomainActivationResult> ActivateAsync(
        DomainEntity domain,
        string actorId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var now = DateTime.UtcNow;

        // Mark that we are in the provisioning pipeline. This makes the intermediate
        // state visible and prevents concurrent activations from racing.
        var provisioning = domain with
        {
            Status = "certificate_provisioning",
            LastVerifiedAt = now,
            UpdatedAt = now
        };
        await _domainRepository.UpdateAsync(provisioning);

        // ── Step 1: ACM certificate ────────────────────────────────────────────
        CertificateRequestResult certResult;
        try
        {
            certResult = await _certificateService.RequestCertificateAsync(
                domain.TenantId, domain.Id, domain.Hostname);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Certificate provisioning failed for domain {DomainId} ({Hostname}): {Message}",
                domain.Id, domain.Hostname, ex.Message);

            var now2 = DateTime.UtcNow;
            var failed = provisioning with
            {
                Status = "certificate_failed",
                UpdatedAt = now2
            };
            await _domainRepository.UpdateAsync(failed);

            await _auditLog.LogAsync(new AuditLogEntry
            {
                Id = Guid.NewGuid(),
                TenantId = domain.TenantId,
                Action = "domain.certificate_failed",
                ActorId = actorId,
                ActorType = "system",
                ResourceType = "domain",
                ResourceId = domain.Id.ToString(),
                OldValue = AuditLogEntryFactory.Snapshot(new { status = "certificate_provisioning" }),
                NewValue = AuditLogEntryFactory.Snapshot(new { status = "certificate_failed" }),
                Details = ex.Message,
                Timestamp = now2
            }, ct);

            return DomainActivationResult.Failure(
                failed,
                $"Certificate provisioning failed: {ex.Message}");
        }

        // Persist the certificate fields before attempting the distribution step.
        var withCert = provisioning with
        {
            CertificateArn = certResult.CertificateArn,
            CertificateStatus = certResult.Status,
            UpdatedAt = DateTime.UtcNow
        };
        await _domainRepository.UpdateAsync(withCert);

        await _auditLog.LogAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            TenantId = domain.TenantId,
            Action = "domain.certificate_requested",
            ActorId = actorId,
            ActorType = "system",
            ResourceType = "domain",
            ResourceId = domain.Id.ToString(),
            NewValue = AuditLogEntryFactory.Snapshot(new
            {
                certificateArn = certResult.CertificateArn,
                certificateStatus = certResult.Status
            }),
            Timestamp = DateTime.UtcNow
        }, ct);

        _logger.LogInformation(
            "Certificate provisioned for domain {DomainId}: {Arn}",
            domain.Id, certResult.CertificateArn);

        // ── Step 2: CloudFront distribution tenant (non-fatal on failure) ──────
        // Distribution failure is logged as an error but does not prevent the domain
        // from reaching the active state — the customer can retry via other endpoints.
        var withDist = withCert;
        try
        {
            var quota = await _distributionService.CheckQuotaAsync();
            if (!quota.IsWithinQuota)
            {
                _logger.LogError(
                    "Distribution tenant quota exhausted for domain {DomainId} ({Hostname}): count={Count} limit={Limit}",
                    domain.Id, domain.Hostname, quota.CurrentCount, quota.QuotaLimit);
            }
            else
            {
                var distResult = await _distributionService.CreateDistributionTenantAsync(
                    domain.TenantId, domain.Id, domain.Hostname, certResult.CertificateArn);

                withDist = withCert with
                {
                    DistributionTenantId = distResult.DistributionTenantId,
                    DistributionTenantStatus = distResult.Status,
                    DistributionTenantETag = distResult.ETag
                };
                await _domainRepository.UpdateAsync(withDist);

                _logger.LogInformation(
                    "Distribution tenant created for domain {DomainId}: id={TenantId} status={Status}",
                    domain.Id, distResult.DistributionTenantId, distResult.Status);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Distribution tenant creation failed for domain {DomainId} ({Hostname}): {Message}",
                domain.Id, domain.Hostname, ex.Message);
        }

        // ── Step 3: Transition to active ──────────────────────────────────────
        var now3 = DateTime.UtcNow;
        var active = withDist with
        {
            Status = "active",
            UpdatedAt = now3
        };
        await _domainRepository.UpdateAsync(active);

        await _auditLog.LogAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            TenantId = domain.TenantId,
            Action = "domain.activated",
            ActorId = actorId,
            ActorType = "system",
            ResourceType = "domain",
            ResourceId = domain.Id.ToString(),
            OldValue = AuditLogEntryFactory.Snapshot(new { status = domain.Status }),
            NewValue = AuditLogEntryFactory.Snapshot(new { status = "active" }),
            Timestamp = now3
        }, ct);

        _logger.LogInformation(
            "Domain {DomainId} ({Hostname}) activated successfully",
            domain.Id, domain.Hostname);

        // ── Step 4: Customer notification (non-fatal on failure) ──────────────
        try
        {
            await _notification.NotifyVerifiedAsync(active, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Verified notification failed for domain {DomainId} ({Hostname})",
                domain.Id, domain.Hostname);
        }

        return DomainActivationResult.Success(active);
    }
}
