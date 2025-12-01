namespace ControlPlane.Api.Models.Responses;

/// <summary>
/// Lightweight dashboard status snapshot for a custom domain. Derived entirely from stored state —
/// no live DNS or HTTP probes — so it is safe to poll frequently.
/// </summary>
public sealed record DomainStatusResponse
{
    /// <summary>The unique identifier of the domain.</summary>
    public required Guid DomainId { get; init; }

    /// <summary>The registered hostname.</summary>
    public required string Hostname { get; init; }

    /// <summary>Lifecycle status: "pending_verification", "verifying", "verification_failed", "certificate_provisioning", "certificate_failed", "active", or "inactive".</summary>
    public required string Status { get; init; }

    /// <summary>Aggregate health indicator: "green" (fully operational), "yellow" (in progress or minor issue), "red" (failure or disabled).</summary>
    public required string HealthScore { get; init; }

    /// <summary>Verification state, DNS record expectations, and recent attempt history.</summary>
    public required DomainVerificationStatus Verification { get; init; }

    /// <summary>TLS certificate summary.</summary>
    public required DomainCertificateSummary Certificate { get; init; }

    /// <summary>CloudFront routing endpoint and distribution provisioning state.</summary>
    public required DomainRoutingStatus Routing { get; init; }

    /// <summary>Number of active links on this domain.</summary>
    public required int LinkCount { get; init; }

    /// <summary>Ordered list of recommended actions the customer should take to resolve any issues. Empty when the domain is fully operational.</summary>
    public required IReadOnlyList<SuggestedAction> SuggestedActions { get; init; }

    /// <summary>When this status snapshot was computed (UTC).</summary>
    public required DateTime CheckedAt { get; init; }
}

/// <summary>DNS verification state and record expectations for a domain.</summary>
public sealed record DomainVerificationStatus
{
    /// <summary>Verification lifecycle phase. Mirrors the domain status for verification-relevant states.</summary>
    public required string Status { get; init; }

    /// <summary>When the most recent DNS verification attempt was made, or null if none yet.</summary>
    public DateTime? LastAttemptAt { get; init; }

    /// <summary>Exact DNS records the customer must create at their DNS provider.</summary>
    public required DnsRecordExpectations DnsRecords { get; init; }

    /// <summary>Up to 5 most recent verification attempts, oldest first.</summary>
    public required IReadOnlyList<VerificationAttemptSummary> RecentAttempts { get; init; }
}

/// <summary>The TXT and CNAME records the customer must configure at their DNS provider.</summary>
public sealed record DnsRecordExpectations
{
    /// <summary>Hostname for the TXT ownership-verification record.</summary>
    public required string TxtName { get; init; }

    /// <summary>Exact value of the TXT record (e.g., "short-io-verify=abc123…").</summary>
    public required string TxtValue { get; init; }

    /// <summary>Hostname for the CNAME routing record.</summary>
    public required string CnameName { get; init; }

    /// <summary>Target the CNAME must point to (the CloudFront endpoint).</summary>
    public required string CnameTarget { get; init; }
}

/// <summary>Outcome of a single DNS verification attempt.</summary>
public sealed record VerificationAttemptSummary
{
    /// <summary>When the attempt was made (UTC).</summary>
    public required DateTime AttemptedAt { get; init; }

    /// <summary>Whether both TXT and CNAME checks passed.</summary>
    public required bool Passed { get; init; }

    /// <summary>Whether the TXT ownership-verification record was found and correct.</summary>
    public required bool TxtPassed { get; init; }

    /// <summary>Whether the CNAME routing record was found and correct.</summary>
    public required bool CnamePassed { get; init; }

    /// <summary>Short failure reason, or null on success.</summary>
    public string? FailureReason { get; init; }
}

/// <summary>ACM TLS certificate summary for a domain.</summary>
public sealed record DomainCertificateSummary
{
    /// <summary>Certificate status: "pending", "pending_validation", "issued", "failed", or "revoked".</summary>
    public required string Status { get; init; }

    /// <summary>The ACM certificate ARN, or null if not yet provisioned.</summary>
    public string? Arn { get; init; }

    /// <summary>When the certificate was issued, or null if not yet issued.</summary>
    public DateTime? IssuedAt { get; init; }

    /// <summary>When the certificate expires, or null if not yet issued.</summary>
    public DateTime? ExpiresAt { get; init; }
}

/// <summary>CloudFront routing endpoint and distribution provisioning state.</summary>
public sealed record DomainRoutingStatus
{
    /// <summary>The CNAME target the domain must point to (the shared CloudFront endpoint).</summary>
    public required string RoutingEndpoint { get; init; }

    /// <summary>The CloudFront SaaS Manager distribution tenant ID, or null if not yet provisioned.</summary>
    public string? DistributionTenantId { get; init; }

    /// <summary>Distribution tenant status: "InProgress", "Deployed", "Failed", or null if not provisioned.</summary>
    public string? DistributionStatus { get; init; }

    /// <summary>True when the domain is active and the distribution is deployed and serving traffic.</summary>
    public required bool IsServing { get; init; }
}

/// <summary>A recommended action the customer should take to resolve a domain configuration issue.</summary>
public sealed record SuggestedAction
{
    /// <summary>Stable machine-readable action code (e.g., "add_dns_records", "retry_verification").</summary>
    public required string ActionCode { get; init; }

    /// <summary>Short human-readable label for use in buttons or links.</summary>
    public required string Label { get; init; }

    /// <summary>Explanation of what to do and why.</summary>
    public required string Description { get; init; }

    /// <summary>Optional relative URL that the UI can use as a deep link to the relevant settings page.</summary>
    public string? ActionUrl { get; init; }
}
