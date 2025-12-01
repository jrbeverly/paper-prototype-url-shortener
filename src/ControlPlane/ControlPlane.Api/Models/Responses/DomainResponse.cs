namespace ControlPlane.Api.Models.Responses;

public sealed record CreateDomainResponse
{
    /// <summary>The unique identifier of the domain.</summary>
    public required Guid Id { get; init; }

    /// <summary>The registered hostname.</summary>
    public required string Hostname { get; init; }

    /// <summary>The domain status: "pending_verification", "verifying", "verification_failed", or "active".</summary>
    public required string Status { get; init; }

    /// <summary>DNS records the customer must create to verify ownership.</summary>
    public required DnsVerificationInstructions VerificationInstructions { get; init; }

    /// <summary>When the domain was registered.</summary>
    public required DateTime CreatedAt { get; init; }
}

public sealed record DnsVerificationInstructions
{
    /// <summary>The DNS TXT record name to create.</summary>
    public required string TxtName { get; init; }

    /// <summary>The DNS TXT record value to create.</summary>
    public required string TxtValue { get; init; }

    /// <summary>The DNS CNAME record name to create.</summary>
    public required string CnameName { get; init; }

    /// <summary>The DNS CNAME record value to point to.</summary>
    public required string CnameValue { get; init; }
}

public sealed record DomainDetailResponse
{
    /// <summary>The unique identifier of the domain.</summary>
    public required Guid Id { get; init; }

    /// <summary>The tenant that owns this domain.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>The registered hostname.</summary>
    public required string Hostname { get; init; }

    /// <summary>The domain status: "pending_verification", "verifying", "verification_failed", or "active".</summary>
    public required string Status { get; init; }

    /// <summary>The TLS certificate status: "pending", "issued", or "failed".</summary>
    public required string CertificateStatus { get; init; }

    /// <summary>The ACM certificate ARN, or null if not yet provisioned.</summary>
    public string? CertificateArn { get; init; }

    /// <summary>Number of active links on this domain.</summary>
    public required int LinkCount { get; init; }

    /// <summary>Domain settings for default behavior and branding.</summary>
    public required DomainSettings Settings { get; init; }

    /// <summary>When the domain was registered.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>When the domain was last updated.</summary>
    public DateTime? UpdatedAt { get; init; }

    /// <summary>When the domain was soft-deleted, or null if active.</summary>
    public DateTime? DeletedAt { get; init; }

    /// <summary>When the domain was deactivated, or null if not currently inactive. Used to enforce the 30-day reactivation grace period.</summary>
    public DateTime? DeactivatedAt { get; init; }

    /// <summary>The CloudFront SaaS Manager distribution tenant ID, or null if not yet provisioned.</summary>
    public string? DistributionTenantId { get; init; }

    /// <summary>The CloudFront distribution tenant provisioning status: "InProgress", "Deployed", "Failed", or null.</summary>
    public string? DistributionTenantStatus { get; init; }
}

public sealed record DomainSettings
{
    /// <summary>The default URL to redirect to when a visitor hits the domain root.</summary>
    public string? DefaultRedirectUrl { get; init; }

    /// <summary>Custom branding for error pages shown to visitors.</summary>
    public string? ErrorPageBranding { get; init; }

    /// <summary>Behavior when no matching link is found: "404", "redirect", or "passthrough".</summary>
    public string NotFoundBehavior { get; init; } = "404";
}

public sealed record DomainListItemResponse
{
    /// <summary>The unique identifier of the domain.</summary>
    public required Guid Id { get; init; }

    /// <summary>The registered hostname.</summary>
    public required string Hostname { get; init; }

    /// <summary>The domain status: "pending_verification", "verifying", "verification_failed", or "active".</summary>
    public required string Status { get; init; }

    /// <summary>The TLS certificate status: "pending", "issued", or "failed".</summary>
    public required string CertificateStatus { get; init; }

    /// <summary>The ACM certificate ARN, or null if not yet provisioned.</summary>
    public string? CertificateArn { get; init; }

    /// <summary>Number of active links on this domain.</summary>
    public required int LinkCount { get; init; }

    /// <summary>When the domain was registered.</summary>
    public required DateTime CreatedAt { get; init; }
}

public sealed record ListDomainsResponse
{
    /// <summary>The list of domain items for the current page.</summary>
    public required List<DomainListItemResponse> Items { get; init; }

    /// <summary>The current page number (1-based).</summary>
    public required int Page { get; init; }

    /// <summary>The number of items per page.</summary>
    public required int PageSize { get; init; }

    /// <summary>Total number of domains matching the query (across all pages).</summary>
    public required int TotalCount { get; init; }
}

public sealed record CertificateProvisionResponse
{
    /// <summary>The unique identifier of the domain.</summary>
    public required Guid DomainId { get; init; }

    /// <summary>The ACM certificate ARN.</summary>
    public required string CertificateArn { get; init; }

    /// <summary>The certificate status (typically "pending_validation").</summary>
    public required string Status { get; init; }

    /// <summary>DNS CNAME records the customer must create to validate domain ownership with ACM.</summary>
    public required List<CertificateRecord> ValidationRecords { get; init; }
}

public sealed record CertificateRecord
{
    /// <summary>The DNS CNAME record name (fully qualified).</summary>
    public required string Name { get; init; }

    /// <summary>The DNS CNAME record value.</summary>
    public required string Value { get; init; }
}

public sealed record CertificateStatusResponse
{
    /// <summary>The unique identifier of the domain.</summary>
    public required Guid DomainId { get; init; }

    /// <summary>The ACM certificate ARN.</summary>
    public required string CertificateArn { get; init; }

    /// <summary>The certificate status: "pending_validation", "issued", "failed", or "revoked".</summary>
    public required string Status { get; init; }

    /// <summary>When the certificate was issued, or null if not yet issued.</summary>
    public DateTime? IssuedAt { get; init; }

    /// <summary>When the certificate expires, or null if not yet issued.</summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>Whether ACM auto-renewal is enabled for this certificate.</summary>
    public required bool IsRenewalEligible { get; init; }

    /// <summary>If the certificate failed validation, the failure reason.</summary>
    public string? FailureReason { get; init; }
}
