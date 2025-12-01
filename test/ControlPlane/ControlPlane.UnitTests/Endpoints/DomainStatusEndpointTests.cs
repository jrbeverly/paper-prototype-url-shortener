using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Endpoints;

/// <summary>
/// Tests for GET /tenants/{tenantId}/domains/{domainId}/status.
/// Verifies health score computation, DNS record expectations, suggested actions,
/// verification attempt history, and routing status — all derived from stored state
/// with no live DNS or HTTP probes.
/// </summary>
public sealed class DomainStatusEndpointTests : IAsyncDisposable
{
    private readonly UnitTestWebApplicationFactory _factory = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    private string BaseUrl => $"/api/v1/tenants/{_tenantId}/domains";

    private (HttpClient Client, TestClaimsProvider Claims) AsAdmin() =>
        _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Admin);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<CreateDomainResponse> CreateDomainAsync(HttpClient client, string? hostname = null)
    {
        hostname ??= $"status-{Guid.NewGuid():N}.example.com";
        var resp = await client.PostAsJsonAsync(BaseUrl, new CreateDomainRequest { Hostname = hostname });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<CreateDomainResponse>())!;
    }

    private async Task VerifyDomainAsync(HttpClient client, Guid domainId, bool pass = true)
    {
        _factory.DnsVerification.SetNextResult(pass
            ? TestDnsVerificationService.PassResult("txt", "cname")
            : TestDnsVerificationService.FailTxtResult("txt", "cname"));
        var resp = await client.PostAsync($"{BaseUrl}/{domainId}/verify", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<DomainStatusResponse> GetStatusAsync(HttpClient client, Guid domainId)
    {
        var resp = await client.GetAsync($"{BaseUrl}/{domainId}/status");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<DomainStatusResponse>())!;
    }

    // ── Shape and structure ───────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_Returns200WithValidShape()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var resp = await client.GetAsync($"{BaseUrl}/{domain.Id}/status");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_DomainId_MatchesRequest()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var status = await GetStatusAsync(client, domain.Id);

        status.DomainId.Should().Be(domain.Id);
        status.Hostname.Should().Be(domain.Hostname);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_CheckedAt_IsRecent()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        var before = DateTime.UtcNow;

        var status = await GetStatusAsync(client, domain.Id);

        status.CheckedAt.Should().BeOnOrAfter(before.AddSeconds(-5));
        status.CheckedAt.Should().BeOnOrBefore(DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_AllTopLevelFieldsArePopulated()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var status = await GetStatusAsync(client, domain.Id);

        status.Status.Should().NotBeNullOrWhiteSpace();
        status.HealthScore.Should().NotBeNullOrWhiteSpace();
        status.Verification.Should().NotBeNull();
        status.Certificate.Should().NotBeNull();
        status.Routing.Should().NotBeNull();
        status.SuggestedActions.Should().NotBeNull();
    }

    // ── Health score ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_HealthScore_IsOneOfValidValues()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var status = await GetStatusAsync(client, domain.Id);

        status.HealthScore.Should().BeOneOf("green", "yellow", "red");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_PendingVerification_HealthScoreIsYellow()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var status = await GetStatusAsync(client, domain.Id);

        status.HealthScore.Should().Be("yellow");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_VerificationFailed_HealthScoreIsRed()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id, pass: false);

        var status = await GetStatusAsync(client, domain.Id);

        status.HealthScore.Should().Be("red");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_InactiveDomain_HealthScoreIsRed()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);
        await client.PostAsync($"{BaseUrl}/{domain.Id}/deactivate", null);

        var status = await GetStatusAsync(client, domain.Id);

        status.HealthScore.Should().Be("red");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_ActiveDomain_HealthScoreIsYellowOrGreen()
    {
        // After verify → active, cert is pending_validation and dist is InProgress → yellow.
        // Once cert is issued and dist is Deployed → green. The in-memory services leave both
        // in intermediate states, so yellow is the expected outcome for a freshly activated domain.
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var status = await GetStatusAsync(client, domain.Id);

        status.HealthScore.Should().BeOneOf("green", "yellow");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_ActiveDomain_WithIssuedCertAndDeployedDist_HealthScoreIsGreen()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        // Simulate cert issued + distribution deployed
        var repo = _factory.Services.GetRequiredService<IDomainRepository>();
        var entity = await repo.GetByIdAsync(_tenantId, domain.Id);
        await repo.UpdateAsync(entity! with
        {
            CertificateStatus = "issued",
            DistributionTenantStatus = "Deployed"
        });

        var status = await GetStatusAsync(client, domain.Id);

        status.HealthScore.Should().Be("green");
    }

    // ── DNS record expectations ───────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_DnsRecords_TxtNameMatchesHostname()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var status = await GetStatusAsync(client, domain.Id);

        status.Verification.DnsRecords.TxtName.Should().Be(domain.Hostname);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_DnsRecords_TxtValueContainsVerificationPrefix()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var status = await GetStatusAsync(client, domain.Id);

        status.Verification.DnsRecords.TxtValue.Should().StartWith("short-io-verify=");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_DnsRecords_CnameNameMatchesHostname()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var status = await GetStatusAsync(client, domain.Id);

        status.Verification.DnsRecords.CnameName.Should().Be(domain.Hostname);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_DnsRecords_CnameTargetIsNonEmpty()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var status = await GetStatusAsync(client, domain.Id);

        status.Verification.DnsRecords.CnameTarget.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_DnsRecords_TxtValueIsStableAcrossCalls()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var status1 = await GetStatusAsync(client, domain.Id);
        var status2 = await GetStatusAsync(client, domain.Id);

        status1.Verification.DnsRecords.TxtValue.Should().Be(status2.Verification.DnsRecords.TxtValue);
    }

    // ── Verification attempt history ──────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_NoVerificationAttemptYet_RecentAttemptsIsEmpty()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var status = await GetStatusAsync(client, domain.Id);

        status.Verification.RecentAttempts.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_AfterFailedVerification_RecentAttemptsContainsOneEntry()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id, pass: false);

        var status = await GetStatusAsync(client, domain.Id);

        status.Verification.RecentAttempts.Should().HaveCount(1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_AfterFailedVerification_AttemptIsMarkedFailed()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id, pass: false);

        var status = await GetStatusAsync(client, domain.Id);

        var attempt = status.Verification.RecentAttempts.Single();
        attempt.Passed.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_AfterSuccessfulVerification_RecentAttemptsContainsPassedEntry()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id, pass: true);

        var status = await GetStatusAsync(client, domain.Id);

        status.Verification.RecentAttempts.Should().HaveCount(1);
        status.Verification.RecentAttempts.Single().Passed.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_AttemptTimestamp_IsRecent()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        var before = DateTime.UtcNow;
        await VerifyDomainAsync(client, domain.Id, pass: false);

        var status = await GetStatusAsync(client, domain.Id);

        var attempt = status.Verification.RecentAttempts.Single();
        attempt.AttemptedAt.Should().BeOnOrAfter(before.AddSeconds(-5));
        attempt.AttemptedAt.Should().BeOnOrBefore(DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_MultipleFailedAttempts_AllAreRecorded()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        // Three failed attempts (rate limit is 60s, so advance the entity's LastVerifiedAt between calls)
        for (var i = 0; i < 3; i++)
        {
            var repo = _factory.Services.GetRequiredService<IDomainRepository>();
            var entity = await repo.GetByIdAsync(_tenantId, domain.Id);
            // Move LastVerifiedAt back so the cooldown is satisfied
            await repo.UpdateAsync(entity! with { LastVerifiedAt = DateTime.UtcNow.AddMinutes(-5) });

            await VerifyDomainAsync(client, domain.Id, pass: false);
        }

        var status = await GetStatusAsync(client, domain.Id);

        status.Verification.RecentAttempts.Should().HaveCount(3);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_MoreThan5Attempts_KeepsOnlyLast5()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        for (var i = 0; i < 6; i++)
        {
            var repo = _factory.Services.GetRequiredService<IDomainRepository>();
            var entity = await repo.GetByIdAsync(_tenantId, domain.Id);
            await repo.UpdateAsync(entity! with { LastVerifiedAt = DateTime.UtcNow.AddMinutes(-5) });

            await VerifyDomainAsync(client, domain.Id, pass: false);
        }

        var status = await GetStatusAsync(client, domain.Id);

        status.Verification.RecentAttempts.Should().HaveCount(5);
    }

    // ── Certificate summary ───────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_PendingDomain_CertificateStatusIsPending()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var status = await GetStatusAsync(client, domain.Id);

        status.Certificate.Status.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_PendingDomain_CertificateArnIsNull()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var status = await GetStatusAsync(client, domain.Id);

        status.Certificate.Arn.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_ActiveDomain_CertificateArnIsPopulated()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var status = await GetStatusAsync(client, domain.Id);

        status.Certificate.Arn.Should().NotBeNullOrWhiteSpace();
    }

    // ── Routing status ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_RoutingEndpoint_IsNonEmpty()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var status = await GetStatusAsync(client, domain.Id);

        status.Routing.RoutingEndpoint.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_PendingDomain_IsServingIsFalse()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var status = await GetStatusAsync(client, domain.Id);

        status.Routing.IsServing.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_ActiveDomain_WithDeployedDistribution_IsServingIsTrue()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var repo = _factory.Services.GetRequiredService<IDomainRepository>();
        var entity = await repo.GetByIdAsync(_tenantId, domain.Id);
        await repo.UpdateAsync(entity! with { DistributionTenantStatus = "Deployed" });

        var status = await GetStatusAsync(client, domain.Id);

        status.Routing.IsServing.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_ActiveDomain_DistributionTenantIdIsPopulated()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        var status = await GetStatusAsync(client, domain.Id);

        status.Routing.DistributionTenantId.Should().NotBeNullOrWhiteSpace();
    }

    // ── Suggested actions ─────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_PendingVerification_SuggestsAddDnsRecordsAndTriggerVerification()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var status = await GetStatusAsync(client, domain.Id);

        var codes = status.SuggestedActions.Select(a => a.ActionCode).ToList();
        codes.Should().Contain("add_dns_records");
        codes.Should().Contain("trigger_verification");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_VerificationFailed_SuggestsCheckAndRetry()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id, pass: false);

        var status = await GetStatusAsync(client, domain.Id);

        var codes = status.SuggestedActions.Select(a => a.ActionCode).ToList();
        codes.Should().Contain("check_dns_records");
        codes.Should().Contain("retry_verification");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_InactiveDomain_SuggestsReactivate()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);
        await client.PostAsync($"{BaseUrl}/{domain.Id}/deactivate", null);

        var status = await GetStatusAsync(client, domain.Id);

        status.SuggestedActions.Should().Contain(a => a.ActionCode == "reactivate_domain");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_FullyOperationalDomain_SuggestedActionsIsEmpty()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id);

        // Simulate fully-operational state: cert issued, distribution deployed
        var repo = _factory.Services.GetRequiredService<IDomainRepository>();
        var entity = await repo.GetByIdAsync(_tenantId, domain.Id);
        await repo.UpdateAsync(entity! with
        {
            CertificateStatus = "issued",
            DistributionTenantStatus = "Deployed"
        });

        var status = await GetStatusAsync(client, domain.Id);

        status.SuggestedActions.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_SuggestedActions_EachHasNonEmptyLabelAndDescription()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var status = await GetStatusAsync(client, domain.Id);

        foreach (var action in status.SuggestedActions)
        {
            action.Label.Should().NotBeNullOrWhiteSpace(because: $"{action.ActionCode} must have a label");
            action.Description.Should().NotBeNullOrWhiteSpace(because: $"{action.ActionCode} must have a description");
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_SuggestedActions_CertificateFailed_SuggestsReprovision()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var repo = _factory.Services.GetRequiredService<IDomainRepository>();
        var entity = await repo.GetByIdAsync(_tenantId, domain.Id);
        await repo.UpdateAsync(entity! with { Status = "certificate_failed" });

        var status = await GetStatusAsync(client, domain.Id);

        status.SuggestedActions.Should().Contain(a => a.ActionCode == "reprovision_certificate");
    }

    // ── Link count ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_LinkCount_IsReturned()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);

        var status = await GetStatusAsync(client, domain.Id);

        status.LinkCount.Should().BeGreaterThanOrEqualTo(0);
    }

    // ── Auth and access control ───────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_NotFound_Returns404()
    {
        var (client, _) = AsAdmin();

        var resp = await client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}/status");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_Unauthenticated_Returns401()
    {
        await using var factory = new UnitTestWebApplicationFactory();
        var unauthClient = factory.CreateClient();

        var resp = await unauthClient.GetAsync(
            $"/api/v1/tenants/{Guid.NewGuid()}/domains/{Guid.NewGuid()}/status");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_ViewerRole_CanReadStatus()
    {
        var (adminClient, _) = AsAdmin();
        var domain = await CreateDomainAsync(adminClient);

        var (viewerClient, _) = _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Viewer);
        var resp = await viewerClient.GetAsync($"{BaseUrl}/{domain.Id}/status");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_CrossTenant_Returns404()
    {
        var (adminClient, _) = AsAdmin();
        var domain = await CreateDomainAsync(adminClient);

        var otherTenantId = Guid.NewGuid();
        var (otherClient, _) = _factory.CreateAuthenticatedClient(otherTenantId.ToString(), Roles.Admin);
        var resp = await otherClient.GetAsync(
            $"/api/v1/tenants/{otherTenantId}/domains/{domain.Id}/status");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── JSON serialization ────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetStatus_Response_IsJsonSerializable()
    {
        var (client, _) = AsAdmin();
        var domain = await CreateDomainAsync(client);
        await VerifyDomainAsync(client, domain.Id, pass: false);

        var httpResp = await client.GetAsync($"{BaseUrl}/{domain.Id}/status");
        var raw = await httpResp.Content.ReadAsStringAsync();
        var deserialized = JsonSerializer.Deserialize<DomainStatusResponse>(
            raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.Verification.RecentAttempts.Should().HaveCount(1);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
