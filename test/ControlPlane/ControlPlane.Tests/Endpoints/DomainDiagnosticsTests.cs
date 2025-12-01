using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;

namespace ControlPlane.Tests.Endpoints;

[Collection("DynamoDB")]
public sealed class DomainDiagnosticsTests : IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly Guid _tenantId = Guid.NewGuid();

    public DomainDiagnosticsTests(LocalStackFixture localStack)
    {
        _factory = new CustomWebApplicationFactory(localStack.DynamoDb, localStack.TableName);
    }

    /// <summary>Creates a domain and verifies it so it reaches "active" status.</summary>
    private async Task<Guid> CreateActiveDomainAsync(
        HttpClient client, TestClaimsProvider claimsProvider, string hostname)
    {
        _factory.DnsVerification.SetNextResult(
            TestDnsVerificationService.PassResult("pass-txt", "pass-cname"));

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = hostname });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/verify", null);

        return created.Id;
    }

    // ── GetDiagnostics ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetDiagnostics_PendingDomain_Returns200WithStructuredReport()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "diag-pending.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/diagnostics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DomainDiagnosticsResponse>();
        body.Should().NotBeNull();
        body!.DomainId.Should().Be(created.Id);
        body.Hostname.Should().Be("diag-pending.example.com");
        body.OverallStatus.Should().BeOneOf("pass", "fail", "partial");
        body.Checks.Should().NotBeEmpty();
        body.DetectedIssues.Should().NotBeNull();
        body.Summary.Should().NotBeNullOrWhiteSpace();
        body.RunAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetDiagnostics_ResponseContainsAllSevenChecks()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "diag-allchecks.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/diagnostics");

        var body = await response.Content.ReadFromJsonAsync<DomainDiagnosticsResponse>();
        var checkNames = body!.Checks.Select(c => c.Name).ToList();

        checkNames.Should().Contain("domain_status");
        checkNames.Should().Contain("dns_txt_record");
        checkNames.Should().Contain("dns_cname_record");
        checkNames.Should().Contain("certificate_status");
        checkNames.Should().Contain("distribution_tenant");
        checkNames.Should().Contain("https_connectivity");
        checkNames.Should().Contain("dns_propagation");
        body.Checks.Should().HaveCount(7);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetDiagnostics_EachCheckHasRequiredFields()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "diag-fields.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/diagnostics");

        var body = await response.Content.ReadFromJsonAsync<DomainDiagnosticsResponse>();
        body!.Checks.Should().AllSatisfy(c =>
        {
            c.Name.Should().NotBeNullOrWhiteSpace();
            c.Label.Should().NotBeNullOrWhiteSpace();
            c.Status.Should().BeOneOf("pass", "fail", "warning", "skip");
            c.Message.Should().NotBeNullOrWhiteSpace();
        });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetDiagnostics_PendingDomain_DomainStatusCheckIsWarning()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "diag-status-pending.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/diagnostics");

        var body = await response.Content.ReadFromJsonAsync<DomainDiagnosticsResponse>();
        var statusCheck = body!.Checks.Single(c => c.Name == "domain_status");
        statusCheck.Status.Should().Be("warning");
        statusCheck.Message.Should().Contain("pending_verification");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetDiagnostics_VerificationFailedDomain_DomainStatusCheckIsFail()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "diag-status-failed.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        // Trigger a verification failure
        _factory.DnsVerification.SetNextResult(
            TestDnsVerificationService.FailBothResult(
                created!.VerificationInstructions.TxtValue,
                created.VerificationInstructions.CnameValue));

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}/verify", null);

        // Reset DNS to pass so diagnostics DNS checks pass cleanly
        _factory.DnsVerification.SetNextResult(
            TestDnsVerificationService.PassResult("pass-txt", "pass-cname"));

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}/diagnostics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DomainDiagnosticsResponse>();
        var statusCheck = body!.Checks.Single(c => c.Name == "domain_status");
        statusCheck.Status.Should().Be("fail");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetDiagnostics_ActiveDomain_DomainStatusCheckIsPass()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var domainId = await CreateActiveDomainAsync(
            client, claimsProvider, "diag-status-active.example.com");

        // Ensure DNS checks pass for diagnostics
        _factory.DnsVerification.SetNextResult(
            TestDnsVerificationService.PassResult("pass-txt", "pass-cname"));

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{domainId}/diagnostics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DomainDiagnosticsResponse>();
        var statusCheck = body!.Checks.Single(c => c.Name == "domain_status");
        statusCheck.Status.Should().Be("pass");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetDiagnostics_InactiveDomain_DomainStatusCheckIsWarning()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var domainId = await CreateActiveDomainAsync(
            client, claimsProvider, "diag-status-inactive.example.com");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{domainId}/deactivate", null);

        _factory.DnsVerification.SetNextResult(
            TestDnsVerificationService.PassResult("pass-txt", "pass-cname"));

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{domainId}/diagnostics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DomainDiagnosticsResponse>();
        var statusCheck = body!.Checks.Single(c => c.Name == "domain_status");
        statusCheck.Status.Should().Be("warning");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetDiagnostics_PendingDomain_CertificateCheckIsSkipped()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "diag-cert-skip.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/diagnostics");

        var body = await response.Content.ReadFromJsonAsync<DomainDiagnosticsResponse>();
        var certCheck = body!.Checks.Single(c => c.Name == "certificate_status");
        certCheck.Status.Should().Be("skip");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetDiagnostics_PendingDomain_DistributionCheckIsSkipped()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "diag-dist-skip.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/diagnostics");

        var body = await response.Content.ReadFromJsonAsync<DomainDiagnosticsResponse>();
        var distCheck = body!.Checks.Single(c => c.Name == "distribution_tenant");
        distCheck.Status.Should().Be("skip");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetDiagnostics_DetectedIssuesHaveRequiredFields()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "diag-issues.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/diagnostics");

        var body = await response.Content.ReadFromJsonAsync<DomainDiagnosticsResponse>();

        // A pending domain always has at least one issue (domain_not_active warning)
        body!.DetectedIssues.Should().NotBeEmpty();
        body.DetectedIssues.Should().AllSatisfy(i =>
        {
            i.Code.Should().NotBeNullOrWhiteSpace();
            i.Severity.Should().BeOneOf("error", "warning");
            i.Title.Should().NotBeNullOrWhiteSpace();
            i.Description.Should().NotBeNullOrWhiteSpace();
        });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetDiagnostics_NotFound_Returns404()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Viewer);

        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{Guid.NewGuid()}/diagnostics");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetDiagnostics_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{Guid.NewGuid()}/diagnostics");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetDiagnostics_ViewerCanRunDiagnostics()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "diag-viewer.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        // Viewer role has domain:read, which is sufficient for diagnostics
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/diagnostics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
