using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;

namespace ControlPlane.Tests.Endpoints;

[Collection("DynamoDB")]
public sealed class DomainLifecycleTests : IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly Guid _tenantId = Guid.NewGuid();

    public DomainLifecycleTests(LocalStackFixture localStack)
    {
        _factory = new CustomWebApplicationFactory(localStack.DynamoDb, localStack.TableName);
    }

    /// <summary>Creates a domain and runs DNS verification so it reaches "active" status.</summary>
    private async Task<(Guid Id, DomainDetailResponse Detail)> CreateActiveDomainAsync(
        HttpClient client, TestClaimsProvider claimsProvider, string hostname)
    {
        // Explicitly set passing DNS result so prior test state cannot interfere.
        _factory.DnsVerification.SetNextResult(
            TestDnsVerificationService.PassResult("pass-txt", "pass-cname"));

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = hostname });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var verify = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/verify", null);
        verify.StatusCode.Should().Be(HttpStatusCode.OK);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var get = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}");
        var detail = await get.Content.ReadFromJsonAsync<DomainDetailResponse>();
        detail!.Status.Should().Be("active");

        return (created.Id, detail);
    }

    // ── DeactivateDomain ─────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeactivateDomain_ActiveDomain_Returns200WithInactiveStatus()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var (domainId, _) = await CreateActiveDomainAsync(
            client, claimsProvider, "deactivate-active.example.com");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{domainId}/deactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DomainDetailResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("inactive");
        body.Id.Should().Be(domainId);
        body.Hostname.Should().Be("deactivate-active.example.com");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeactivateDomain_SetsDeactivatedAtTimestamp()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var (domainId, _) = await CreateActiveDomainAsync(
            client, claimsProvider, "deactivate-timestamp.example.com");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{domainId}/deactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DomainDetailResponse>();
        body!.DeactivatedAt.Should().NotBeNull();
        body.DeactivatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
        body.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeactivateDomain_AlreadyInactive_Returns409Conflict()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var (domainId, _) = await CreateActiveDomainAsync(
            client, claimsProvider, "deactivate-twice.example.com");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var first = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{domainId}/deactivate", null);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var second = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{domainId}/deactivate", null);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeactivateDomain_PendingVerificationDomain_Returns200()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "deactivate-pending.example.com" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/deactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DomainDetailResponse>();
        body!.Status.Should().Be("inactive");
        body.DeactivatedAt.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeactivateDomain_NotFound_Returns404()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{Guid.NewGuid()}/deactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeactivateDomain_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{Guid.NewGuid()}/deactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeactivateDomain_WithViewerRole_Returns403()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Viewer);

        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{Guid.NewGuid()}/deactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── ReactivateDomain ──────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ReactivateDomain_InactiveDomain_Returns200WithActiveStatus()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var (domainId, _) = await CreateActiveDomainAsync(
            client, claimsProvider, "reactivate-basic.example.com");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{domainId}/deactivate", null);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{domainId}/reactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DomainDetailResponse>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("active");
        body.Id.Should().Be(domainId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ReactivateDomain_ClearsDeactivatedAt()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var (domainId, _) = await CreateActiveDomainAsync(
            client, claimsProvider, "reactivate-timestamp.example.com");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var deactivateResponse = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{domainId}/deactivate", null);
        var deactivated = await deactivateResponse.Content.ReadFromJsonAsync<DomainDetailResponse>();
        deactivated!.DeactivatedAt.Should().NotBeNull();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{domainId}/reactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DomainDetailResponse>();
        body!.DeactivatedAt.Should().BeNull();
        body.Status.Should().Be("active");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ReactivateDomain_ReprovisionsCertificate()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var (domainId, activeDomain) = await CreateActiveDomainAsync(
            client, claimsProvider, "reactivate-cert.example.com");
        activeDomain.CertificateArn.Should().NotBeNullOrEmpty();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{domainId}/deactivate", null);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{domainId}/reactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DomainDetailResponse>();
        body!.CertificateArn.Should().NotBeNullOrEmpty();
        body.CertificateStatus.Should().BeOneOf("pending_validation", "pending");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ReactivateDomain_ActiveDomain_Returns409Conflict()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var (domainId, _) = await CreateActiveDomainAsync(
            client, claimsProvider, "reactivate-already-active.example.com");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{domainId}/reactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ReactivateDomain_PendingVerificationDomain_Returns409Conflict()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "reactivate-pending.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/reactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ReactivateDomain_NotFound_Returns404()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{Guid.NewGuid()}/reactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ReactivateDomain_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{Guid.NewGuid()}/reactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ReactivateDomain_WithViewerRole_Returns403()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Viewer);

        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{Guid.NewGuid()}/reactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── End-to-end onboarding flows ───────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task OnboardingFlow_CreateVerifyActivate_CertificateAndDistributionProvisioned()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        // Step 1: Register domain — initially pending
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "flow-onboard.example.com" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();
        created!.Status.Should().Be("pending_verification");
        created.VerificationInstructions.TxtValue.Should().StartWith("short-io-verify=");
        created.VerificationInstructions.CnameValue.Should().NotBeEmpty();

        // Step 2: DNS passes — domain becomes active, certificate and distribution provisioned
        _factory.DnsVerification.SetNextResult(
            TestDnsVerificationService.PassResult(
                created.VerificationInstructions.TxtValue,
                created.VerificationInstructions.CnameValue));

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var verify = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}/verify", null);
        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        var verifyBody = await verify.Content.ReadFromJsonAsync<VerifyDomainResponse>();
        verifyBody!.Status.Should().Be("active");
        verifyBody.TxtCheck.Passed.Should().BeTrue();
        verifyBody.CnameCheck.Passed.Should().BeTrue();
        verifyBody.Message.Should().Contain("activated successfully");

        // Step 3: Confirm final domain state
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var get = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await get.Content.ReadFromJsonAsync<DomainDetailResponse>();
        detail!.Status.Should().Be("active");
        detail.CertificateArn.Should().NotBeNullOrEmpty();
        detail.CertificateStatus.Should().Be("pending_validation");
        detail.DistributionTenantId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task OnboardingFlow_VerificationFailure_DomainStaysInFailedState()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "flow-fail.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        // Both TXT and CNAME checks fail
        _factory.DnsVerification.SetNextResult(
            TestDnsVerificationService.FailBothResult(
                created!.VerificationInstructions.TxtValue,
                created.VerificationInstructions.CnameValue));

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var verify = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}/verify", null);
        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        var verifyBody = await verify.Content.ReadFromJsonAsync<VerifyDomainResponse>();
        verifyBody!.Status.Should().Be("verification_failed");
        verifyBody.TxtCheck.Passed.Should().BeFalse();
        verifyBody.CnameCheck.Passed.Should().BeFalse();

        // Domain is not active — no certificate or distribution provisioned
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var get = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}");
        var detail = await get.Content.ReadFromJsonAsync<DomainDetailResponse>();
        detail!.Status.Should().Be("verification_failed");
        detail.CertificateArn.Should().BeNull();
        detail.DistributionTenantId.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task OnboardingFlow_VerificationFailureThenRateLimit_ImmediateRetryBlocked()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "flow-ratelimit.example.com" });
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        // First attempt: DNS fails
        _factory.DnsVerification.SetNextResult(
            TestDnsVerificationService.FailBothResult(
                created!.VerificationInstructions.TxtValue,
                created.VerificationInstructions.CnameValue));

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var first = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}/verify", null);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<VerifyDomainResponse>();
        firstBody!.Status.Should().Be("verification_failed");

        // Immediate retry is blocked by the 60-second cooldown, even when DNS is now ready
        _factory.DnsVerification.SetNextResult(
            TestDnsVerificationService.PassResult(
                created.VerificationInstructions.TxtValue,
                created.VerificationInstructions.CnameValue));

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var second = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}/verify", null);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // Domain remains in verification_failed despite DNS now being ready
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var get = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created.Id}");
        var detail = await get.Content.ReadFromJsonAsync<DomainDetailResponse>();
        detail!.Status.Should().Be("verification_failed");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LifecycleFlow_ActivateDeactivateReactivate_FullCycle()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        // Phase 1: Create and verify — domain is active with resources provisioned
        var (domainId, activeDomain) = await CreateActiveDomainAsync(
            client, claimsProvider, "flow-full-cycle.example.com");
        activeDomain.Status.Should().Be("active");
        activeDomain.CertificateArn.Should().NotBeNullOrEmpty();
        activeDomain.DistributionTenantId.Should().NotBeNullOrEmpty();

        // Phase 2: Deactivate — domain goes inactive, timestamp set
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var deactivate = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{domainId}/deactivate", null);
        deactivate.StatusCode.Should().Be(HttpStatusCode.OK);
        var deactivated = await deactivate.Content.ReadFromJsonAsync<DomainDetailResponse>();
        deactivated!.Status.Should().Be("inactive");
        deactivated.DeactivatedAt.Should().NotBeNull();

        // Phase 3: Reactivate — domain returns to active with fresh certificate
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var reactivate = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{domainId}/reactivate", null);
        reactivate.StatusCode.Should().Be(HttpStatusCode.OK);
        var reactivated = await reactivate.Content.ReadFromJsonAsync<DomainDetailResponse>();
        reactivated!.Status.Should().Be("active");
        reactivated.DeactivatedAt.Should().BeNull();
        reactivated.CertificateArn.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LifecycleFlow_DeleteActiveDomain_DomainNotAccessibleAndNotListed()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var (domainId, _) = await CreateActiveDomainAsync(
            client, claimsProvider, "flow-delete-active.example.com");

        // Delete the active domain
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var delete = await client.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{domainId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Domain is no longer accessible by ID
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var get = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{domainId}");
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Domain is excluded from the tenant's domain list
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var list = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains");
        var listBody = await list.Content.ReadFromJsonAsync<ListDomainsResponse>();
        listBody!.Items.Should().NotContain(d => d.Id == domainId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LifecycleFlow_DomainLimitEnforced_BlocksCreationAtLimit()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        // Create domains up to the hard-block threshold: plan limit (3) + 10% grace = 4 allowed.
        for (int i = 0; i < 4; i++)
        {
            claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
                _tenantId.ToString(), Roles.Admin));
            var r = await client.PostAsJsonAsync(
                $"/api/v1/tenants/{_tenantId}/domains",
                new CreateDomainRequest { Hostname = $"limit-{i}.example.com" });
            r.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // The next domain registration is rejected with 402 (plan limit exceeded).
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var overLimit = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = "limit-overflow.example.com" });

        overLimit.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
    }

    // ── Concurrent domain registrations ───────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentRegistrations_DifferentHostnames_AllSucceed()
    {
        var claimsProvider = _factory.Services.GetRequiredService<TestClaimsProvider>();
        var client = _factory.CreateClient();
        var adminPrincipal = TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin);

        // Launch 3 concurrent creates for distinct hostnames — none should conflict
        var tasks = Enumerable.Range(0, 3).Select(async i =>
        {
            claimsProvider.SetClaims(adminPrincipal);
            return await client.PostAsJsonAsync(
                $"/api/v1/tenants/{_tenantId}/domains",
                new CreateDomainRequest { Hostname = $"concurrent-diff-{i}.example.com" });
        });

        var responses = await Task.WhenAll(tasks);

        responses.Should().AllSatisfy(r =>
            r.StatusCode.Should().Be(HttpStatusCode.Created));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentRegistrations_DifferentHostnames_AllAppearInList()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var adminPrincipal = TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin);

        var hostnames = Enumerable.Range(0, 3)
            .Select(i => $"concurrent-list-{i}.example.com")
            .ToList();

        // Register all domains concurrently
        var createTasks = hostnames.Select(async h =>
        {
            claimsProvider.SetClaims(adminPrincipal);
            return await client.PostAsJsonAsync(
                $"/api/v1/tenants/{_tenantId}/domains",
                new CreateDomainRequest { Hostname = h });
        });
        var creates = await Task.WhenAll(createTasks);
        creates.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.Created));

        // All should appear in the list
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var list = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/domains");
        var listBody = await list.Content.ReadFromJsonAsync<ListDomainsResponse>();
        listBody!.TotalCount.Should().Be(hostnames.Count);
        listBody.Items.Select(d => d.Hostname).Should()
            .BeEquivalentTo(hostnames);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
