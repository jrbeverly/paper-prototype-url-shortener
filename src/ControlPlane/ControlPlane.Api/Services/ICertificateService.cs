namespace ControlPlane.Api.Services;

public interface ICertificateService
{
    /// <summary>
    /// Requests an ACM certificate for a domain using DNS validation.
    /// Returns the certificate ARN and the DNS CNAME records the customer must create.
    /// </summary>
    Task<CertificateRequestResult> RequestCertificateAsync(Guid tenantId, Guid domainId, string hostname);

    /// <summary>
    /// Gets the current status of a certificate in ACM.
    /// </summary>
    Task<CertificateStatusResult> GetCertificateStatusAsync(string certificateArn);

    /// <summary>
    /// Deletes a certificate from ACM.
    /// </summary>
    Task<bool> DeleteCertificateAsync(string certificateArn);
}

public sealed record CertificateRequestResult
{
    public required string CertificateArn { get; init; }
    public required string Status { get; init; }
    public required List<CertificateValidationRecord> ValidationRecords { get; init; }
}

public sealed record CertificateValidationRecord
{
    public required string Name { get; init; }
    public required string Value { get; init; }
}

public sealed record CertificateStatusResult
{
    public required string CertificateArn { get; init; }
    public required string Status { get; init; }
    public required IReadOnlyList<string> SubjectAlternativeNames { get; init; }
    public DateTime? IssuedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string? FailureReason { get; init; }
    public bool IsRenewalEligible { get; init; }
}
