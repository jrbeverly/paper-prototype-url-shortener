using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Services;

public sealed class InMemoryDistributionService : IDistributionService
{
    private readonly ConcurrentDictionary<string, InMemoryDistributionTenant> _tenants = new();
    private readonly DistributionOptions _options;
    private readonly ILogger<InMemoryDistributionService> _logger;

    private int _sequence;

    public InMemoryDistributionService(
        IOptions<DistributionOptions> options,
        ILogger<InMemoryDistributionService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<QuotaCheckResult> CheckQuotaAsync()
    {
        var count = _tenants.Count;

        return Task.FromResult(new QuotaCheckResult
        {
            IsWithinQuota = count < _options.TenantQuotaLimit,
            CurrentCount = count,
            QuotaLimit = _options.TenantQuotaLimit,
            IsNearingLimit = count >= _options.QuotaWarningThreshold
        });
    }

    public Task<DistributionTenantResult> CreateDistributionTenantAsync(
        Guid tenantId, Guid domainId, string hostname, string? certificateArn)
    {
        if (_tenants.Count >= _options.TenantQuotaLimit)
            throw new InvalidOperationException(
                "Distribution tenant quota exceeded. Please contact support to increase the limit.");

        var seq = Interlocked.Increment(ref _sequence);
        var id = $"dt_{seq:x16}mock";
        var etag = $"E{seq:X16}";

        var tenant = new InMemoryDistributionTenant
        {
            Id = id,
            TenantId = tenantId,
            DomainId = domainId,
            Hostname = hostname,
            CertificateArn = certificateArn,
            Status = "InProgress",
            ETag = etag,
            CreatedAt = DateTime.UtcNow
        };

        _tenants[id] = tenant;

        _logger.LogInformation(
            "Distribution tenant created (in-memory): id={Id} hostname={Hostname} tenantId={TenantId}",
            id, hostname, tenantId);

        return Task.FromResult(new DistributionTenantResult
        {
            DistributionTenantId = id,
            Status = tenant.Status,
            ETag = etag
        });
    }

    public Task<DistributionTenantResult> UpdateDistributionTenantAsync(
        string distributionTenantId, string etag, string hostname, string? certificateArn)
    {
        if (!_tenants.TryGetValue(distributionTenantId, out var tenant))
            throw new InvalidOperationException(
                $"Distribution tenant '{distributionTenantId}' was not found.");

        var seq = Interlocked.Increment(ref _sequence);
        var newEtag = $"E{seq:X16}";

        tenant.Hostname = hostname;
        tenant.CertificateArn = certificateArn;
        tenant.ETag = newEtag;

        _logger.LogInformation(
            "Distribution tenant updated (in-memory): id={Id} hostname={Hostname}",
            distributionTenantId, hostname);

        return Task.FromResult(new DistributionTenantResult
        {
            DistributionTenantId = distributionTenantId,
            Status = tenant.Status,
            ETag = newEtag
        });
    }

    public Task<bool> DeleteDistributionTenantAsync(string distributionTenantId)
    {
        if (_tenants.TryRemove(distributionTenantId, out var tenant))
        {
            _logger.LogInformation(
                "Distribution tenant deleted (in-memory): id={Id} hostname={Hostname}",
                distributionTenantId, tenant.Hostname);
            return Task.FromResult(true);
        }

        _logger.LogWarning(
            "Distribution tenant not found for deletion (in-memory): {Id}", distributionTenantId);
        return Task.FromResult(false);
    }

    public Task<DistributionTenantStatusResult> GetDistributionTenantStatusAsync(string distributionTenantId)
    {
        if (!_tenants.TryGetValue(distributionTenantId, out var tenant))
        {
            return Task.FromResult(new DistributionTenantStatusResult
            {
                DistributionTenantId = distributionTenantId,
                Status = "NotFound",
                ETag = ""
            });
        }

        // Auto-transition InProgress → Deployed after 2 seconds (mirrors InMemoryCertificateService).
        if (tenant.Status == "InProgress" && DateTime.UtcNow - tenant.CreatedAt > TimeSpan.FromSeconds(2))
        {
            tenant.Status = "Deployed";
            _logger.LogInformation(
                "Distribution tenant auto-deployed (in-memory): id={Id} hostname={Hostname}",
                distributionTenantId, tenant.Hostname);
        }

        return Task.FromResult(new DistributionTenantStatusResult
        {
            DistributionTenantId = distributionTenantId,
            Status = tenant.Status,
            ETag = tenant.ETag
        });
    }

    private sealed class InMemoryDistributionTenant
    {
        public required string Id { get; init; }
        public required Guid TenantId { get; init; }
        public required Guid DomainId { get; init; }
        public string Hostname { get; set; } = "";
        public string? CertificateArn { get; set; }
        public string Status { get; set; } = "InProgress";
        public string ETag { get; set; } = "";
        public DateTime CreatedAt { get; init; }
    }
}
