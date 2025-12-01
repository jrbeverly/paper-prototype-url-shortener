namespace ControlPlane.Api.Services;

/// <summary>
/// Runs a suite of diagnostic checks for a custom domain and returns a structured report.
/// Checks cover domain lifecycle status, DNS records, TLS certificate, CloudFront distribution,
/// end-to-end HTTPS connectivity, and multi-resolver DNS propagation.
/// </summary>
public sealed class DomainDiagnosticsService : IDomainDiagnosticsService
{
    private readonly IDnsVerificationService _dns;
    private readonly ICertificateService _certificates;
    private readonly IHttpConnectivityCheck _connectivity;
    private readonly IDnsPropagationCheck _propagation;
    private readonly ILogger<DomainDiagnosticsService> _logger;

    public DomainDiagnosticsService(
        IDnsVerificationService dns,
        ICertificateService certificates,
        IHttpConnectivityCheck connectivity,
        IDnsPropagationCheck propagation,
        ILogger<DomainDiagnosticsService> logger)
    {
        _dns = dns;
        _certificates = certificates;
        _connectivity = connectivity;
        _propagation = propagation;
        _logger = logger;
    }

    public async Task<DiagnosticsReport> RunAsync(DomainEntity domain, CancellationToken ct = default)
    {
        var checks = new List<DiagnosticCheckResult>(7);
        var issues = new List<DiagnosticIssue>();
        var now = DateTime.UtcNow;

        // Check 1: Domain lifecycle status (no external I/O)
        checks.Add(BuildDomainStatusCheck(domain, issues));

        // Checks 2 & 3: DNS records — single resolver call produces both TXT and CNAME results
        var txtValue = $"short-io-verify={domain.VerificationCode}";
        DnsVerificationResult dnsResult;
        try
        {
            dnsResult = await _dns.VerifyAsync(domain.Hostname, txtValue, domain.CnameTarget);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DNS lookup failed during diagnostics for {Hostname}", domain.Hostname);
            dnsResult = MakeFailedDnsResult(txtValue, domain.CnameTarget, ex.Message);
        }
        checks.Add(BuildTxtCheck(dnsResult, issues));
        checks.Add(BuildCnameCheck(dnsResult, domain, issues));

        // Check 4: ACM TLS certificate
        checks.Add(await BuildCertificateCheckAsync(domain, issues, ct));

        // Check 5: CloudFront distribution tenant
        checks.Add(BuildDistributionCheck(domain, issues));

        // Check 6: End-to-end HTTPS connectivity (only when distribution is present)
        checks.Add(await BuildConnectivityCheckAsync(domain, issues, ct));

        // Check 7: Multi-resolver DNS propagation
        checks.Add(await BuildPropagationCheckAsync(domain, issues, ct));

        _logger.LogInformation(
            "Diagnostics for domain {DomainId} {Hostname}: pass={Pass} warn={Warn} fail={Fail} skip={Skip}",
            domain.Id, domain.Hostname,
            checks.Count(c => c.Status == DiagnosticStatus.Pass),
            checks.Count(c => c.Status == DiagnosticStatus.Warning),
            checks.Count(c => c.Status == DiagnosticStatus.Fail),
            checks.Count(c => c.Status == DiagnosticStatus.Skip));

        return new DiagnosticsReport
        {
            Checks = checks.AsReadOnly(),
            Issues = issues.AsReadOnly(),
            RunAt = now
        };
    }

    // ── Check builders ────────────────────────────────────────────────────────

