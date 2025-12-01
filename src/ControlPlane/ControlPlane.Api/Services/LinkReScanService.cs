using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Services;

/// <summary>
/// Periodically re-scans destination URLs of active and quarantined links.
/// Links whose destinations have become malicious are quarantined; quarantined
/// links whose destinations are now clean are automatically un-quarantined.
/// High-traffic links are prioritized and scanned first.
/// </summary>
public sealed class LinkReScanService : BackgroundService
{
    private readonly ILinkRepository _linkRepository;
    private readonly IUrlSafetyService _urlSafetyService;
    private readonly IAuditLogService _auditLog;
    private readonly LinkReScanOptions _options;
    private readonly ILogger<LinkReScanService> _logger;

    public LinkReScanService(
        ILinkRepository linkRepository,
        IUrlSafetyService urlSafetyService,
        IAuditLogService auditLog,
        IOptions<LinkReScanOptions> options,
        ILogger<LinkReScanService> logger)
    {
        _linkRepository = linkRepository;
        _urlSafetyService = urlSafetyService;
        _auditLog = auditLog;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "LinkReScanService started — interval {IntervalHours}h, batch {BatchSize}, high-traffic threshold {Threshold}",
            _options.ReScanIntervalHours, _options.ScanBatchSize, _options.HighTrafficThreshold);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(_options.ReScanIntervalHours));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunScanCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Re-scan cycle failed with unhandled exception — next cycle will retry");
            }
        }
    }

    internal async Task RunScanCycleAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting destination re-scan cycle");

        var links = await _linkRepository.GetLinksForReScanAsync(ct);

        if (links.Count == 0)
        {
            _logger.LogDebug("No links eligible for re-scan");
            return;
        }

        var batch = links
            .OrderByDescending(l => l.ClickCount)
            .Take(_options.ScanBatchSize)
            .ToList();

        _logger.LogInformation(
            "Re-scanning {BatchCount} links (of {TotalCount} eligible), prioritized by traffic",
            batch.Count, links.Count);

        int scanned = 0, quarantined = 0, unquarantined = 0, skipped = 0;

        foreach (var link in batch)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var result = await _urlSafetyService.ScanAsync(link.DestinationUrl, ct);
                scanned++;

                switch (result.Verdict)
                {
                    case UrlSafetyVerdict.Malicious:
                        if (link.Status != "quarantined")
                        {
                            await QuarantineLinkAsync(link, result, ct);
                            quarantined++;
                        }
                        else
                        {
                            skipped++;
                        }
                        break;

                    case UrlSafetyVerdict.Suspicious:
                        if (link.Status == "active")
                        {
                            await QuarantineLinkAsync(link, result, ct);
                            quarantined++;
                        }
                        else
                        {
                            skipped++;
                        }
                        break;

                    case UrlSafetyVerdict.Safe:
                        if (link.Status == "quarantined")
                        {
                            await UnquarantineLinkAsync(link, result, ct);
                            unquarantined++;
                        }
                        else
                        {
                            skipped++;
                        }
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Re-scan failed for link {LinkId} ({DestinationUrl}) — link status unchanged",
                    link.Id, link.DestinationUrl);
            }
        }

        _logger.LogInformation(
            "Re-scan cycle complete — scanned {Scanned}, quarantined {Quarantined}, unquarantined {Unquarantined}, skipped {Skipped}",
            scanned, quarantined, unquarantined, skipped);
    }

    private async Task QuarantineLinkAsync(LinkEntity link, UrlSafetyResult result, CancellationToken ct)
    {
        await _linkRepository.UpdateAsync(link.TenantId, link.Id,
            new Models.Requests.UpdateLinkRequest { Status = "quarantined" });

        await _auditLog.LogAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            TenantId = link.TenantId,
            Action = "link.quarantined_by_rescan",
            ActorId = "rescan",
            ActorType = "system",
            ResourceType = "link",
            ResourceId = link.Id.ToString(),
            OldValue = AuditLogEntryFactory.Snapshot(new { status = link.Status }),
            NewValue = AuditLogEntryFactory.Snapshot(new { status = "quarantined" }),
            Details = $"verdict={result.Verdict} source={result.Source} reason={result.Reason} url={link.DestinationUrl}",
            Timestamp = DateTime.UtcNow
        }, ct);

        _logger.LogWarning(
            "Link {LinkId} quarantined by re-scan — {Reason} ({Source})",
            link.Id, result.Reason, result.Source);
    }

    private async Task UnquarantineLinkAsync(LinkEntity link, UrlSafetyResult result, CancellationToken ct)
    {
        await _linkRepository.UpdateAsync(link.TenantId, link.Id,
            new Models.Requests.UpdateLinkRequest { Status = "active" });

        await _auditLog.LogAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            TenantId = link.TenantId,
            Action = "link.unquarantined_by_rescan",
            ActorId = "rescan",
            ActorType = "system",
            ResourceType = "link",
            ResourceId = link.Id.ToString(),
            OldValue = AuditLogEntryFactory.Snapshot(new { status = "quarantined" }),
            NewValue = AuditLogEntryFactory.Snapshot(new { status = "active" }),
            Details = $"verdict={result.Verdict} source={result.Source} reason={result.Reason} url={link.DestinationUrl}",
            Timestamp = DateTime.UtcNow
        }, ct);

        _logger.LogInformation(
            "Link {LinkId} auto-unquarantined by re-scan — destination now clean",
            link.Id);
    }
}
