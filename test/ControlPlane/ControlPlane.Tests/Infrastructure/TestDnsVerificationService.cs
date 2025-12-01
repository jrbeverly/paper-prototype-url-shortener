using ControlPlane.Api.Services;

namespace ControlPlane.Tests.Infrastructure;

public sealed class TestDnsVerificationService : IDnsVerificationService
{
    private DnsVerificationResult? _nextResult;

    public void SetNextResult(DnsVerificationResult result)
    {
        _nextResult = result;
    }

    public Task<DnsVerificationResult> VerifyAsync(string hostname, string expectedTxtValue, string expectedCnameValue)
    {
        var result = _nextResult ?? new DnsVerificationResult
        {
            TxtPassed = true,
            ExpectedTxtValue = expectedTxtValue,
            ActualTxtValue = expectedTxtValue,
            CnamePassed = true,
            ExpectedCnameValue = expectedCnameValue,
            ActualCnameValue = expectedCnameValue
        };
        return Task.FromResult(result);
    }

    public static DnsVerificationResult PassResult(string txtValue, string cnameValue) => new()
    {
        TxtPassed = true,
        ExpectedTxtValue = txtValue,
        ActualTxtValue = txtValue,
        CnamePassed = true,
        ExpectedCnameValue = cnameValue,
        ActualCnameValue = cnameValue
    };

    public static DnsVerificationResult FailTxtResult(string txtValue, string cnameValue) => new()
    {
        TxtPassed = false,
        ExpectedTxtValue = txtValue,
        ActualTxtValue = null,
        TxtError = "No TXT record containing the verification code was found.",
        CnamePassed = true,
        ExpectedCnameValue = cnameValue,
        ActualCnameValue = cnameValue
    };

    public static DnsVerificationResult FailCnameResult(string txtValue, string cnameValue) => new()
    {
        TxtPassed = true,
        ExpectedTxtValue = txtValue,
        ActualTxtValue = txtValue,
        CnamePassed = false,
        ExpectedCnameValue = cnameValue,
        ActualCnameValue = null,
        CnameError = "No CNAME record pointing to the expected target was found."
    };

    public static DnsVerificationResult FailBothResult(string txtValue, string cnameValue) => new()
    {
        TxtPassed = false,
        ExpectedTxtValue = txtValue,
        TxtError = "No TXT record containing the verification code was found.",
        CnamePassed = false,
        ExpectedCnameValue = cnameValue,
        CnameError = "No CNAME record pointing to the expected target was found."
    };
}
