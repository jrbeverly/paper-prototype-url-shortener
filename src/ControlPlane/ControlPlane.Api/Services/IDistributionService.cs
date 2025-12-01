namespace ControlPlane.Api.Services;

public interface IDistributionService
{
    /// <summary>
    /// Checks whether the account has capacity to create new distribution tenants.
    /// Should be called before <see cref="CreateDistributionTenantAsync"/> to catch quota
    /// exhaustion before attempting the creation and receiving a hard API error.
    /// </summary>
    Task<QuotaCheckResult> CheckQuotaAsync();

    /// <summary>
    /// Creates a CloudFront SaaS Manager distribution tenant for the specified domain.
    /// If <paramref name="certificateArn"/> is provided the ACM certificate is attached directly.
    /// Otherwise a CloudFront-managed certificate is requested using <c>ValidationTokenHost: cloudfront</c>,
    /// which validates automatically once the customer's CNAME points to the connection group routing endpoint.
    /// The tenant is created disabled; it is enabled once the certificate is deployed.
    /// </summary>
    Task<DistributionTenantResult> CreateDistributionTenantAsync(
        Guid tenantId, Guid domainId, string hostname, string? certificateArn);

    /// <summary>
    /// Updates an existing distribution tenant. Requires the current ETag for optimistic concurrency.
    /// Called when domain certificate or settings change.
    /// </summary>
    Task<DistributionTenantResult> UpdateDistributionTenantAsync(
        string distributionTenantId, string etag, string hostname, string? certificateArn);

    /// <summary>
    /// Deletes a distribution tenant. Called when a domain is removed.
    /// Returns <c>false</c> if the tenant does not exist (already deleted or never created).
    /// </summary>
    Task<bool> DeleteDistributionTenantAsync(string distributionTenantId);

    /// <summary>
    /// Returns the current provisioning status of a distribution tenant.
    /// Statuses: <c>InProgress</c>, <c>Deployed</c>, <c>Failed</c>.
    /// </summary>
    Task<DistributionTenantStatusResult> GetDistributionTenantStatusAsync(string distributionTenantId);
}

public sealed record QuotaCheckResult
{
    public required bool IsWithinQuota { get; init; }
    public required int CurrentCount { get; init; }
    public required int QuotaLimit { get; init; }
    public required bool IsNearingLimit { get; init; }
}

public sealed record DistributionTenantResult
{
    public required string DistributionTenantId { get; init; }
    public required string Status { get; init; }
    public required string ETag { get; init; }
}

public sealed record DistributionTenantStatusResult
{
    public required string DistributionTenantId { get; init; }
    public required string Status { get; init; }
    public required string ETag { get; init; }
    public bool IsDeployed => Status == "Deployed";
    public bool IsFailed => Status == "Failed";
}
