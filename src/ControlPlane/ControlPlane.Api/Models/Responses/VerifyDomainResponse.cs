namespace ControlPlane.Api.Models.Responses;

public sealed record VerifyDomainResponse
{
    /// <summary>The unique identifier of the domain.</summary>
    public required Guid Id { get; init; }

    /// <summary>The hostname being verified.</summary>
    public required string Hostname { get; init; }

    /// <summary>The domain status after verification: "active" or "verification_failed".</summary>
    public required string Status { get; init; }

    /// <summary>Results of the TXT record check.</summary>
    public required DnsCheckDetail TxtCheck { get; init; }

    /// <summary>Results of the CNAME record check.</summary>
    public required DnsCheckDetail CnameCheck { get; init; }

    /// <summary>A human-readable summary of the verification result.</summary>
    public string? Message { get; init; }

    /// <summary>When the verification check was performed.</summary>
    public required DateTime CheckedAt { get; init; }
}

public sealed record DnsCheckDetail
{
    /// <summary>Whether the DNS record check passed.</summary>
    public required bool Passed { get; init; }

    /// <summary>The expected DNS record value.</summary>
    public required string Expected { get; init; }

    /// <summary>The actual DNS record value found, or null if the record was not found.</summary>
    public string? Actual { get; init; }

    /// <summary>Error message if the DNS lookup failed, or null if it succeeded.</summary>
    public string? Error { get; init; }
}