    private static DiagnosticCheckResult BuildDomainStatusCheck(
        DomainEntity domain, List<DiagnosticIssue> issues)
    {
        switch (domain.Status)
        {
            case "active":
                return Pass("domain_status", "Domain Status", "Domain is active and verified.");

            case "inactive":
                return Warn("domain_status", "Domain Status",
                    "Domain is deactivated. Links will not resolve until you reactivate it (30-day window).");

            case "certificate_provisioning":
                return Warn("domain_status", "Domain Status",
                    "Domain is awaiting TLS certificate provisioning. This typically completes within a few minutes.");

            case "certificate_failed":
                issues.Add(new DiagnosticIssue
                {
                    Code = "certificate_failed",
                    Severity = "error",
                    Title = "TLS Certificate Provisioning Failed",
                    Description = "AWS ACM could not issue a TLS certificate. This is usually caused by missing or incorrect DNS validation records.",
                    Remediation = "Check the certificate validation CNAME records under Settings › Certificate, add them to your DNS provider, then request a new certificate."
                });
                return Fail("domain_status", "Domain Status",
                    "Domain activation failed during TLS certificate provisioning. No traffic can be served until the certificate is re-provisioned.");

            case "verification_failed":
                issues.Add(new DiagnosticIssue
                {
                    Code = "domain_not_active",
                    Severity = "error",
                    Title = "DNS Verification Failed",
                    Description = "The required DNS records were not found during the last verification attempt.",
                    Remediation = "Add the required TXT and CNAME records to your DNS provider, then click Verify again."
                });
                return Fail("domain_status", "Domain Status",
                    "DNS verification failed. Correct the DNS records and re-verify the domain.");

            default:
                // pending_verification, verifying, or unknown
                issues.Add(new DiagnosticIssue
                {
                    Code = "domain_not_active",
                    Severity = "warning",
                    Title = "Domain Not Yet Active",
                    Description = $"Domain is in '{domain.Status}' status and is not yet serving traffic.",
                    Remediation = "Add the required TXT and CNAME DNS records, then verify the domain to activate it."
                });
                return Warn("domain_status", "Domain Status",
                    $"Domain is in '{domain.Status}' status. Complete DNS verification to activate it.");
        }
    }

    private static DiagnosticCheckResult BuildTxtCheck(
        DnsVerificationResult dns, List<DiagnosticIssue> issues)
    {
        if (dns.TxtPassed)
        {
            return Pass("dns_txt_record", "DNS TXT Record",
                "Ownership verification TXT record is present with the correct value.",
                new Dictionary<string, string?>
                {
                    ["expected"] = dns.ExpectedTxtValue,
                    ["actual"] = dns.ActualTxtValue
                });
        }

        if (dns.ActualTxtValue is null)
        {
            issues.Add(new DiagnosticIssue
            {
                Code = "missing_txt_record",
                Severity = "error",
                Title = "TXT Record Not Found",
                Description = "No TXT record containing the verification code was found. The record may not have been created yet, or DNS propagation is still in progress.",
                Remediation = $"Add a TXT record with the exact value: {dns.ExpectedTxtValue}"
            });
        }
        else
        {
            issues.Add(new DiagnosticIssue
            {
                Code = "wrong_txt_record",
                Severity = "error",
                Title = "TXT Record Has Wrong Value",
                Description = "A TXT record was found but it does not contain the required verification code.",
                Remediation = $"Update the TXT record to exactly: {dns.ExpectedTxtValue}"
            });
        }

        return Fail("dns_txt_record", "DNS TXT Record",
            dns.TxtError ?? "TXT record check failed.",
            new Dictionary<string, string?>
            {
                ["expected"] = dns.ExpectedTxtValue,
                ["actual"] = dns.ActualTxtValue,
                ["error"] = dns.TxtError
            });
    }

    private static DiagnosticCheckResult BuildCnameCheck(
        DnsVerificationResult dns, DomainEntity domain, List<DiagnosticIssue> issues)
    {
        if (dns.CnamePassed)
        {
            return Pass("dns_cname_record", "DNS CNAME Record",
                "CNAME record is present and pointing to the correct target.",
                new Dictionary<string, string?>
                {
                    ["expected"] = dns.ExpectedCnameValue,
                    ["actual"] = dns.ActualCnameValue
                });
        }

        if (dns.ActualCnameValue is not null)
        {
            issues.Add(new DiagnosticIssue
            {
                Code = "wrong_cname_target",
                Severity = "error",
                Title = "CNAME Points to Wrong Target",
                Description = $"Your CNAME record points to '{dns.ActualCnameValue}' but must point to '{domain.CnameTarget}'.",
                Remediation = $"Update the CNAME record for '{domain.Hostname}' to point to '{domain.CnameTarget}'."
            });
        }
        else
        {
            issues.Add(new DiagnosticIssue
            {
                Code = "missing_cname_record",
                Severity = "error",
                Title = "CNAME Record Not Found",
                Description = $"No CNAME record was found for '{domain.Hostname}'. Without it, visitors cannot be routed to your short links.",
                Remediation = $"Add a CNAME record for '{domain.Hostname}' pointing to '{domain.CnameTarget}'."
            });
        }

        return Fail("dns_cname_record", "DNS CNAME Record",
            dns.CnameError ?? "CNAME record check failed.",
            new Dictionary<string, string?>
            {
                ["expected"] = dns.ExpectedCnameValue,
                ["actual"] = dns.ActualCnameValue,
                ["error"] = dns.CnameError
            });
    }

