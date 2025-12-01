using Amazon.CloudFront;
using Amazon.CloudFront.Model;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Services;

public sealed class DistributionService : IDistributionService
{
    private readonly IAmazonCloudFront _cloudFront;
    private readonly DistributionOptions _options;
    private readonly ILogger<DistributionService> _logger;

    public DistributionService(
        IAmazonCloudFront cloudFront,
        IOptions<DistributionOptions> options,
        ILogger<DistributionService> logger)
    {
        _cloudFront = cloudFront;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<QuotaCheckResult> CheckQuotaAsync()
    {
        // List up to TenantQuotaLimit tenants for this distribution to determine current usage.
        // Paging beyond QuotaLimit is not needed — if MaxItems are returned we are at or over limit.
        var response = await _cloudFront.ListDistributionTenantsAsync(
            new ListDistributionTenantsRequest
            {
                AssociationFilter = new DistributionTenantAssociationFilter
                {
                    DistributionId = _options.MultiTenantDistributionId
                },
                MaxItems = _options.TenantQuotaLimit
            });

        var count = response.DistributionTenantList.Count;

        if (count >= _options.QuotaWarningThreshold)
        {
            _logger.LogWarning(
                "Distribution tenant count approaching quota: count={Count} warningThreshold={Threshold} limit={Limit}",
                count, _options.QuotaWarningThreshold, _options.TenantQuotaLimit);
        }

        return new QuotaCheckResult
        {
            IsWithinQuota = count < _options.TenantQuotaLimit,
            CurrentCount = count,
            QuotaLimit = _options.TenantQuotaLimit,
            IsNearingLimit = count >= _options.QuotaWarningThreshold
        };
    }

    public async Task<DistributionTenantResult> CreateDistributionTenantAsync(
        Guid tenantId, Guid domainId, string hostname, string? certificateArn)
    {
        _logger.LogInformation(
            "Creating distribution tenant: hostname={Hostname} tenantId={TenantId} domainId={DomainId}",
            hostname, tenantId, domainId);

        var request = new CreateDistributionTenantRequest
        {
            DistributionId = _options.MultiTenantDistributionId,
            ConnectionGroupId = _options.ConnectionGroupId,
            Name = $"tenant-{tenantId:N}-domain-{domainId:N}",
            Enabled = false, // enabled once certificate is deployed
            Domains = [new DomainItem { Domain = hostname }],
            Tags = new Tags
            {
                Items =
                [
                    new Tag { Key = "TenantId", Value = tenantId.ToString() },
                    new Tag { Key = "DomainId", Value = domainId.ToString() },
                    new Tag { Key = "Service", Value = "url-shortener" }
                ]
            }
        };

        if (!string.IsNullOrEmpty(certificateArn))
        {
            request.Customizations = new Customizations
            {
                Certificate = new Certificate { Arn = certificateArn }
            };
        }
        else
        {
            // CloudFront-managed certificate: validates automatically when the customer's
            // CNAME points to the connection group routing endpoint.
            request.ManagedCertificateRequest = new ManagedCertificateRequest
            {
                PrimaryDomainName = hostname,
                ValidationTokenHost = ValidationTokenHost.Cloudfront,
                CertificateTransparencyLoggingPreference =
                    CertificateTransparencyLoggingPreference.Enabled
            };
        }

        CreateDistributionTenantResponse response;
        try
        {
            response = await _cloudFront.CreateDistributionTenantAsync(request);
        }
        catch (EntityLimitExceededException ex)
        {
            _logger.LogError(ex,
                "Distribution tenant quota exceeded for hostname {Hostname}", hostname);
            throw new InvalidOperationException(
                "Distribution tenant quota exceeded. Please contact support to increase the limit.", ex);
        }
        catch (EntityAlreadyExistsException ex)
        {
            _logger.LogError(ex,
                "Distribution tenant already exists for hostname {Hostname}", hostname);
            throw new InvalidOperationException(
                $"A distribution tenant already exists for hostname '{hostname}'.", ex);
        }

        var tenant = response.DistributionTenant;

        _logger.LogInformation(
            "Distribution tenant created: id={Id} hostname={Hostname} status={Status}",
            tenant.Id, hostname, tenant.Status);

        return new DistributionTenantResult
        {
            DistributionTenantId = tenant.Id,
            Status = tenant.Status,
            ETag = response.ETag
        };
    }

    public async Task<DistributionTenantResult> UpdateDistributionTenantAsync(
        string distributionTenantId, string etag, string hostname, string? certificateArn)
    {
        _logger.LogInformation(
            "Updating distribution tenant: id={Id} hostname={Hostname}",
            distributionTenantId, hostname);

        var request = new UpdateDistributionTenantRequest
        {
            Id = distributionTenantId,
            DistributionId = _options.MultiTenantDistributionId,
            ConnectionGroupId = _options.ConnectionGroupId,
            IfMatch = etag,
            Domains = [new DomainItem { Domain = hostname }]
        };

        if (!string.IsNullOrEmpty(certificateArn))
        {
            request.Customizations = new Customizations
            {
                Certificate = new Certificate { Arn = certificateArn }
            };
        }
        else
        {
            request.ManagedCertificateRequest = new ManagedCertificateRequest
            {
                PrimaryDomainName = hostname,
                ValidationTokenHost = ValidationTokenHost.Cloudfront,
                CertificateTransparencyLoggingPreference =
                    CertificateTransparencyLoggingPreference.Enabled
            };
        }

        UpdateDistributionTenantResponse response;
        try
        {
            response = await _cloudFront.UpdateDistributionTenantAsync(request);
        }
        catch (EntityNotFoundException ex)
        {
            _logger.LogWarning(ex,
                "Distribution tenant not found for update: {Id}", distributionTenantId);
            throw new InvalidOperationException(
                $"Distribution tenant '{distributionTenantId}' was not found.", ex);
        }

        var tenant = response.DistributionTenant;

        _logger.LogInformation(
            "Distribution tenant updated: id={Id} hostname={Hostname} status={Status}",
            tenant.Id, hostname, tenant.Status);

        return new DistributionTenantResult
        {
            DistributionTenantId = tenant.Id,
            Status = tenant.Status,
            ETag = response.ETag
        };
    }

    public async Task<bool> DeleteDistributionTenantAsync(string distributionTenantId)
    {
        _logger.LogInformation("Deleting distribution tenant: {Id}", distributionTenantId);

        try
        {
            var getResponse = await _cloudFront.GetDistributionTenantAsync(
                new GetDistributionTenantRequest { Identifier = distributionTenantId });

            await _cloudFront.DeleteDistributionTenantAsync(new DeleteDistributionTenantRequest
            {
                Id = distributionTenantId,
                IfMatch = getResponse.ETag
            });

            _logger.LogInformation("Distribution tenant deleted: {Id}", distributionTenantId);
            return true;
        }
        catch (EntityNotFoundException)
        {
            _logger.LogWarning("Distribution tenant not found for deletion: {Id}", distributionTenantId);
            return false;
        }
    }

    public async Task<DistributionTenantStatusResult> GetDistributionTenantStatusAsync(
        string distributionTenantId)
    {
        try
        {
            var response = await _cloudFront.GetDistributionTenantAsync(
                new GetDistributionTenantRequest { Identifier = distributionTenantId });

            return new DistributionTenantStatusResult
            {
                DistributionTenantId = distributionTenantId,
                Status = response.DistributionTenant.Status,
                ETag = response.ETag
            };
        }
        catch (EntityNotFoundException)
        {
            _logger.LogWarning("Distribution tenant not found: {Id}", distributionTenantId);
            return new DistributionTenantStatusResult
            {
                DistributionTenantId = distributionTenantId,
                Status = "NotFound",
                ETag = ""
            };
        }
    }
}
