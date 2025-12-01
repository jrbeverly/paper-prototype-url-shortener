using ControlPlane.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlPlane.UnitTests.Services;

public sealed class DomainDiagnosticsServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DomainDiagnosticsService CreateService(
        IDnsVerificationService? dns = null,
        ICertificateService? certificates = null,
        IHttpConnectivityCheck? connectivity = null,
        IDnsPropagationCheck? propagation = null) =>
        new(
            dns ?? PassDns(),
            certificates ?? IssuedCertService(),
            connectivity ?? PassConnectivityCheck(),
            propagation ?? AllPropagatedCheck(),
            NullLogger<DomainDiagnosticsService>.Instance);

    // Minimal domain — no cert or distribution; adjust with `with` as needed.
    private static DomainEntity CreateDomain(string status = "pending_verification") =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Hostname = "links.example.com",
            Status = status,
            VerificationCode = "abc123",
            CnameTarget = "domains.short.io",
            CreatedAt = DateTime.UtcNow
        };

    // Full active domain: cert + Deployed distribution → all checks can run.
    private static DomainEntity ActiveDomain() =>
        CreateDomain("active") with
        {
            CertificateArn = "arn:aws:acm:us-east-1:123:certificate/test",
            DistributionTenantId = "dt_test",
            DistributionTenantStatus = "Deployed"
        };

    private static DiagnosticCheckResult GetCheck(DiagnosticsReport report, string name) =>
        report.Checks.Single(c => c.Name == name);

    // ── Shape and completeness ────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunAsync_Always_Returns7Checks()
    {
        var report = await CreateService().RunAsync(ActiveDomain());

        report.Checks.Should().HaveCount(7);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunAsync_CheckNames_AllSevenPresentExactlyOnce()
    {
        var report = await CreateService().RunAsync(ActiveDomain());

        report.Checks.Select(c => c.Name).Should().BeEquivalentTo(
        [
            "domain_status", "dns_txt_record", "dns_cname_record",
            "certificate_status", "distribution_tenant",
            "https_connectivity", "dns_propagation"
        ]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunAsync_RunAt_IsRecentUtc()
    {
        var before = DateTime.UtcNow;
        var report = await CreateService().RunAsync(ActiveDomain());

        report.RunAt.Should().BeOnOrAfter(before);
        report.RunAt.Should().BeOnOrBefore(DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RunAsync_Issues_IsNeverNull()
    {
        var report = await CreateService().RunAsync(ActiveDomain());

        report.Issues.Should().NotBeNull();
    }

    // ── Check 1: Domain lifecycle status ─────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DomainStatus_Active_ReturnsPass()
    {
        var report = await CreateService().RunAsync(ActiveDomain());

        GetCheck(report, "domain_status").Status.Should().Be(DiagnosticStatus.Pass);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DomainStatus_Inactive_ReturnsWarning()
    {
        var report = await CreateService().RunAsync(CreateDomain("inactive"));

        GetCheck(report, "domain_status").Status.Should().Be(DiagnosticStatus.Warning);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DomainStatus_CertificateProvisioning_ReturnsWarning()
    {
        var report = await CreateService().RunAsync(CreateDomain("certificate_provisioning"));

        GetCheck(report, "domain_status").Status.Should().Be(DiagnosticStatus.Warning);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DomainStatus_CertificateFailed_ReturnsFailWithIssue()
    {
        var report = await CreateService().RunAsync(CreateDomain("certificate_failed"));

        var check = GetCheck(report, "domain_status");
        check.Status.Should().Be(DiagnosticStatus.Fail);
        report.Issues.Should().Contain(i => i.Code == "certificate_failed");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DomainStatus_CertificateFailed_IssueHasErrorSeverityAndRemediation()
    {
        var report = await CreateService().RunAsync(CreateDomain("certificate_failed"));

        var issue = report.Issues.Single(i => i.Code == "certificate_failed");
        issue.Severity.Should().Be("error");
        issue.Remediation.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DomainStatus_VerificationFailed_ReturnsFailWithIssue()
    {
        var report = await CreateService().RunAsync(CreateDomain("verification_failed"));

        var check = GetCheck(report, "domain_status");
        check.Status.Should().Be(DiagnosticStatus.Fail);
        report.Issues.Should().Contain(i => i.Code == "domain_not_active");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DomainStatus_PendingVerification_ReturnsWarningWithIssue()
    {
        var report = await CreateService().RunAsync(CreateDomain("pending_verification"));

        var check = GetCheck(report, "domain_status");
        check.Status.Should().Be(DiagnosticStatus.Warning);
        report.Issues.Should().Contain(i => i.Code == "domain_not_active");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DomainStatus_UnknownStatus_ReturnsWarning()
    {
        var report = await CreateService().RunAsync(CreateDomain("some_unknown_state"));

        GetCheck(report, "domain_status").Status.Should().Be(DiagnosticStatus.Warning);
    }

    // ── Checks 2 & 3: DNS records ─────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DnsTxt_Pass_ReturnsPass()
    {
        var report = await CreateService(dns: PassDns()).RunAsync(ActiveDomain());

        GetCheck(report, "dns_txt_record").Status.Should().Be(DiagnosticStatus.Pass);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DnsTxt_Pass_DetailsContainExpectedAndActual()
    {
        var report = await CreateService(dns: PassDns()).RunAsync(ActiveDomain());

        var check = GetCheck(report, "dns_txt_record");
        check.Details.Should().ContainKey("expected");
        check.Details.Should().ContainKey("actual");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DnsTxt_Missing_ReturnsFailWithMissingTxtIssue()
    {
        var dns = StubDns(new DnsVerificationResult
        {
            TxtPassed = false,
            ExpectedTxtValue = "short-io-verify=abc123",
            ActualTxtValue = null,           // record absent
            TxtError = "No TXT record found.",
            CnamePassed = true,
            ExpectedCnameValue = "domains.short.io",
            ActualCnameValue = "domains.short.io"
        });

        var report = await CreateService(dns: dns).RunAsync(ActiveDomain());

        GetCheck(report, "dns_txt_record").Status.Should().Be(DiagnosticStatus.Fail);
        report.Issues.Should().Contain(i => i.Code == "missing_txt_record");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DnsTxt_WrongValue_ReturnsFailWithWrongTxtIssue()
    {
        var dns = StubDns(new DnsVerificationResult
        {
            TxtPassed = false,
            ExpectedTxtValue = "short-io-verify=abc123",
            ActualTxtValue = "short-io-verify=wrong-code",  // wrong value present
            TxtError = "TXT value does not match.",
            CnamePassed = true,
            ExpectedCnameValue = "domains.short.io",
            ActualCnameValue = "domains.short.io"
        });

        var report = await CreateService(dns: dns).RunAsync(ActiveDomain());

        GetCheck(report, "dns_txt_record").Status.Should().Be(DiagnosticStatus.Fail);
        report.Issues.Should().Contain(i => i.Code == "wrong_txt_record");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DnsCname_Pass_ReturnsPass()
    {
        var report = await CreateService(dns: PassDns()).RunAsync(ActiveDomain());

        GetCheck(report, "dns_cname_record").Status.Should().Be(DiagnosticStatus.Pass);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DnsCname_Missing_ReturnsFailWithMissingCnameIssue()
    {
        var dns = StubDns(new DnsVerificationResult
        {
            TxtPassed = true,
            ExpectedTxtValue = "short-io-verify=abc123",
            ActualTxtValue = "short-io-verify=abc123",
            CnamePassed = false,
            ExpectedCnameValue = "domains.short.io",
            ActualCnameValue = null,          // CNAME absent
            CnameError = "No CNAME record found."
        });

        var report = await CreateService(dns: dns).RunAsync(ActiveDomain());

        GetCheck(report, "dns_cname_record").Status.Should().Be(DiagnosticStatus.Fail);
        report.Issues.Should().Contain(i => i.Code == "missing_cname_record");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DnsCname_WrongTarget_ReturnsFailWithWrongTargetIssue()
    {
        var dns = StubDns(new DnsVerificationResult
        {
            TxtPassed = true,
            ExpectedTxtValue = "short-io-verify=abc123",
            ActualTxtValue = "short-io-verify=abc123",
            CnamePassed = false,
            ExpectedCnameValue = "domains.short.io",
            ActualCnameValue = "wrong-cdn.other.net",  // wrong target present
            CnameError = "CNAME points to the wrong target."
        });

        var report = await CreateService(dns: dns).RunAsync(ActiveDomain());

        GetCheck(report, "dns_cname_record").Status.Should().Be(DiagnosticStatus.Fail);
        report.Issues.Should().Contain(i => i.Code == "wrong_cname_target");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DnsCname_WrongTarget_IssueDescriptionMentionsActualAndExpected()
    {
        var dns = StubDns(new DnsVerificationResult
        {
            TxtPassed = true,
            ExpectedTxtValue = "short-io-verify=abc123",
            ActualTxtValue = "short-io-verify=abc123",
            CnamePassed = false,
            ExpectedCnameValue = "domains.short.io",
            ActualCnameValue = "bad-cdn.other.net",
            CnameError = "Wrong target."
        });

        var report = await CreateService(dns: dns).RunAsync(ActiveDomain());

        var issue = report.Issues.Single(i => i.Code == "wrong_cname_target");
        issue.Description.Should().Contain("bad-cdn.other.net");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Dns_ServiceThrows_BothTxtAndCnameReturnFail()
    {
        var report = await CreateService(dns: new ThrowingDnsService()).RunAsync(ActiveDomain());

        GetCheck(report, "dns_txt_record").Status.Should().Be(DiagnosticStatus.Fail);
        GetCheck(report, "dns_cname_record").Status.Should().Be(DiagnosticStatus.Fail);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Dns_ServiceThrows_RemainingChecksStillRun()
    {
        var report = await CreateService(dns: new ThrowingDnsService()).RunAsync(ActiveDomain());

        report.Checks.Should().HaveCount(7);
    }

    // ── Check 4: TLS certificate ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Certificate_NoArn_DomainNotActive_ReturnsSkip()
    {
        // CreateDomain("pending_verification") has no CertificateArn (init default = null).
        var report = await CreateService().RunAsync(CreateDomain("pending_verification"));

        GetCheck(report, "certificate_status").Status.Should().Be(DiagnosticStatus.Skip);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Certificate_NoArn_DomainActive_ReturnsFailWithIssue()
    {
        var domain = ActiveDomain() with { CertificateArn = null };

        var report = await CreateService().RunAsync(domain);

        GetCheck(report, "certificate_status").Status.Should().Be(DiagnosticStatus.Fail);
        report.Issues.Should().Contain(i => i.Code == "certificate_missing");
        report.Issues.Single(i => i.Code == "certificate_missing").Severity.Should().Be("error");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Certificate_Issued_ReturnsPass()
    {
        var domain = ActiveDomain();
        var service = CreateService(certificates: IssuedCertService());

        var report = await service.RunAsync(domain);

        GetCheck(report, "certificate_status").Status.Should().Be(DiagnosticStatus.Pass);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Certificate_Issued_DetailsContainStatusAndDates()
    {
        var domain = ActiveDomain();
        var service = CreateService(certificates: IssuedCertService());

        var report = await service.RunAsync(domain);

        var check = GetCheck(report, "certificate_status");
        check.Details.Should().ContainKey("status");
        check.Details!["status"].Should().Be("issued");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Certificate_ExpiringWithin30Days_ReturnsWarningWithIssue()
    {
        var certs = StubCerts(new CertificateStatusResult
        {
            CertificateArn = "arn:aws:acm:us-east-1:123:certificate/expiring",
            Status = "issued",
            SubjectAlternativeNames = ["links.example.com"],
            ExpiresAt = DateTime.UtcNow.AddDays(10),   // within 30-day window
            IsRenewalEligible = false
        });

        var report = await CreateService(certificates: certs).RunAsync(ActiveDomain());

        GetCheck(report, "certificate_status").Status.Should().Be(DiagnosticStatus.Warning);
        report.Issues.Should().Contain(i => i.Code == "certificate_expiring_soon");
        report.Issues.Single(i => i.Code == "certificate_expiring_soon").Severity.Should().Be("warning");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Certificate_ExpiringInMoreThan30Days_ReturnsPass()
    {
        var certs = StubCerts(new CertificateStatusResult
        {
            CertificateArn = "arn:aws:acm:us-east-1:123:certificate/ok",
            Status = "issued",
            SubjectAlternativeNames = ["links.example.com"],
            ExpiresAt = DateTime.UtcNow.AddDays(60),   // safe
            IsRenewalEligible = true
        });

        var report = await CreateService(certificates: certs).RunAsync(ActiveDomain());

        GetCheck(report, "certificate_status").Status.Should().Be(DiagnosticStatus.Pass);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Certificate_PendingValidation_ReturnsWarningWithIssue()
    {
        var certs = StubCerts(new CertificateStatusResult
        {
            CertificateArn = "arn:aws:acm:us-east-1:123:certificate/pending",
            Status = "pending_validation",
            SubjectAlternativeNames = ["links.example.com"]
        });

        var report = await CreateService(certificates: certs).RunAsync(ActiveDomain());

        GetCheck(report, "certificate_status").Status.Should().Be(DiagnosticStatus.Warning);
        report.Issues.Should().Contain(i => i.Code == "certificate_pending");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Certificate_Failed_ReturnsFailWithIssue()
    {
        var certs = StubCerts(new CertificateStatusResult
        {
            CertificateArn = "arn:aws:acm:us-east-1:123:certificate/failed",
            Status = "failed",
            SubjectAlternativeNames = [],
            FailureReason = "DNS record not found."
        });

        var report = await CreateService(certificates: certs).RunAsync(ActiveDomain());

        GetCheck(report, "certificate_status").Status.Should().Be(DiagnosticStatus.Fail);
        report.Issues.Should().Contain(i => i.Code == "certificate_failed");
        report.Issues.Single(i => i.Code == "certificate_failed").Severity.Should().Be("error");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Certificate_ServiceThrows_ReturnsFail()
    {
        var report = await CreateService(certificates: new ThrowingCertificateService())
            .RunAsync(ActiveDomain());

        GetCheck(report, "certificate_status").Status.Should().Be(DiagnosticStatus.Fail);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Certificate_ServiceThrows_RemainingChecksStillRun()
    {
        var report = await CreateService(certificates: new ThrowingCertificateService())
            .RunAsync(ActiveDomain());

        report.Checks.Should().HaveCount(7);
    }

    // ── Check 5: CloudFront distribution tenant ───────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Distribution_NoTenant_DomainNotActive_ReturnsSkip()
    {
        // CreateDomain("pending_verification") has no DistributionTenantId.
        var report = await CreateService().RunAsync(CreateDomain("pending_verification"));

        GetCheck(report, "distribution_tenant").Status.Should().Be(DiagnosticStatus.Skip);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Distribution_NoTenant_DomainActive_ReturnsFailWithIssue()
    {
        var domain = ActiveDomain() with { DistributionTenantId = null, DistributionTenantStatus = null };

        var report = await CreateService().RunAsync(domain);

        GetCheck(report, "distribution_tenant").Status.Should().Be(DiagnosticStatus.Fail);
        report.Issues.Should().Contain(i => i.Code == "distribution_not_provisioned");
        report.Issues.Single(i => i.Code == "distribution_not_provisioned").Severity.Should().Be("error");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Distribution_Deployed_ReturnsPass()
    {
        var domain = ActiveDomain() with { DistributionTenantId = "dt_test", DistributionTenantStatus = "Deployed" };

        var report = await CreateService().RunAsync(domain);

        var check = GetCheck(report, "distribution_tenant");
        check.Status.Should().Be(DiagnosticStatus.Pass);
        check.Details.Should().ContainKey("status");
        check.Details!["status"].Should().Be("Deployed");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Distribution_InProgress_ReturnsWarning()
    {
        var domain = ActiveDomain() with { DistributionTenantId = "dt_test", DistributionTenantStatus = "InProgress" };

        var report = await CreateService().RunAsync(domain);

        GetCheck(report, "distribution_tenant").Status.Should().Be(DiagnosticStatus.Warning);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Distribution_InProgress_MessageMentionsDeploying()
    {
        var domain = ActiveDomain() with { DistributionTenantId = "dt_test", DistributionTenantStatus = "InProgress" };

        var report = await CreateService().RunAsync(domain);

        GetCheck(report, "distribution_tenant").Message.Should().ContainEquivalentOf("deploy");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Distribution_OtherStatus_ReturnsWarning()
    {
        var domain = ActiveDomain() with { DistributionTenantId = "dt_test", DistributionTenantStatus = "Creating" };

        var report = await CreateService().RunAsync(domain);

        GetCheck(report, "distribution_tenant").Status.Should().Be(DiagnosticStatus.Warning);
    }

    // ── Check 6: HTTPS connectivity ───────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Connectivity_NoDist_ReturnsSkip()
    {
        var domain = ActiveDomain() with { DistributionTenantId = null };

        var report = await CreateService().RunAsync(domain);

        GetCheck(report, "https_connectivity").Status.Should().Be(DiagnosticStatus.Skip);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Connectivity_DomainNotActive_ReturnsSkip()
    {
        // no DistributionTenantId on the minimal domain
        var report = await CreateService().RunAsync(CreateDomain("inactive"));

        GetCheck(report, "https_connectivity").Status.Should().Be(DiagnosticStatus.Skip);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Connectivity_ReachableAndValidSsl_ReturnsPass()
    {
        var conn = StubConn(new HttpConnectivityResult
        {
            HttpsReachable = true,
            SslValid = true,
            StatusCode = 302,
            ResponseMs = 45
        });

        var report = await CreateService(connectivity: conn).RunAsync(ActiveDomain());

        var check = GetCheck(report, "https_connectivity");
        check.Status.Should().Be(DiagnosticStatus.Pass);
        check.Details.Should().ContainKey("statusCode");
        check.Details.Should().ContainKey("responseMs");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Connectivity_ReachableButInvalidSsl_ReturnsFailWithIssue()
    {
        var conn = StubConn(new HttpConnectivityResult
        {
            HttpsReachable = true,
            SslValid = false,
            Error = "Certificate not valid for domain."
        });

        var report = await CreateService(connectivity: conn).RunAsync(ActiveDomain());

        GetCheck(report, "https_connectivity").Status.Should().Be(DiagnosticStatus.Fail);
        report.Issues.Should().Contain(i => i.Code == "ssl_invalid");
        report.Issues.Single(i => i.Code == "ssl_invalid").Severity.Should().Be("error");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Connectivity_Unreachable_ReturnsFailWithIssue()
    {
        var conn = StubConn(new HttpConnectivityResult
        {
            HttpsReachable = false,
            SslValid = false,
            Error = "Connection refused."
        });

        var report = await CreateService(connectivity: conn).RunAsync(ActiveDomain());

        GetCheck(report, "https_connectivity").Status.Should().Be(DiagnosticStatus.Fail);
        report.Issues.Should().Contain(i => i.Code == "https_unreachable");
        report.Issues.Single(i => i.Code == "https_unreachable").Severity.Should().Be("error");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Connectivity_ServiceThrows_ReturnsFail()
    {
        var report = await CreateService(connectivity: new ThrowingConnectivityCheck())
            .RunAsync(ActiveDomain());

        GetCheck(report, "https_connectivity").Status.Should().Be(DiagnosticStatus.Fail);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Connectivity_ServiceThrows_RemainingChecksStillRun()
    {
        var report = await CreateService(connectivity: new ThrowingConnectivityCheck())
            .RunAsync(ActiveDomain());

        report.Checks.Should().HaveCount(7);
    }

    // ── Check 7: DNS propagation ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Propagation_AllResolved_ReturnsPassMentioningResolverCount()
    {
        var report = await CreateService(propagation: AllPropagatedCheck()).RunAsync(ActiveDomain());

        var check = GetCheck(report, "dns_propagation");
        check.Status.Should().Be(DiagnosticStatus.Pass);
        check.Message.Should().Contain("3");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Propagation_AllResolved_DetailsContainResolverNames()
    {
        var report = await CreateService(propagation: AllPropagatedCheck()).RunAsync(ActiveDomain());

        var check = GetCheck(report, "dns_propagation");
        check.Details.Should().NotBeNull();
        check.Details!.Should().HaveCount(3);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Propagation_NoneResolved_ReturnsFailWithIssue()
    {
        var report = await CreateService(propagation: StubProp(NoPropagation()))
            .RunAsync(ActiveDomain());

        GetCheck(report, "dns_propagation").Status.Should().Be(DiagnosticStatus.Fail);
        report.Issues.Should().Contain(i => i.Code == "dns_not_propagated");
        report.Issues.Single(i => i.Code == "dns_not_propagated").Severity.Should().Be("error");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Propagation_Partial_ReturnsWarningWithIssue()
    {
        var report = await CreateService(propagation: StubProp(PartialPropagation()))
            .RunAsync(ActiveDomain());

        var check = GetCheck(report, "dns_propagation");
        check.Status.Should().Be(DiagnosticStatus.Warning);
        check.Message.Should().Contain("1/3");
        report.Issues.Should().Contain(i => i.Code == "partial_propagation");
        report.Issues.Single(i => i.Code == "partial_propagation").Severity.Should().Be("warning");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Propagation_ServiceThrows_ReturnsFail()
    {
        var report = await CreateService(propagation: new ThrowingPropagationCheck())
            .RunAsync(ActiveDomain());

        GetCheck(report, "dns_propagation").Status.Should().Be(DiagnosticStatus.Fail);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Propagation_ServiceThrows_RemainingChecksNotAffected()
    {
        var report = await CreateService(propagation: new ThrowingPropagationCheck())
            .RunAsync(ActiveDomain());

        report.Checks.Should().HaveCount(7);
    }

    // ── Precanned result factories ────────────────────────────────────────────

    private static IDnsVerificationService PassDns() =>
        StubDns(new DnsVerificationResult
        {
            TxtPassed = true,
            ExpectedTxtValue = "short-io-verify=abc123",
            ActualTxtValue = "short-io-verify=abc123",
            CnamePassed = true,
            ExpectedCnameValue = "domains.short.io",
            ActualCnameValue = "domains.short.io"
        });

    private static IDnsVerificationService StubDns(DnsVerificationResult result) =>
        new StubDnsService(result);

    private static ICertificateService IssuedCertService() =>
        StubCerts(new CertificateStatusResult
        {
            CertificateArn = "arn:aws:acm:us-east-1:123:certificate/test",
            Status = "issued",
            SubjectAlternativeNames = ["links.example.com"],
            IssuedAt = DateTime.UtcNow.AddMonths(-6),
            ExpiresAt = DateTime.UtcNow.AddMonths(7),
            IsRenewalEligible = true
        });

    private static ICertificateService StubCerts(CertificateStatusResult result) =>
        new StubCertificateService(result);

    private static IHttpConnectivityCheck PassConnectivityCheck() =>
        StubConn(new HttpConnectivityResult
        {
            HttpsReachable = true,
            SslValid = true,
            StatusCode = 200,
            ResponseMs = 50
        });

    private static IHttpConnectivityCheck StubConn(HttpConnectivityResult result) =>
        new StubConnectivityCheck(result);

    private static IDnsPropagationCheck AllPropagatedCheck() =>
        StubProp(AllPropagated());

    private static PropagationCheckResult AllPropagated() => new()
    {
        AllResolved = true,
        PassedCount = 3,
        TotalCount = 3,
        Results =
        [
            new ResolverResult { ResolverName = "Google Public DNS", ResolverAddress = "8.8.8.8",        Resolved = true, ResolvedTarget = "domains.short.io" },
            new ResolverResult { ResolverName = "Cloudflare DNS",    ResolverAddress = "1.1.1.1",        Resolved = true, ResolvedTarget = "domains.short.io" },
            new ResolverResult { ResolverName = "OpenDNS",           ResolverAddress = "208.67.222.222", Resolved = true, ResolvedTarget = "domains.short.io" },
        ]
    };

    private static PropagationCheckResult NoPropagation() => new()
    {
        AllResolved = false,
        PassedCount = 0,
        TotalCount = 3,
        Results =
        [
            new ResolverResult { ResolverName = "Google Public DNS", ResolverAddress = "8.8.8.8",        Resolved = false, Error = "No CNAME record found." },
            new ResolverResult { ResolverName = "Cloudflare DNS",    ResolverAddress = "1.1.1.1",        Resolved = false, Error = "No CNAME record found." },
            new ResolverResult { ResolverName = "OpenDNS",           ResolverAddress = "208.67.222.222", Resolved = false, Error = "No CNAME record found." },
        ]
    };

    private static PropagationCheckResult PartialPropagation() => new()
    {
        AllResolved = false,
        PassedCount = 1,
        TotalCount = 3,
        Results =
        [
            new ResolverResult { ResolverName = "Google Public DNS", ResolverAddress = "8.8.8.8",        Resolved = true,  ResolvedTarget = "domains.short.io" },
            new ResolverResult { ResolverName = "Cloudflare DNS",    ResolverAddress = "1.1.1.1",        Resolved = false, Error = "No CNAME record found." },
            new ResolverResult { ResolverName = "OpenDNS",           ResolverAddress = "208.67.222.222", Resolved = false, Error = "No CNAME record found." },
        ]
    };

    private static IDnsPropagationCheck StubProp(PropagationCheckResult result) =>
        new StubPropagationCheck(result);

    // ── Test doubles ──────────────────────────────────────────────────────────

    private sealed class StubDnsService(DnsVerificationResult result) : IDnsVerificationService
    {
        public Task<DnsVerificationResult> VerifyAsync(
            string hostname, string expectedTxtValue, string expectedCnameValue) =>
            Task.FromResult(result);
    }

    private sealed class ThrowingDnsService : IDnsVerificationService
    {
        public Task<DnsVerificationResult> VerifyAsync(
            string hostname, string expectedTxtValue, string expectedCnameValue) =>
            throw new InvalidOperationException("Simulated DNS lookup failure");
    }

    private sealed class StubCertificateService(CertificateStatusResult statusResult) : ICertificateService
    {
        public Task<CertificateRequestResult> RequestCertificateAsync(
            Guid tenantId, Guid domainId, string hostname) =>
            Task.FromResult(new CertificateRequestResult
            {
                CertificateArn = statusResult.CertificateArn,
                Status = statusResult.Status,
                ValidationRecords = []
            });

        public Task<CertificateStatusResult> GetCertificateStatusAsync(string certificateArn) =>
            Task.FromResult(statusResult);

        public Task<bool> DeleteCertificateAsync(string certificateArn) =>
            Task.FromResult(true);
    }

    private sealed class ThrowingCertificateService : ICertificateService
    {
        public Task<CertificateRequestResult> RequestCertificateAsync(
            Guid tenantId, Guid domainId, string hostname) =>
            throw new InvalidOperationException("Simulated ACM failure");

        public Task<CertificateStatusResult> GetCertificateStatusAsync(string certificateArn) =>
            throw new InvalidOperationException("Simulated ACM failure");

        public Task<bool> DeleteCertificateAsync(string certificateArn) =>
            Task.FromResult(false);
    }

    private sealed class StubConnectivityCheck(HttpConnectivityResult result) : IHttpConnectivityCheck
    {
        public Task<HttpConnectivityResult> CheckAsync(string hostname, CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    private sealed class ThrowingConnectivityCheck : IHttpConnectivityCheck
    {
        public Task<HttpConnectivityResult> CheckAsync(string hostname, CancellationToken ct = default) =>
            throw new InvalidOperationException("Simulated connectivity check failure");
    }

    private sealed class StubPropagationCheck(PropagationCheckResult result) : IDnsPropagationCheck
    {
        public Task<PropagationCheckResult> CheckAsync(
            string hostname, string expectedCnameTarget, CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    private sealed class ThrowingPropagationCheck : IDnsPropagationCheck
    {
        public Task<PropagationCheckResult> CheckAsync(
            string hostname, string expectedCnameTarget, CancellationToken ct = default) =>
            throw new InvalidOperationException("Simulated propagation check failure");
    }
}