    private async Task<DiagnosticCheckResult> BuildCertificateCheckAsync(
        DomainEntity domain, List<DiagnosticIssue> issues, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(domain.CertificateArn))
        {
            if (domain.Status == "active")
            {
                issues.Add(new DiagnosticIssue
                {
                    Code = "certificate_missing",
                    Severity = "error",
                    Title = "No TLS Certificate Provisioned",
                    Description = "The domain is active but has no TLS certificate. All HTTPS connections will fail.",
                    Remediation = "Request a certificate under Settings › Certificate."
                });
                return Fail("certificate_status", "TLS Certificate",
                    "No TLS certificate has been provisioned for this domain.");
            }

            return Skip("certificate_status", "TLS Certificate",
                "Certificate check skipped — domain is not yet active.");
        }

        CertificateStatusResult certStatus;
        try
        {
            certStatus = await _certificates.GetCertificateStatusAsync(domain.CertificateArn);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Certificate status lookup failed for {Arn}", domain.CertificateArn);
            return Fail("certificate_status", "TLS Certificate",
                "Unable to retrieve certificate status.",
                new Dictionary<string, string?> { ["error"] = ex.Message });
        }

        if (certStatus.Status == "issued")
        {
            if (certStatus.ExpiresAt.HasValue && certStatus.ExpiresAt.Value < DateTime.UtcNow.AddDays(30))
            {
                var daysLeft = (int)(certStatus.ExpiresAt.Value - DateTime.UtcNow).TotalDays;
                issues.Add(new DiagnosticIssue
                {
                    Code = "certificate_expiring_soon",
                    Severity = "warning",
                    Title = "TLS Certificate Expiring Soon",
                    Description = $"The certificate expires in {daysLeft} day{(daysLeft == 1 ? "" : "s")} on {certStatus.ExpiresAt.Value:yyyy-MM-dd}.",
                    Remediation = "ACM auto-renews 60 days before expiry. Ensure the ACM validation CNAME records are still present in your DNS."
                });
                return Warn("certificate_status", "TLS Certificate",
                    $"Certificate is valid but expires in {daysLeft} day{(daysLeft == 1 ? "" : "s")}. Verify auto-renewal DNS records are in place.",
                    new Dictionary<string, string?>
                    {
                        ["status"] = certStatus.Status,
                        ["expiresAt"] = certStatus.ExpiresAt?.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                        ["renewalEligible"] = certStatus.IsRenewalEligible.ToString().ToLowerInvariant()
                    });
            }

            return Pass("certificate_status", "TLS Certificate",
                "TLS certificate is issued and valid.",
                new Dictionary<string, string?>
                {
                    ["status"] = certStatus.Status,
                    ["issuedAt"] = certStatus.IssuedAt?.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                    ["expiresAt"] = certStatus.ExpiresAt?.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture)
                });
        }

        if (certStatus.Status is "pending_validation" or "pending")
        {
            issues.Add(new DiagnosticIssue
            {
                Code = "certificate_pending",
                Severity = "warning",
                Title = "Certificate Pending DNS Validation",
                Description = "AWS ACM is waiting to validate domain ownership via a CNAME record. This process typically completes within 30 minutes once the validation records are in place.",
                Remediation = "Ensure the ACM validation CNAME records are added to your DNS provider. Check Settings › Certificate for the required records."
            });
            return Warn("certificate_status", "TLS Certificate",
                "Certificate is pending DNS validation by AWS ACM.",
                new Dictionary<string, string?> { ["status"] = certStatus.Status });
        }

        // failed or other terminal state
        issues.Add(new DiagnosticIssue
        {
            Code = "certificate_failed",
            Severity = "error",
            Title = "TLS Certificate Failed",
            Description = certStatus.FailureReason ?? "AWS ACM could not validate domain ownership.",
            Remediation = "Request a new certificate and ensure the validation CNAME records are present in DNS."
        });
        return Fail("certificate_status", "TLS Certificate",
            $"Certificate validation failed: {certStatus.FailureReason ?? "unknown reason"}.",
            new Dictionary<string, string?>
            {
                ["status"] = certStatus.Status,
                ["failureReason"] = certStatus.FailureReason
            });
    }

    private static DiagnosticCheckResult BuildDistributionCheck(
        DomainEntity domain, List<DiagnosticIssue> issues)
    {
        if (string.IsNullOrEmpty(domain.DistributionTenantId))
        {
            if (domain.Status == "active")
            {
                issues.Add(new DiagnosticIssue
                {
                    Code = "distribution_not_provisioned",
                    Severity = "error",
                    Title = "CloudFront Distribution Not Provisioned",
                    Description = "The domain is active but has no CloudFront distribution tenant. Redirect traffic cannot be served.",
                    Remediation = "Re-verify the domain to trigger automatic provisioning, or contact support."
                });
                return Fail("distribution_tenant", "CloudFront Distribution",
                    "No CloudFront distribution tenant is provisioned for this domain.");
            }

            return Skip("distribution_tenant", "CloudFront Distribution",
                "Distribution check skipped — domain is not yet active.");
        }

        if (domain.DistributionTenantStatus == "Deployed")
        {
            return Pass("distribution_tenant", "CloudFront Distribution",
                "CloudFront distribution tenant is deployed and serving traffic.",
                new Dictionary<string, string?>
                {
                    ["tenantId"] = domain.DistributionTenantId,
                    ["status"] = domain.DistributionTenantStatus
                });
        }

        if (domain.DistributionTenantStatus == "InProgress")
        {
            return Warn("distribution_tenant", "CloudFront Distribution",
                "CloudFront distribution is still deploying (typically 5–10 minutes after domain activation).",
                new Dictionary<string, string?>
                {
                    ["tenantId"] = domain.DistributionTenantId,
                    ["status"] = domain.DistributionTenantStatus
                });
        }

        return Warn("distribution_tenant", "CloudFront Distribution",
            $"CloudFront distribution is in '{domain.DistributionTenantStatus}' status.",
            new Dictionary<string, string?>
            {
                ["tenantId"] = domain.DistributionTenantId,
                ["status"] = domain.DistributionTenantStatus
            });
    }

    private async Task<DiagnosticCheckResult> BuildConnectivityCheckAsync(
        DomainEntity domain, List<DiagnosticIssue> issues, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(domain.DistributionTenantId) || domain.Status != "active")
        {
            return Skip("https_connectivity", "HTTPS Connectivity",
                "HTTPS check skipped — domain must be active with a deployed distribution.");
        }

        HttpConnectivityResult result;
        try
        {
            result = await _connectivity.CheckAsync(domain.Hostname, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HTTPS connectivity check error for {Hostname}", domain.Hostname);
            return Fail("https_connectivity", "HTTPS Connectivity",
                "HTTPS connectivity check encountered an unexpected error.",
                new Dictionary<string, string?> { ["error"] = ex.Message });
        }

        if (result.HttpsReachable && result.SslValid)
        {
            return Pass("https_connectivity", "HTTPS Connectivity",
                "Domain is reachable over HTTPS with a valid TLS certificate.",
                new Dictionary<string, string?>
                {
                    ["statusCode"] = result.StatusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["responseMs"] = result.ResponseMs?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
        }

        if (result.HttpsReachable && !result.SslValid)
        {
            issues.Add(new DiagnosticIssue
            {
                Code = "ssl_invalid",
                Severity = "error",
                Title = "TLS Certificate Not Valid for Domain",
                Description = $"The domain responded over HTTPS but the TLS certificate is invalid: {result.Error}",
                Remediation = "Wait for certificate issuance to complete, or request a new certificate."
            });
            return Fail("https_connectivity", "HTTPS Connectivity",
                "Domain is reachable but the TLS certificate is not valid.",
                new Dictionary<string, string?> { ["error"] = result.Error });
        }

        issues.Add(new DiagnosticIssue
        {
            Code = "https_unreachable",
            Severity = "error",
            Title = "Domain Not Reachable Over HTTPS",
            Description = $"Could not connect to https://{domain.Hostname}. The CNAME may not yet point to CloudFront, or the distribution is still deploying.",
            Remediation = "Verify the CNAME record points to the correct CloudFront target and wait for the distribution to finish deploying."
        });
        return Fail("https_connectivity", "HTTPS Connectivity",
            result.Error ?? "Connection refused or timed out.",
            new Dictionary<string, string?> { ["error"] = result.Error });
    }

    private async Task<DiagnosticCheckResult> BuildPropagationCheckAsync(
        DomainEntity domain, List<DiagnosticIssue> issues, CancellationToken ct)
    {
        PropagationCheckResult result;
        try
        {
            result = await _propagation.CheckAsync(domain.Hostname, domain.CnameTarget, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Propagation check error for {Hostname}", domain.Hostname);
            return Fail("dns_propagation", "DNS Propagation",
                "DNS propagation check encountered an error.",
                new Dictionary<string, string?> { ["error"] = ex.Message });
        }

        var details = result.Results.ToDictionary(
            r => r.ResolverName,
            r => (string?)(r.Resolved ? r.ResolvedTarget : $"FAILED: {r.Error}"));

        if (result.AllResolved)
        {
            return Pass("dns_propagation", "DNS Propagation",
                $"DNS has propagated to all {result.TotalCount} checked resolvers.",
                details);
        }

        if (result.PassedCount == 0)
        {
            issues.Add(new DiagnosticIssue
            {
                Code = "dns_not_propagated",
                Severity = "error",
                Title = "DNS Not Propagated",
                Description = "The CNAME record is not visible from any of the checked DNS resolvers. It may have been recently added or not yet created.",
                Remediation = "Add the CNAME record if not done yet. DNS propagation typically takes 5–30 minutes but can take up to 48 hours."
            });
            return Fail("dns_propagation", "DNS Propagation",
                $"CNAME record not visible from any resolver ({result.TotalCount} checked).",
                details);
        }

        issues.Add(new DiagnosticIssue
        {
            Code = "partial_propagation",
            Severity = "warning",
            Title = "DNS Partially Propagated",
            Description = $"The CNAME record is visible from {result.PassedCount} of {result.TotalCount} checked resolvers. Global propagation can take up to 48 hours.",
            Remediation = "Wait for full propagation. If the issue persists after 48 hours, check your DNS provider settings."
        });
        return Warn("dns_propagation", "DNS Propagation",
            $"DNS propagation in progress: {result.PassedCount}/{result.TotalCount} resolvers have the record.",
            details);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DiagnosticCheckResult Pass(string name, string label, string message,
        IReadOnlyDictionary<string, string?>? details = null) =>
        new() { Name = name, Label = label, Status = DiagnosticStatus.Pass, Message = message, Details = details };

    private static DiagnosticCheckResult Warn(string name, string label, string message,
        IReadOnlyDictionary<string, string?>? details = null) =>
        new() { Name = name, Label = label, Status = DiagnosticStatus.Warning, Message = message, Details = details };

    private static DiagnosticCheckResult Fail(string name, string label, string message,
        IReadOnlyDictionary<string, string?>? details = null) =>
        new() { Name = name, Label = label, Status = DiagnosticStatus.Fail, Message = message, Details = details };

    private static DiagnosticCheckResult Skip(string name, string label, string message) =>
        new() { Name = name, Label = label, Status = DiagnosticStatus.Skip, Message = message };

    private static DnsVerificationResult MakeFailedDnsResult(
        string txtValue, string cnameTarget, string errorMessage)
    {
        var error = $"DNS lookup failed: {errorMessage}";
        return new DnsVerificationResult
        {
            TxtPassed = false,
            ExpectedTxtValue = txtValue,
            TxtError = error,
            CnamePassed = false,
            ExpectedCnameValue = cnameTarget,
            CnameError = error
        };
    }
}
