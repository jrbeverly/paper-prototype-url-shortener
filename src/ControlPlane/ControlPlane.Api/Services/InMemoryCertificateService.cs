using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Services;

public sealed class InMemoryCertificateService : ICertificateService
{
    private readonly ConcurrentDictionary<string, InMemoryCertificate> _certificates = new();
    private readonly CertificateOptions _options;
    private readonly ILogger<InMemoryCertificateService> _logger;

    private int _sequence;

    public InMemoryCertificateService(IOptions<CertificateOptions> options, ILogger<InMemoryCertificateService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<CertificateRequestResult> RequestCertificateAsync(Guid tenantId, Guid domainId, string hostname)
    {
        var pendingCount = _certificates.Values.Count(c =>
            c.TenantId == tenantId && c.Status == "pending_validation");

        if (pendingCount >= _options.MaxPendingCertificates)
            throw new InvalidOperationException(
                $"Tenant {tenantId} has reached the maximum pending certificates limit ({_options.MaxPendingCertificates}).");

        var requestCount = _certificates.Values.Count(c =>
            c.TenantId == tenantId && c.RequestedAt > DateTime.UtcNow.AddMinutes(-1));

        if (requestCount >= _options.MaxRequestsPerMinute)
            throw new InvalidOperationException(
                $"Certificate request rate limit exceeded for tenant {tenantId}.");

        var seq = Interlocked.Increment(ref _sequence);
        var arn = $"arn:aws:acm:{_options.Region}:000000000000:certificate/mock-{seq:x8}";
        var validationRecordName = $"_acm.{hostname}";
        var validationRecordValue = $"_{seq:x16}.acm-validations.aws";

        var cert = new InMemoryCertificate
        {
            Arn = arn,
            TenantId = tenantId,
            DomainId = domainId,
            Hostname = hostname,
            Status = "pending_validation",
            SubjectAlternativeNames = [hostname],
            ValidationRecordName = validationRecordName,
            ValidationRecordValue = validationRecordValue,
            RequestedAt = DateTime.UtcNow
        };

        _certificates[arn] = cert;

        _logger.LogInformation(
            "Certificate requested: arn={Arn} hostname={Hostname} tenantId={TenantId}",
            arn, hostname, tenantId);

        return Task.FromResult(new CertificateRequestResult
        {
            CertificateArn = arn,
            Status = "pending_validation",
            ValidationRecords =
            [
                new CertificateValidationRecord
                {
                    Name = validationRecordName,
                    Value = validationRecordValue
                }
            ]
        });
    }

    public Task<CertificateStatusResult> GetCertificateStatusAsync(string certificateArn)
    {
        if (!_certificates.TryGetValue(certificateArn, out var cert))
        {
            _logger.LogWarning("Certificate not found: {Arn}", certificateArn);
            return Task.FromResult(new CertificateStatusResult
            {
                CertificateArn = certificateArn,
                Status = "failed",
                SubjectAlternativeNames = [],
                IssuedAt = null,
                ExpiresAt = null,
                FailureReason = "Certificate not found in ACM",
                IsRenewalEligible = false
            });
        }

        if (cert.Status == "pending_validation")
        {
            var elapsed = DateTime.UtcNow - cert.RequestedAt;
            if (elapsed > TimeSpan.FromSeconds(2))
            {
                cert.Status = "issued";
                cert.IssuedAt = DateTime.UtcNow;
                cert.ExpiresAt = DateTime.UtcNow.AddMonths(13);
                cert.IsRenewalEligible = true;

                _logger.LogInformation(
                    "Certificate auto-issued (in-memory): arn={Arn} hostname={Hostname}",
                    certificateArn, cert.Hostname);
            }
        }

        return Task.FromResult(new CertificateStatusResult
        {
            CertificateArn = cert.Arn,
            Status = cert.Status,
            SubjectAlternativeNames = cert.SubjectAlternativeNames,
            IssuedAt = cert.IssuedAt,
            ExpiresAt = cert.ExpiresAt,
            FailureReason = cert.FailureReason,
            IsRenewalEligible = cert.IsRenewalEligible
        });
    }

    public Task<bool> DeleteCertificateAsync(string certificateArn)
    {
        if (_certificates.TryRemove(certificateArn, out var cert))
        {
            _logger.LogInformation(
                "Certificate deleted: arn={Arn} hostname={Hostname}",
                certificateArn, cert.Hostname);
            return Task.FromResult(true);
        }

        _logger.LogWarning("Certificate not found for deletion: {Arn}", certificateArn);
        return Task.FromResult(false);
    }

    private sealed class InMemoryCertificate
    {
        public required string Arn { get; init; }
        public required Guid TenantId { get; init; }
        public required Guid DomainId { get; init; }
        public required string Hostname { get; init; }
        public string Status { get; set; } = "pending_validation";
        public IReadOnlyList<string> SubjectAlternativeNames { get; set; } = [];
        public string? ValidationRecordName { get; init; }
        public string? ValidationRecordValue { get; init; }
        public DateTime RequestedAt { get; init; }
        public DateTime? IssuedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? FailureReason { get; set; }
        public bool IsRenewalEligible { get; set; }
    }
}
