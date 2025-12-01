using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Endpoints;

public sealed class DomainEndpointTests : IAsyncDisposable
{
    private readonly UnitTestWebApplicationFactory _factory = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    private string BaseUrl => $"/api/v1/tenants/{_tenantId}/domains";

    private (HttpClient Client, TestClaimsProvider Claims) AsAdmin() =>
        _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Admin);

    private async Task<CreateDomainResponse> CreateDomainAsync(HttpClient client, string? hostname = null)
    {
        hostname ??= $"unit-{Guid.NewGuid():N}.example.com";
        var response = await client.PostAsJsonAsync(BaseUrl,
            new CreateDomainRequest { Hostname = hostname });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CreateDomainResponse>())!;
    }

    // ── CreateDomain ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateDomain_ValidHostname_Returns201WithBody()
    {
        var (client, _) = AsAdmin();

        var response = await client.PostAsJsonAsync(BaseUrl,
            new CreateDomainRequest { Hostname = "links.example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateDomainResponse>();
        body.Should().NotBeNull();
        body!.Id.Should().NotBeEmpty();
        body.Hostname.Should().Be("links.example.com");
        body.Status.Should().Be("pending_verification");
        body.VerificationInstructions.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateDomain_NormalizesHostnameToLowercase()
    {
        var (client, _) = AsAdmin();

        var response = await client.PostAsJsonAsync(BaseUrl,
            new CreateDomainRequest { Hostname = "LINKS.EXAMPLE.COM" });

        var body = await response.Content.ReadFromJsonAsync<CreateDomainResponse>();
        body!.Hostname.Should().Be("links.example.com");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateDomain_IncludesDnsInstructions()
    {
        var (client, _) = AsAdmin();

        var response = await client.PostAsJsonAsync(BaseUrl,
            new CreateDomainRequest { Hostname = "go.example.com" });

        var body = await response.Content.ReadFromJsonAsync<CreateDomainResponse>();
        var instructions = body!.VerificationInstructions;
        instructions.TxtValue.Should().StartWith("short-io-verify=");
        instructions.CnameValue.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateDomain_DuplicateHostname_Returns409()
    {
        var (client, _) = AsAdmin();
        await CreateDomainAsync(client, "dup.example.com");

        var response = await client.PostAsJsonAsync(BaseUrl,
            new CreateDomainRequest { Hostname = "dup.example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateDomain_ExceedsPlanLimit_Returns402()
    {
        // Seed a tenant on the free plan (MaxDomains=3). Grace limit = ceil(3.3) = 4.
        // Create 4 domains (including 1 grace slot), then the 5th must be blocked.
        await _factory.SeedTenantAsync(_tenantId, plan: "free");
        var (client, _) = AsAdmin();
        for (int i = 0; i < 4; i++)
            await CreateDomainAsync(client);

        var response = await client.PostAsJsonAsync(BaseUrl,
            new CreateDomainRequest { Hostname = $"overflow-{Guid.NewGuid():N}.example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("upgradeUrl").GetString().Should().Be("https://short.io/pricing");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateDomain_WithPlanBypassPermission_IgnoresLimit()
    {
        // Seed a tenant with MaxDomains=1. Grace limit = ceil(1.1) = 2. Fill up to grace.
        await _factory.SeedTenantAsync(_tenantId, maxDomains: 1);
        var (client, _) = AsAdmin();
        for (int i = 0; i < 2; i++)
            await CreateDomainAsync(client);

        // Confirm the limit is now enforced for a regular admin
        var blocked = await client.PostAsJsonAsync(BaseUrl,
            new CreateDomainRequest { Hostname = $"blocked-{Guid.NewGuid():N}.example.com" });
        blocked.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);

        // A principal with plan:bypass can still create
        _factory.SetPlanBypassClaims(_tenantId.ToString(), Roles.Admin);
        var bypassed = await client.PostAsJsonAsync(BaseUrl,
            new CreateDomainRequest { Hostname = $"bypass-{Guid.NewGuid():N}.example.com" });
        bypassed.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateDomain_InvalidHostname_Returns400()
    {
        var (client, _) = AsAdmin();

        var response = await client.PostAsJsonAsync(BaseUrl,
            new CreateDomainRequest { Hostname = "https://not-a-hostname.com/path" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── ListDomains ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListDomains_EmptyTenant_ReturnsEmptyPage()
    {
        var (client, _) = AsAdmin();

        var response = await client.GetAsync(BaseUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ListDomainsResponse>();
        body!.Items.Should().BeEmpty();
        body.TotalCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListDomains_AfterCreation_ContainsCreatedDomain()
    {
        var (client, _) = AsAdmin();
        await CreateDomainAsync(client, "listed.example.com");

        var response = await client.GetAsync(BaseUrl);

        var body = await response.Content.ReadFromJsonAsync<ListDomainsResponse>();
        body!.Items.Should().ContainSingle(d => d.Hostname == "listed.example.com");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListDomains_PaginationClampsBelowOne()
    {
        var (client, _) = AsAdmin();

        var response = await client.GetAsync($"{BaseUrl}?page=0&pageSize=0");

        // Invalid params are clamped to 1 (page=1, pageSize=1), not rejected
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── GetDomain ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDomain_ExistingDomain_Returns200WithDetail()
    {
        var (client, _) = AsAdmin();
        var created = await CreateDomainAsync(client, "get.example.com");

        var response = await client.GetAsync($"{BaseUrl}/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DomainDetailResponse>();
        body!.Id.Should().Be(created.Id);
        body.Hostname.Should().Be("get.example.com");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetDomain_NotFound_Returns404()
    {
        var (client, _) = AsAdmin();

        var response = await client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── UpdateDomain ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateDomain_ChangesDefaultRedirectUrl()
    {
        var (client, _) = AsAdmin();
        var created = await CreateDomainAsync(client);

        var update = new UpdateDomainRequest { DefaultRedirectUrl = "https://fallback.example.com" };
        var response = await client.PatchAsJsonAsync($"{BaseUrl}/{created.Id}", update);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DomainDetailResponse>();
        body!.Settings.DefaultRedirectUrl.Should().Be("https://fallback.example.com");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateDomain_NotFound_Returns404()
    {
        var (client, _) = AsAdmin();

        var response = await client.PatchAsJsonAsync(
            $"{BaseUrl}/{Guid.NewGuid()}", new UpdateDomainRequest());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── DeleteDomain ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteDomain_ExistingDomain_Returns204()
    {
        var (client, _) = AsAdmin();
        var created = await CreateDomainAsync(client);

        var response = await client.DeleteAsync($"{BaseUrl}/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteDomain_NotFound_Returns404()
    {
        var (client, _) = AsAdmin();

        var response = await client.DeleteAsync($"{BaseUrl}/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── VerifyDomain ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task VerifyDomain_DnsPassesAll_StatusBecomesActive()
    {
        var (client, _) = AsAdmin();
        var created = await CreateDomainAsync(client, "verify-pass.example.com");
        _factory.DnsVerification.SetNextResult(
            TestDnsVerificationService.PassResult("short-io-verify=code", "domains.short.io"));

        var response = await client.PostAsync($"{BaseUrl}/{created.Id}/verify", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<VerifyDomainResponse>();
        body!.Status.Should().Be("active");
        body.TxtCheck.Passed.Should().BeTrue();
        body.CnameCheck.Passed.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task VerifyDomain_DnsFails_StatusBecomesVerificationFailed()
    {
        var (client, _) = AsAdmin();
        var created = await CreateDomainAsync(client, "verify-fail.example.com");
        _factory.DnsVerification.SetNextResult(
            TestDnsVerificationService.FailBothResult("txt", "cname"));

        var response = await client.PostAsync($"{BaseUrl}/{created.Id}/verify", null);

        var body = await response.Content.ReadFromJsonAsync<VerifyDomainResponse>();
        body!.Status.Should().Be("verification_failed");
        body.TxtCheck.Passed.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task VerifyDomain_NotFound_Returns404()
    {
        var (client, _) = AsAdmin();

        var response = await client.PostAsync($"{BaseUrl}/{Guid.NewGuid()}/verify", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Certificate ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RequestCertificate_ActiveDomain_Returns201WithValidationRecords()
    {
        var (client, _) = AsAdmin();
        var created = await CreateDomainAsync(client, "cert.example.com");

        // Verify so domain becomes active (also auto-provisions a cert).
        await client.PostAsync($"{BaseUrl}/{created.Id}/verify", null);

        // Explicit certificate request should succeed (auto-provisioned cert is not "explicitly provisioned").
        var response = await client.PostAsync($"{BaseUrl}/{created.Id}/certificate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CertificateProvisionResponse>();
        body.Should().NotBeNull();
        body!.DomainId.Should().Be(created.Id);
        body.Status.Should().Be("pending_validation");
        body.CertificateArn.Should().NotBeNullOrEmpty();
        body.ValidationRecords.Should().NotBeEmpty();
        body.ValidationRecords[0].Name.Should().Contain("cert.example.com");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RequestCertificate_NotActiveDomain_Returns400()
    {
        var (client, _) = AsAdmin();
        var created = await CreateDomainAsync(client, "not-active.example.com");

        // Domain is pending_verification — no explicit verify call.
        var response = await client.PostAsync($"{BaseUrl}/{created.Id}/certificate", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RequestCertificate_AlreadyExplicitlyProvisioned_Returns409()
    {
        var (client, _) = AsAdmin();
        var created = await CreateDomainAsync(client, "dup-cert.example.com");

        // Verify → active + auto-provisioned cert (EP=false).
        await client.PostAsync($"{BaseUrl}/{created.Id}/verify", null);

        // First explicit request → 201 (EP becomes true).
        var first = await client.PostAsync($"{BaseUrl}/{created.Id}/certificate", null);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // Second explicit request → 409.
        var second = await client.PostAsync($"{BaseUrl}/{created.Id}/certificate", null);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RequestCertificate_NotFound_Returns404()
    {
        var (client, _) = AsAdmin();

        var response = await client.PostAsync($"{BaseUrl}/{Guid.NewGuid()}/certificate", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCertificateStatus_NoCertificate_Returns400()
    {
        var (client, _) = AsAdmin();
        var created = await CreateDomainAsync(client, "no-cert.example.com");

        var response = await client.GetAsync($"{BaseUrl}/{created.Id}/certificate");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCertificateStatus_WithCertificate_Returns200()
    {
        var (client, _) = AsAdmin();
        var created = await CreateDomainAsync(client, "has-cert.example.com");

        // Verify → active + auto-provisioned cert.
        await client.PostAsync($"{BaseUrl}/{created.Id}/verify", null);

        var response = await client.GetAsync($"{BaseUrl}/{created.Id}/certificate");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CertificateStatusResponse>();
        body.Should().NotBeNull();
        body!.DomainId.Should().Be(created.Id);
        body.CertificateArn.Should().NotBeNullOrEmpty();
        body.Status.Should().Be("pending_validation");
        body.IsRenewalEligible.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetCertificateStatus_NotFound_Returns404()
    {
        var (client, _) = AsAdmin();

        var response = await client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}/certificate");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task VerifyDomain_OnSuccess_AutoProvisionsCertificate()
    {
        var (client, _) = AsAdmin();
        var created = await CreateDomainAsync(client, "auto-cert.example.com");

        await client.PostAsync($"{BaseUrl}/{created.Id}/verify", null);

        var detail = await client.GetAsync($"{BaseUrl}/{created.Id}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await detail.Content.ReadFromJsonAsync<DomainDetailResponse>();
        body!.CertificateArn.Should().NotBeNullOrEmpty();
        body.CertificateStatus.Should().Be("pending_validation");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteDomain_WithCertificate_Returns204()
    {
        var (client, _) = AsAdmin();
        var created = await CreateDomainAsync(client, "del-cert.example.com");

        // Verify → active + auto-provisioned cert.
        await client.PostAsync($"{BaseUrl}/{created.Id}/verify", null);

        // Delete should succeed even with a certificate provisioned.
        var response = await client.DeleteAsync($"{BaseUrl}/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
