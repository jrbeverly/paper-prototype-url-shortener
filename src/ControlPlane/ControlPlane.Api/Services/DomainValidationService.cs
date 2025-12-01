using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Services;

public sealed class DomainValidationOptions
{
    public const string SectionName = "DomainValidation";

    /// <summary>Maximum milliseconds to wait for a DNS lookup before returning Timeout.</summary>
    public int LookupTimeoutMs { get; init; } = 10_000;

    /// <summary>Hours after domain creation during which missing (not wrong) records yield Pending instead of Failed.</summary>
    public int GracePeriodHours { get; init; } = 24;

    /// <summary>Seconds to retain a successful (Valid) result in the in-process cache.</summary>
    public int SuccessCacheTtlSeconds { get; init; } = 3_600;
}

/// <summary>
/// Validates domain DNS records with timeout, propagation grace period, and result caching.
/// Uses <see cref="IDnsVerificationService"/> for the raw DNS lookups so the lookup
/// implementation can be swapped independently.
/// </summary>
public sealed class DomainValidationService : IDomainValidationService
{
    private readonly IDnsVerificationService _dns;
    private readonly DomainValidationOptions _options;
    private readonly ILogger<DomainValidationService> _logger;
    private readonly ConcurrentDictionary<string, CachedValidation> _cache
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _lookupTimeout;
    private readonly TimeSpan _gracePeriod;
    private readonly TimeSpan _cacheTtl;

    public DomainValidationService(
        IDnsVerificationService dns,
        IOptions<DomainValidationOptions> options,
        ILogger<DomainValidationService> logger)
    {
        _dns = dns;
        _options = options.Value;
        _logger = logger;
        _lookupTimeout = TimeSpan.FromMilliseconds(_options.LookupTimeoutMs);
        _gracePeriod = TimeSpan.FromHours(_options.GracePeriodHours);
        _cacheTtl = TimeSpan.FromSeconds(_options.SuccessCacheTtlSeconds);
    }

    public async Task<DomainValidationResult> ValidateAsync(
        string hostname,
        string expectedTxtValue,
        string expectedCnameTarget,
        DateTime domainCreatedAt,
        CancellationToken ct = default)
    {
        if (_cache.TryGetValue(hostname, out var cached) && !cached.IsExpired)
        {
            _logger.LogDebug("Domain validation cache hit for {Hostname}", hostname);
            return cached.Result with { FromCache = true };
        }

        var dnsTask = _dns.VerifyAsync(hostname, expectedTxtValue, expectedCnameTarget);
        var completed = await Task.WhenAny(dnsTask, Task.Delay(_lookupTimeout, ct));

        // Propagate request cancellation (distinct from a lookup timeout).
        ct.ThrowIfCancellationRequested();

        if (completed != dnsTask)
        {
            _logger.LogWarning(
                "Domain DNS validation timed out after {TimeoutMs}ms for {Hostname}",
                _options.LookupTimeoutMs, hostname);

            return new DomainValidationResult
            {
                Status = DomainValidationStatus.Timeout,
                FailureReason =
                    $"DNS lookup did not complete within {_options.LookupTimeoutMs / 1000.0:G} seconds. " +
                    "Try again shortly."
            };
        }

        var dnsResult = await dnsTask;

        DomainValidationResult result;

        if (dnsResult.AllPassed)
        {
            result = new DomainValidationResult
            {
                Status = DomainValidationStatus.Valid,
                TxtVerified = true,
                CnameVerified = true
            };

            // Only cache successes — a failure might resolve after propagation.
            _cache[hostname] = new CachedValidation(result, DateTime.UtcNow.Add(_cacheTtl));
        }
        else
        {
            // Distinguish "record not yet visible" (null actual) from "wrong value" (non-null actual).
            // Wrong values indicate misconfiguration, not propagation delay, so the grace period
            // only applies when all failures are due to missing records.
            var txtWrong = !dnsResult.TxtPassed && dnsResult.ActualTxtValue is not null;
            var cnameWrong = !dnsResult.CnamePassed && dnsResult.ActualCnameValue is not null;
            var hasWrongValues = txtWrong || cnameWrong;

            var withinGracePeriod = DateTime.UtcNow - domainCreatedAt < _gracePeriod;

            var status = (!hasWrongValues && withinGracePeriod)
                ? DomainValidationStatus.Pending
                : DomainValidationStatus.Failed;

            result = new DomainValidationResult
            {
                Status = status,
                TxtVerified = dnsResult.TxtPassed,
                CnameVerified = dnsResult.CnamePassed,
                FailureReason = BuildFailureReason(dnsResult)
            };
        }

        _logger.LogInformation(
            "Domain validation for {Hostname}: {Status} (txt={TxtOk}, cname={CnameOk})",
            hostname, result.Status, result.TxtVerified, result.CnameVerified);

        return result;
    }

    public void InvalidateCache(string hostname) => _cache.TryRemove(hostname, out _);

    private static string? BuildFailureReason(DnsVerificationResult result)
    {
        if (result.AllPassed) return null;

        var parts = new List<string>(2);
        if (!result.TxtPassed)
            parts.Add(result.TxtError
                ?? $"TXT record does not contain the expected value '{result.ExpectedTxtValue}'.");
        if (!result.CnamePassed)
            parts.Add(result.CnameError
                ?? $"CNAME record does not point to the expected target '{result.ExpectedCnameValue}'.");

        return string.Join(" ", parts);
    }

    private sealed record CachedValidation(DomainValidationResult Result, DateTime ExpiresAt)
    {
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    }
}
