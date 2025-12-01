using Amazon.CertificateManager;
using Amazon.CertificateManager.Model;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Services;

public sealed class CertificateService : ICertificateService
{
    private readonly IAmazonCertificateManager _acm;
    private readonly CertificateOptions _options;
    private readonly ILogger<CertificateService> _logger;

    public CertificateService(
        IAmazonCertificateManager acm,
        IOptions<CertificateOptions> options,
        ILogger<CertificateService> logger)
    {
        _acm = acm;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CertificateRequestResult> RequestCertificateAsync(
        Guid tenantId, Guid domainId, string hostname)
    {
        var listResponse = await _acm.ListCertificatesAsync(new ListCertificatesRequest
        {
            CertificateStatuses = [CertificateStatus.PENDING_VALIDATION],
            MaxItems = _options.MaxPendingCertificates * 10
        });

        var pendingCount = listResponse.CertificateSummaryList.Count;
        if (pendingCount >= _options.MaxPendingCertificates * 10)
        {
            _logger.LogWarning(
                "ACM certificate limit nearly reached: pending={PendingCount} max={MaxPending}",
                pendingCount, _options.MaxPendingCertificates * 10);
        }

        _logger.LogInformation(
            "Requesting ACM certificate: hostname={Hostname} tenantId={TenantId}",
            hostname, tenantId);

        var request = new RequestCertificateRequest
        {
            DomainName = hostname,
            ValidationMethod = ValidationMethod.DNS,
            IdempotencyToken = $"{tenantId}-{domainId}-{hostname}"
        };

        RequestCertificateResponse response;
        try
        {
            response = await _acm.RequestCertificateAsync(request);
        }
        catch (LimitExceededException ex)
        {
            _logger.LogError(ex, "ACM limit exceeded requesting certificate for {Hostname}", hostname);
            throw new InvalidOperationException(
                "Certificate request limit exceeded. Please try again later or contact support.", ex);
        }
        catch (TooManyTagsException ex)
        {
            _logger.LogError(ex, "ACM tagging error for {Hostname}", hostname);
            throw new InvalidOperationException(
                "Certificate request failed due to tag limits.", ex);
        }

        _logger.LogInformation(
            "ACM certificate requested: arn={Arn} hostname={Hostname}",
            response.CertificateArn, hostname);

        await Task.Delay(TimeSpan.FromMilliseconds(500));

        var describeResponse = await _acm.DescribeCertificateAsync(new DescribeCertificateRequest
        {
            CertificateArn = response.CertificateArn
        });

        var validationRecords = new List<CertificateValidationRecord>();
        foreach (var opt in describeResponse.Certificate.DomainValidationOptions)
        {
            if (opt.ResourceRecord is not null)
            {
                validationRecords.Add(new CertificateValidationRecord
                {
                    Name = opt.ResourceRecord.Name,
                    Value = opt.ResourceRecord.Value
                });
            }
        }

        return new CertificateRequestResult
        {
            CertificateArn = response.CertificateArn,
            Status = "pending_validation",
            ValidationRecords = validationRecords
        };
    }

    public async Task<CertificateStatusResult> GetCertificateStatusAsync(string certificateArn)
    {
        try
        {
            var response = await _acm.DescribeCertificateAsync(new DescribeCertificateRequest
            {
                CertificateArn = certificateArn
            });

            var cert = response.Certificate;

            _logger.LogDebug(
                "Certificate status: arn={Arn} status={Status} renewalEligible={RenewalEligible}",
                certificateArn, cert.Status.Value, cert.RenewalEligibility.Value);

            return new CertificateStatusResult
            {
                CertificateArn = certificateArn,
                Status = MapStatus(cert.Status.Value),
                SubjectAlternativeNames = cert.SubjectAlternativeNames ?? [],
                IssuedAt = cert.IssuedAt,
                ExpiresAt = cert.NotAfter,
                FailureReason = cert.FailureReason,
                IsRenewalEligible = cert.RenewalEligibility?.Value == RenewalEligibility.ELIGIBLE
            };
        }
        catch (ResourceNotFoundException)
        {
            _logger.LogWarning("Certificate not found in ACM: {Arn}", certificateArn);
            return new CertificateStatusResult
            {
                CertificateArn = certificateArn,
                Status = "failed",
                SubjectAlternativeNames = [],
                IssuedAt = null,
                ExpiresAt = null,
                FailureReason = "Certificate not found in ACM",
                IsRenewalEligible = false
            };
        }
    }

    public async Task<bool> DeleteCertificateAsync(string certificateArn)
    {
        try
        {
            _logger.LogInformation("Deleting ACM certificate: {Arn}", certificateArn);

            await _acm.DeleteCertificateAsync(new DeleteCertificateRequest
            {
                CertificateArn = certificateArn
            });

            _logger.LogInformation("ACM certificate deleted: {Arn}", certificateArn);
            return true;
        }
        catch (ResourceNotFoundException)
        {
            _logger.LogWarning("Certificate not found for deletion: {Arn}", certificateArn);
            return false;
        }
        catch (ResourceInUseException ex)
        {
            _logger.LogError(ex, "Cannot delete certificate still in use: {Arn}", certificateArn);
            return false;
        }
    }

    private static string MapStatus(string acmStatus) => acmStatus switch
    {
        "PENDING_VALIDATION" => "pending_validation",
        "ISSUED" => "issued",
        "INACTIVE" => "inactive",
        "EXPIRED" => "expired",
        "FAILED" => "failed",
        "REVOKED" => "revoked",
        _ => acmStatus.ToLowerInvariant()
    };
}
