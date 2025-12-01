using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Services;

/// <summary>
/// Background service that periodically polls DNS records for domains awaiting verification.
/// Uses per-domain exponential backoff — domains are checked frequently at first (every 5 min)
/// and progressively less often as time passes (up to every 60 min). After 48 hours without
/// confirmed DNS records the domain is transitioned to <c>verification_timeout</c>.
/// On DNS success, delegates the full activation pipeline to <see cref="IDomainActivationService"/>.
/// </summary>
public sealed class DnsPollingService : BackgroundService
{
    private readonly IDomainRepository _domainRepository;
    private readonly IDnsVerificationService _dnsVerification;
    private readonly IDomainActivationService _activation;
    private readonly IAuditLogService _auditLog;
    private readonly IDomainStatusNotificationService _notification;
    private readonly DnsPollingOptions _options;
    private readonly ILogger<DnsPollingService> _logger;

    public DnsPollingService(
        IDomainRepository domainRepository,
        IDnsVerificationService dnsVerification,
        IDomainActivationService activation,
        IAuditLogService auditLog,
        IDomainStatusNotificationService notification,
        IOptions<DnsPollingOptions> options,
        ILogger<DnsPollingService> logger)
    {
        _domainRepository = domainRepository;
        _dnsVerification = dnsVerification;
        _activation = activation;
        _auditLog = auditLog;
        _notification = notification;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "DnsPollingService started — tick every {TickMinutes}m, max polling window {MaxHours}h",
            _options.TickIntervalMinutes, DnsPollingOptions.MaxPollingHours);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.TickIntervalMinutes));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunPollCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DNS poll cycle failed with unhandled exception — next tick will retry");
            }
        }
    }

    /// <summary>
    /// Runs one poll cycle: loads all pending domains, filters to those due for a check,
    /// and processes each one. Exposed as <c>internal</c> so unit tests can drive it directly.
    /// </summary>
    internal async Task RunPollCycleAsync(CancellationToken ct)
    {
        var domains = await _domainRepository.GetPendingVerificationAsync();

        if (domains.Count == 0)
        {
            _logger.LogDebug("DNS poll cycle: no domains pending verification");
            return;
        }

        _logger.LogInformation("DNS poll cycle: {Count} domain(s) in pending/verifying status", domains.Count);

        int checked_ = 0, activated = 0, timedOut = 0, skipped = 0;
        var now = DateTime.UtcNow;

        foreach (var domain in domains)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var age = now - domain.CreatedAt;
                var maxWindow = TimeSpan.FromHours(DnsPollingOptions.MaxPollingHours);

                // Transition to timeout if beyond the max polling window.
                if (age >= maxWindow)
                {
                    await TimeOutDomainAsync(domain, ct);
                    timedOut++;
                    continue;
                }

                // Apply per-domain backoff: skip if checked too recently.
                // A domain with no previous check (LastVerifiedAt is null) is always eligible
                // for its first poll; the interval only applies to subsequent checks.
                if (domain.LastVerifiedAt.HasValue)
                {
                    var pollInterval = GetPollInterval(age);
                    if (now - domain.LastVerifiedAt.Value < pollInterval)
                    {
                        skipped++;
                        continue;
                    }
                }

                // Perform the DNS check.
                checked_++;
                var txtValue = $"short-io-verify={domain.VerificationCode}";
                var dnsResult = await _dnsVerification.VerifyAsync(domain.Hostname, txtValue, domain.CnameTarget);

                _logger.LogInformation(
                    "DNS poll check for {Hostname} (domainId={DomainId}): txt={TxtPassed} cname={CnamePassed} ageMinutes={Age:F0}",
                    domain.Hostname, domain.Id, dnsResult.TxtPassed, dnsResult.CnamePassed, age.TotalMinutes);

                if (dnsResult.AllPassed)
                {
                    await ActivateDomainAsync(domain, ct);
                    activated++;
                }
                else
                {
                    // Update LastVerifiedAt so the backoff timer resets correctly.
                    var updated = domain with
                    {
                        Status = "pending_verification",
                        LastVerifiedAt = now,
                        UpdatedAt = now
                    };
                    await _domainRepository.UpdateAsync(updated);

                    await _auditLog.LogAsync(new AuditLogEntry
                    {
                        Id = Guid.NewGuid(),
                        TenantId = domain.TenantId,
                        Action = "domain.dns_poll_failed",
                        ActorId = "dns-polling",
                        ActorType = "system",
                        ResourceType = "domain",
                        ResourceId = domain.Id.ToString(),
                        Details = $"txt={dnsResult.TxtPassed} cname={dnsResult.CnamePassed} ageMinutes={age.TotalMinutes:F0}",
                        Timestamp = now
                    }, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "DNS poll failed for domain {DomainId} ({Hostname}) — status unchanged",
                    domain.Id, domain.Hostname);
            }
        }

        _logger.LogInformation(
            "DNS poll cycle complete — checked {Checked}, activated {Activated}, timedOut {TimedOut}, skipped {Skipped}",
            checked_, activated, timedOut, skipped);
    }

    /// <summary>
    /// Returns the minimum time that must have elapsed since the last check before this domain
    /// is eligible for another poll, based on how long ago it was created (exponential backoff tiers).
    /// </summary>
    internal TimeSpan GetPollInterval(TimeSpan age)
    {
        if (age.TotalMinutes < _options.EarlyWindowMinutes)
            return TimeSpan.FromMinutes(_options.EarlyPollIntervalMinutes);

        if (age.TotalMinutes < _options.MidWindowMinutes)
            return TimeSpan.FromMinutes(_options.MidPollIntervalMinutes);

        if (age.TotalMinutes < _options.LateWindowMinutes)
            return TimeSpan.FromMinutes(_options.LatePollIntervalMinutes);

        return TimeSpan.FromMinutes(_options.FinalPollIntervalMinutes);
    }

    private async Task ActivateDomainAsync(DomainEntity domain, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Log the DNS-verified event before handing off to the activation pipeline.
        await _auditLog.LogAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            TenantId = domain.TenantId,
            Action = "domain.verified",
            ActorId = "dns-polling",
            ActorType = "system",
            ResourceType = "domain",
            ResourceId = domain.Id.ToString(),
            OldValue = AuditLogEntryFactory.Snapshot(new { status = domain.Status }),
            NewValue = AuditLogEntryFactory.Snapshot(new { status = "active" }),
            Details = "source=auto-poll",
            Timestamp = now
        }, ct);

        _logger.LogInformation(
            "Domain {DomainId} ({Hostname}) verified by polling — starting activation pipeline",
            domain.Id, domain.Hostname);

        await _activation.ActivateAsync(domain, actorId: "dns-polling", ct);
    }

    private async Task TimeOutDomainAsync(DomainEntity domain, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var updated = domain with
        {
            Status = "verification_timeout",
            UpdatedAt = now
        };
        await _domainRepository.UpdateAsync(updated);

        await _auditLog.LogAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            TenantId = domain.TenantId,
            Action = "domain.verification_timeout",
            ActorId = "dns-polling",
            ActorType = "system",
            ResourceType = "domain",
            ResourceId = domain.Id.ToString(),
            OldValue = AuditLogEntryFactory.Snapshot(new { status = domain.Status }),
            NewValue = AuditLogEntryFactory.Snapshot(new { status = "verification_timeout" }),
            Details = $"maxWindowHours={DnsPollingOptions.MaxPollingHours} ageHours={(now - domain.CreatedAt).TotalHours:F1}",
            Timestamp = now
        }, ct);

        _logger.LogWarning(
            "Domain {DomainId} ({Hostname}) timed out after {Hours}h — no DNS records detected",
            domain.Id, domain.Hostname, DnsPollingOptions.MaxPollingHours);

        try
        {
            await _notification.NotifyVerificationTimedOutAsync(updated, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Timeout notification failed for domain {DomainId} ({Hostname})",
                domain.Id, domain.Hostname);
        }
    }
}
