namespace ControlPlane.Api.Services;

public enum DomainValidationStatus
{
    /// <summary>All DNS records are present and correct.</summary>
    Valid,

    /// <summary>Records are missing but the domain is within the propagation grace period.</summary>
    Pending,

    /// <summary>Records are missing after the grace period, or record values are wrong.</summary>
    Failed,

    /// <summary>DNS lookup did not complete within the configured timeout.</summary>
    Timeout
}

public sealed record DomainValidationResult
{
    public required DomainValidationStatus Status { get; init; }
    public bool TxtVerified { get; init; }
    public bool CnameVerified { get; init; }

    /// <summary>Human-readable explanation when Status is not Valid.</summary>
    public string? FailureReason { get; init; }

    /// <summary>True when the result came from the in-process cache.</summary>
    public bool FromCache { get; init; }
}

public interface IDomainValidationService
{
    /// <summary>
    /// Validates that <paramref name="hostname"/> has the expected TXT ownership record and CNAME
    /// routing record. Applies a DNS propagation grace period, lookup timeout, and caches
    /// successful results.
    /// </summary>
    Task<DomainValidationResult> ValidateAsync(
        string hostname,
        string expectedTxtValue,
        string expectedCnameTarget,
        DateTime domainCreatedAt,
        CancellationToken ct = default);

    /// <summary>Removes any cached result for <paramref name="hostname"/>, forcing a fresh lookup.</summary>
    void InvalidateCache(string hostname);
}
