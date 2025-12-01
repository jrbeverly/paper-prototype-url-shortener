namespace ControlPlane.Api.Services;

public sealed record DistributionOptions
{
    public const string SectionName = "DistributionOptions";

    /// <summary>CloudFront SaaS Manager multi-tenant distribution ID (e.g., EDVD632BHDS5).</summary>
    public string MultiTenantDistributionId { get; init; } = "";

    /// <summary>CloudFront SaaS Manager connection group ID (routing endpoint for CNAME targets).</summary>
    public string ConnectionGroupId { get; init; } = "";

    /// <summary>Maximum distribution tenants allowed before creation is blocked. Default: 10,000 (account default quota).</summary>
    public int TenantQuotaLimit { get; init; } = 10_000;

    /// <summary>Tenant count at which a warning is logged. Default: 8,000 (80% of default quota).</summary>
    public int QuotaWarningThreshold { get; init; } = 8_000;
}
