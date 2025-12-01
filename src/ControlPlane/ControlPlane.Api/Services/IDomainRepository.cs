namespace ControlPlane.Api.Services;

public interface IDomainRepository
{
    Task<DomainEntity> CreateAsync(DomainEntity entity);
    Task<DomainEntity?> GetByHostnameAsync(Guid tenantId, string hostname);
    Task<DomainEntity?> GetByIdAsync(Guid tenantId, Guid domainId);
    Task<List<DomainEntity>> GetByTenantAsync(Guid tenantId);
    Task<int> GetCountByTenantAsync(Guid tenantId);
    Task<List<DomainEntity>> GetPendingVerificationAsync();
    Task<(List<DomainEntity> Items, int TotalCount)> ListAsync(Guid tenantId, string? statusFilter, int page, int pageSize);
    Task<DomainEntity?> UpdateAsync(DomainEntity entity);
    Task<DomainEntity?> SoftDeleteAsync(Guid tenantId, Guid domainId);
}

/// <summary>Records the outcome of a single DNS verification attempt.</summary>
public sealed record VerificationAttempt
{
    public required DateTime AttemptedAt { get; init; }
    public required bool TxtPassed { get; init; }
    public required bool CnamePassed { get; init; }
    public string? FailureReason { get; init; }
}

public sealed record DomainEntity
{
    public required Guid Id { get; init; }
    public required Guid TenantId { get; init; }
    public required string Hostname { get; init; }
    public required string Status { get; init; }
    public required string VerificationCode { get; init; }
    public required string CnameTarget { get; init; }
    public required DateTime CreatedAt { get; init; }
    public string? DefaultRedirectUrl { get; init; }
    public string? ErrorPageBranding { get; init; }
    public string NotFoundBehavior { get; init; } = "404";
    public string CertificateStatus { get; init; } = "pending";
    public string? CertificateArn { get; init; }
    public bool CertificateExplicitlyProvisioned { get; init; }
    public int LinkCount { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public DateTime? LastVerifiedAt { get; init; }
    public DateTime? DeletedAt { get; init; }
    public DateTime? DeactivatedAt { get; init; }

    /// <summary>Rolling history of the last 5 DNS verification attempts (oldest first).</summary>
    public IReadOnlyList<VerificationAttempt> VerificationAttempts { get; init; } = [];

    // CloudFront SaaS Manager distribution tenant fields
    public string? DistributionTenantId { get; init; }
    public string? DistributionTenantStatus { get; init; }
    public string? DistributionTenantETag { get; init; }
}
