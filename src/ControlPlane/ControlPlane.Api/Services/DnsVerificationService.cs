using DnsClient;
using DnsClient.Protocol;

namespace ControlPlane.Api.Services;

public sealed class DnsVerificationService : IDnsVerificationService
{
    private readonly ILookupClient _lookupClient;
    private readonly ILogger<DnsVerificationService> _logger;

    public DnsVerificationService(ILookupClient lookupClient, ILogger<DnsVerificationService> logger)
    {
        _lookupClient = lookupClient;
        _logger = logger;
    }

    public async Task<DnsVerificationResult> VerifyAsync(
        string hostname, string expectedTxtValue, string expectedCnameValue)
    {
        var txtCheck = await CheckTxtRecordAsync(hostname, expectedTxtValue);
        var cnameCheck = await CheckCnameRecordAsync(hostname, expectedCnameValue);

        var result = new DnsVerificationResult
        {
            TxtPassed = txtCheck.Passed,
            ExpectedTxtValue = expectedTxtValue,
            ActualTxtValue = txtCheck.Actual,
            TxtError = txtCheck.Error,

            CnamePassed = cnameCheck.Passed,
            ExpectedCnameValue = expectedCnameValue,
            ActualCnameValue = cnameCheck.Actual,
            CnameError = cnameCheck.Error
        };

        _logger.LogInformation(
            "DNS verification for {Hostname}: txt={TxtPassed}, cname={CnamePassed}",
            hostname, result.TxtPassed, result.CnamePassed);

        return result;
    }

    private async Task<(bool Passed, string? Actual, string? Error)> CheckTxtRecordAsync(
        string hostname, string expectedTxtValue)
    {
        try
        {
            var response = await _lookupClient.QueryAsync(hostname, QueryType.TXT);
            var txtRecords = response.Answers.OfType<TxtRecord>();

            foreach (var record in txtRecords)
            {
                foreach (var text in record.Text)
                {
                    if (text.Contains(expectedTxtValue, StringComparison.OrdinalIgnoreCase))
                        return (true, text, null);
                }
            }

            var allValues = txtRecords
                .SelectMany(r => r.Text)
                .ToList();

            return (false, allValues.Count > 0 ? string.Join("; ", allValues) : null,
                "No TXT record containing the verification code was found. " +
                "Ensure you created the TXT record with the exact value provided.");
        }
        catch (DnsResponseException ex) when (ex.Code == DnsResponseCode.NotExistentDomain)
        {
            return (false, null, "Domain does not exist or does not have DNS records yet.");
        }
        catch (Exception ex)
        {
            return (false, null, $"DNS lookup failed: {ex.Message}");
        }
    }

    private async Task<(bool Passed, string? Actual, string? Error)> CheckCnameRecordAsync(
        string hostname, string expectedCnameValue)
    {
        try
        {
            var response = await _lookupClient.QueryAsync(hostname, QueryType.CNAME);
            var cnameRecords = response.Answers.OfType<CNameRecord>();

            foreach (var record in cnameRecords)
            {
                var canonicalName = record.CanonicalName.Value.TrimEnd('.');
                if (canonicalName.Equals(expectedCnameValue.TrimEnd('.'), StringComparison.OrdinalIgnoreCase))
                    return (true, canonicalName, null);
            }

            var actual = cnameRecords.FirstOrDefault()?.CanonicalName.Value.TrimEnd('.');
            return (false, actual,
                $"No CNAME record pointing to '{expectedCnameValue}' was found. " +
                "Ensure you created the CNAME record with the exact target value provided.");
        }
        catch (DnsResponseException ex) when (ex.Code == DnsResponseCode.NotExistentDomain)
        {
            return (false, null, "Domain does not exist or does not have DNS records yet.");
        }
        catch (Exception ex)
        {
            return (false, null, $"DNS lookup failed: {ex.Message}");
        }
    }
}
