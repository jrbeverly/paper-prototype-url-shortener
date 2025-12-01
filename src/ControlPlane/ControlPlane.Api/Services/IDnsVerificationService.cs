namespace ControlPlane.Api.Services;

public interface IDnsVerificationService
{
    Task<DnsVerificationResult> VerifyAsync(string hostname, string expectedTxtValue, string expectedCnameValue);
}

public sealed record DnsVerificationResult
{
    public required bool TxtPassed { get; init; }
    public required string ExpectedTxtValue { get; init; }
    public string? ActualTxtValue { get; init; }
    public string? TxtError { get; init; }

    public required bool CnamePassed { get; init; }
    public required string ExpectedCnameValue { get; init; }
    public string? ActualCnameValue { get; init; }
    public string? CnameError { get; init; }

    public bool AllPassed => TxtPassed && CnamePassed;
}
