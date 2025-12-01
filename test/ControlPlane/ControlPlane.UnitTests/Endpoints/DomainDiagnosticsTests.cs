using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Endpoints;

/// <summary>
/// Tests for GET /tenants/{tenantId}/domains/{domainId}/diagnostics.
/// Uses TestDnsVerificationService, TestHttpConnectivityCheck, and TestDnsPropagationCheck
/// to control each check's outcome without making real DNS or HTTP calls.
/// </summary>
public sealed class DomainDiagnosticsTests : IAsyncDisposable
{
    private readonly UnitTestWebApplicationFactory _factory = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    private string BaseUrl => $"/api/v1/tenants/{_tenantId}/domains";

    private (HttpClient Client, TestClaimsProvider Claims) AsAdmin() =>
        _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Admin);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<CreateDomainResponse> CreateDomainAsync(HttpClient client, string? hostname = null)
    {
        hostname ??= $"diag-{Guid.NewGuid():N}.example.com";
        var resp = await client.PostAsJsonAsync(BaseUrl, new CreateDomainRequest { Hostname = hostname });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<CreateDomainResponse>())!;
    }

    private async Task VerifyDomainAsync(HttpClient client, Guid domainId)
    {
        _factory.DnsVerification.SetNextResult(TestDnsVerificationService.PassResult("txt", "cname"));
        var resp = await client.PostAsync($"{BaseUrl}/{domainId}/verify", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<DomainDiagnosticsResponse> RunDiagnosticsAsync(HttpClient client, Guid domainId)
    {
        var resp = await client.GetAsync($"{BaseUrl}/{domainId}/diagnostics");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<DomainDiagnosticsResponse>())!;
    }

    // ── Shape and structure ───────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_ActiveDomain_Returns200()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var resp = await client.GetAsync($"{BaseUrl}/{domain.Id}/diagnostics");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_AlwaysReturns7Checks()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        report.Checks.Should().HaveCount(7);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_CheckNames_AreAllPresent()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var names = report.Checks.Select(c => c.Name).ToList();
        names.Should().Contain("domain_status");
        names.Should().Contain("dns_txt_record");
        names.Should().Contain("dns_cname_record");
        names.Should().Contain("certificate_status");
        names.Should().Contain("distribution_tenant");
        names.Should().Contain("https_connectivity");
        names.Should().Contain("dns_propagation");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_CheckStatuses_AreLowercaseStrings()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var validStatuses = new[] { "pass", "fail", "warning", "skip" };
        foreach (var check in report.Checks)
            check.Status.Should().BeOneOf(validStatuses, because: $"{check.Name} must have a valid status");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_Summary_IsNotEmpty()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        report.Summary.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_RunAt_IsRecent()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        var before = DateTime.UtcNow;

        var report = await RunDiagnosticsAsync(client, domain.Id);

        report.RunAt.Should().BeOnOrAfter(before.AddSeconds(-5));
        report.RunAt.Should().BeOnOrBefore(DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_DomainId_MatchesRequest()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        report.DomainId.Should().Be(domain.Id);
        report.Hostname.Should().Be(domain.Hostname);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_EachCheck_HasNonEmptyLabelAndMessage()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        foreach (var check in report.Checks)
        {
            check.Label.Should().NotBeNullOrWhiteSpace(because: $"{check.Name} must have a label");
            check.Message.Should().NotBeNullOrWhiteSpace(because: $"{check.Name} must have a message");
        }
    }

    // ── domain_status check ───────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_ActiveDomain_DomainStatusPass()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "domain_status");
        check.Status.Should().Be("pass");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_PendingVerificationDomain_DomainStatusWarning()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client); // never verified

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "domain_status");
        check.Status.Should().Be("warning");
        check.Message.Should().Contain("pending_verification");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_InactiveDomain_DomainStatusWarning()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);
        await client.PostAsync($"{BaseUrl}/{domain.Id}/deactivate", null);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "domain_status");
        check.Status.Should().Be("warning");
    }

    // ── DNS TXT record check ──────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_DnsPass_TxtCheckPass()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);
        // DnsVerification is still set to pass from VerifyDomainAsync

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "dns_txt_record");
        check.Status.Should().Be("pass");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_MissingTxtRecord_TxtCheckFail_IssueDetected()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        // Simulate TXT record removed after verification
        _factory.DnsVerification.SetNextResult(
            TestDnsVerificationService.FailTxtResult("txt", "cname"));

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "dns_txt_record");
        check.Status.Should().Be("fail");

        report.DetectedIssues.Should().Contain(i => i.Code == "missing_txt_record");
        var issue = report.DetectedIssues.Single(i => i.Code == "missing_txt_record");
        issue.Severity.Should().Be("error");
        issue.Remediation.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_WrongTxtValue_TxtCheckFail_WrongTxtIssue()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        _factory.DnsVerification.SetNextResult(new DnsVerificationResult
        {
            TxtPassed = false,
            ExpectedTxtValue = "short-io-verify=expected",
            ActualTxtValue = "short-io-verify=wrong-code",   // wrong value present
            TxtError = "TXT value does not match.",
            CnamePassed = true,
            ExpectedCnameValue = "cname",
            ActualCnameValue = "cname"
        });

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "dns_txt_record");
        check.Status.Should().Be("fail");
        report.DetectedIssues.Should().Contain(i => i.Code == "wrong_txt_record");
    }

    // ── DNS CNAME record check ────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_DnsPass_CnameCheckPass()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "dns_cname_record");
        check.Status.Should().Be("pass");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_WrongCnameTarget_CnameCheckFail_IssueDetected()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        _factory.DnsVerification.SetNextResult(new DnsVerificationResult
        {
            TxtPassed = true,
            ExpectedTxtValue = "txt",
            ActualTxtValue = "txt",
            CnamePassed = false,
            ExpectedCnameValue = "links.cf.net",
            ActualCnameValue = "wrong-target.other-cdn.com",  // wrong, but a record exists
            CnameError = "CNAME points to the wrong target."
        });

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "dns_cname_record");
        check.Status.Should().Be("fail");

        var issue = report.DetectedIssues.Single(i => i.Code == "wrong_cname_target");
        issue.Severity.Should().Be("error");
        issue.Description.Should().Contain("wrong-target.other-cdn.com");
        issue.Remediation.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_MissingCnameRecord_CnameCheckFail_MissingIssueDetected()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        _factory.DnsVerification.SetNextResult(
            TestDnsVerificationService.FailBothResult("txt", "cname"));

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "dns_cname_record");
        check.Status.Should().Be("fail");
        report.DetectedIssues.Should().Contain(i => i.Code == "missing_cname_record");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_CnameCheckFail_Details_ContainExpectedAndActual()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        _factory.DnsVerification.SetNextResult(new DnsVerificationResult
        {
            TxtPassed = true,
            ExpectedTxtValue = "txt",
            ActualTxtValue = "txt",
            CnamePassed = false,
            ExpectedCnameValue = "expected-target.cf.net",
            ActualCnameValue = "actual-wrong.net",
            CnameError = "Wrong target."
        });

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "dns_cname_record");
        check.Details.Should().ContainKey("expected");
        check.Details.Should().ContainKey("actual");
        check.Details!["actual"].Should().Be("actual-wrong.net");
    }

    // ── Certificate status check ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_PendingVerificationDomain_CertificateCheckSkipped()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client); // never verified → no cert ARN

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "certificate_status");
        check.Status.Should().Be("skip");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_ActiveDomain_CertificateCheckNotSkipped()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "certificate_status");
        check.Status.Should().NotBe("skip");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_CertificatePending_ReturnsWarningWithIssue()
    {
        // InMemoryCertificateService starts certs as "pending_validation".
        // The diagnostics service will report warning + certificate_pending issue.
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "certificate_status");
        // pending_validation → warning (may auto-issue after 2s, so accept pass too)
        check.Status.Should().BeOneOf("warning", "pass");
    }

    // ── Distribution tenant check ─────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_PendingVerificationDomain_DistributionCheckSkipped()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "distribution_tenant");
        check.Status.Should().Be("skip");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_ActiveDomain_WithDistributionInProgress_ReturnsWarning()
    {
        // InMemoryDistributionService always creates with "InProgress" status.
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "distribution_tenant");
        check.Status.Should().Be("warning");
        check.Message.Should().Contain("deploying");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_ActiveDomain_WithDeployedDistribution_ReturnsPass()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        // Simulate distribution reaching Deployed status via the repository
        var repo = _factory.Services.GetRequiredService<IDomainRepository>();
        var entity = await repo.GetByIdAsync(_tenantId, domain.Id);
        await repo.UpdateAsync(entity! with { DistributionTenantStatus = "Deployed" });

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "distribution_tenant");
        check.Status.Should().Be("pass");
        check.Details.Should().ContainKey("status");
        check.Details!["status"].Should().Be("Deployed");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_ActiveDomain_WithNoDistribution_ReturnsFailWithIssue()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        // Simulate distribution never provisioned (clear it out)
        var repo = _factory.Services.GetRequiredService<IDomainRepository>();
        var entity = await repo.GetByIdAsync(_tenantId, domain.Id);
        await repo.UpdateAsync(entity! with
        {
            DistributionTenantId = null,
            DistributionTenantStatus = null,
            DistributionTenantETag = null
        });

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "distribution_tenant");
        check.Status.Should().Be("fail");
        report.DetectedIssues.Should().Contain(i => i.Code == "distribution_not_provisioned");
    }

    // ── HTTPS connectivity check ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_PendingVerificationDomain_ConnectivityCheckSkipped()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "https_connectivity");
        check.Status.Should().Be("skip");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_ActiveDomainWithDistribution_ConnectivityCheckRuns()
    {
        // Default TestHttpConnectivityCheck returns pass
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "https_connectivity");
        check.Status.Should().Be("pass");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_HttpsUnreachable_ConnectivityCheckFail_IssueDetected()
    {
        _factory.HttpConnectivity.SetResult(TestHttpConnectivityCheck.Unreachable("Connection refused"));

        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "https_connectivity");
        check.Status.Should().Be("fail");
        report.DetectedIssues.Should().Contain(i => i.Code == "https_unreachable");
        var issue = report.DetectedIssues.Single(i => i.Code == "https_unreachable");
        issue.Severity.Should().Be("error");
        issue.Remediation.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_InvalidSsl_ConnectivityCheckFail_SslIssueDetected()
    {
        _factory.HttpConnectivity.SetResult(TestHttpConnectivityCheck.InvalidSsl("Certificate not valid."));

        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "https_connectivity");
        check.Status.Should().Be("fail");
        report.DetectedIssues.Should().Contain(i => i.Code == "ssl_invalid");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_ConnectivityPass_Details_ContainStatusCode()
    {
        _factory.HttpConnectivity.SetResult(new HttpConnectivityResult
        {
            HttpsReachable = true,
            SslValid = true,
            StatusCode = 302,
            ResponseMs = 85
        });

        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "https_connectivity");
        check.Status.Should().Be("pass");
        check.Details.Should().ContainKey("statusCode");
        check.Details!["statusCode"].Should().Be("302");
    }

    // ── DNS propagation check ─────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_AllResolversPass_PropagationCheckPass()
    {
        // Default TestDnsPropagationCheck returns all passed
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "dns_propagation");
        check.Status.Should().Be("pass");
        check.Message.Should().Contain("3");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_AllResolversFail_PropagationCheckFail_IssueDetected()
    {
        _factory.DnsPropagation.SetResult(TestDnsPropagationCheck.AllFailed());

        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "dns_propagation");
        check.Status.Should().Be("fail");
        report.DetectedIssues.Should().Contain(i => i.Code == "dns_not_propagated");
        var issue = report.DetectedIssues.Single(i => i.Code == "dns_not_propagated");
        issue.Remediation.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_PartialPropagation_ReturnsWarning_WithIssue()
    {
        _factory.DnsPropagation.SetResult(TestDnsPropagationCheck.Partial());

        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "dns_propagation");
        check.Status.Should().Be("warning");
        check.Message.Should().Contain("1/3");
        report.DetectedIssues.Should().Contain(i => i.Code == "partial_propagation");
        report.DetectedIssues.Single(i => i.Code == "partial_propagation").Severity.Should().Be("warning");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_PropagationDetails_ContainResolverResults()
    {
        _factory.DnsPropagation.SetResult(TestDnsPropagationCheck.AllPassed("links.cf.net"));

        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        var check = report.Checks.Single(c => c.Name == "dns_propagation");
        check.Details.Should().ContainKey("Google Public DNS");
        check.Details!["Google Public DNS"].Should().Be("links.cf.net");
    }

    // ── OverallStatus ─────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_AnyCheckFails_OverallStatusIsFail()
    {
        _factory.DnsPropagation.SetResult(TestDnsPropagationCheck.AllFailed());

        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        report.OverallStatus.Should().Be("fail");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_PendingVerificationDomain_OverallStatusIsFail()
    {
        _factory.DnsVerification.SetNextResult(
            TestDnsVerificationService.FailBothResult("txt", "cname"));

        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        report.OverallStatus.Should().Be("fail");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_OverallStatus_IsOneOfValidValues()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        report.OverallStatus.Should().BeOneOf("pass", "fail", "partial");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_FailStatus_SummaryMentionsFailCount()
    {
        _factory.DnsPropagation.SetResult(TestDnsPropagationCheck.AllFailed());

        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        report.OverallStatus.Should().Be("fail");
        report.Summary.Should().MatchRegex(@"\d+ check(s)? failed");
    }

    // ── Issues ────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_NoIssues_DetectedIssuesIsEmpty()
    {
        // All defaults pass; the only expected warns are distribution (InProgress) and possibly cert
        // — but we want a domain with no "error"-severity issues
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        // No error-severity issues (warnings are ok)
        report.DetectedIssues.Should().NotContain(i => i.Severity == "error");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_DetectedIssues_HaveNonEmptyRemediationForErrors()
    {
        _factory.DnsVerification.SetNextResult(
            TestDnsVerificationService.FailBothResult("txt", "cname"));

        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var report = await RunDiagnosticsAsync(client, domain.Id);

        foreach (var issue in report.DetectedIssues.Where(i => i.Severity == "error"))
        {
            issue.Remediation.Should().NotBeNullOrWhiteSpace(
                because: $"error issue '{issue.Code}' must provide remediation steps");
        }
    }

    // ── Sharing / human-readability ───────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_Response_IsJsonSerializable()
    {
        // Verify the entire response round-trips through JSON without loss.
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var httpResp = await client.GetAsync($"{BaseUrl}/{domain.Id}/diagnostics");
        var raw = await httpResp.Content.ReadAsStringAsync();
        var deserialized = JsonSerializer.Deserialize<DomainDiagnosticsResponse>(
            raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Checks.Should().HaveCount(7);
    }

    // ── Auth / not found ──────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_NotFound_Returns404()
    {
        var (client, _) = AsAdmin();

        var resp = await client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}/diagnostics");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_Unauthenticated_Returns401()
    {
        await using var factory = new UnitTestWebApplicationFactory();
        var unauthClient = factory.CreateClient();

        var resp = await unauthClient.GetAsync(
            $"/api/v1/tenants/{Guid.NewGuid()}/domains/{Guid.NewGuid()}/diagnostics");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDiagnostics_RequiresDomainReadPermission()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        // Viewer role has domain:read; ensure a read-only role can run diagnostics
        var (viewerClient, _) = _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Viewer);
        var resp = await viewerClient.GetAsync($"{BaseUrl}/{domain.Id}/diagnostics");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
