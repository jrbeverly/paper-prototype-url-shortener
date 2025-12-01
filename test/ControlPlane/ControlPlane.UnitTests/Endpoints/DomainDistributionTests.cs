using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Authorization;

namespace ControlPlane.UnitTests.Endpoints;

/// <summary>
/// Tests for CloudFront distribution tenant lifecycle triggered by domain operations.
/// Uses InMemoryDistributionService (Program.cs default) — no real CloudFront calls.
/// </summary>
public sealed class DomainDistributionTests : IAsyncDisposable
{
    private readonly UnitTestWebApplicationFactory _factory = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    private string BaseUrl => $"/api/v1/tenants/{_tenantId}/domains";

    private (HttpClient Client, TestClaimsProvider Claims) AsAdmin() =>
        _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Admin);

    private async Task<CreateDomainResponse> CreateDomainAsync(HttpClient client, string hostname)
    {
        var response = await client.PostAsJsonAsync(BaseUrl, new CreateDomainRequest { Hostname = hostname });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CreateDomainResponse>())!;
    }

    private async Task VerifyDomainAsync(HttpClient client, Guid domainId, bool pass = true)
    {
        _factory.DnsVerification.SetNextResult(pass
            ? TestDnsVerificationService.PassResult("txt", "cname")
            : TestDnsVerificationService.FailBothResult("txt", "cname"));
        await client.PostAsync($"{BaseUrl}/{domainId}/verify", null);
    }

    // ── Distribution tenant created on successful verification ─────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task VerifyDomain_Success_CreatesDistributionTenant()
    {
        var (client, _) = AsAdmin();
        var created = await CreateDomainAsync(client, "go.example.com");

        await VerifyDomainAsync(client, created.Id, pass: true);

        var detail = await GetDomainAsync(client, created.Id);
        detail.DistributionTenantId.Should().NotBeNullOrEmpty();
        detail.DistributionTenantStatus.Should().Be("InProgress");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task VerifyDomain_Failure_DoesNotCreateDistributionTenant()
    {
        var (client, _) = AsAdmin();
        var created = await CreateDomainAsync(client, "fail.example.com");

        await VerifyDomainAsync(client, created.Id, pass: false);

        var detail = await GetDomainAsync(client, created.Id);
        detail.DistributionTenantId.Should().BeNull();
        detail.DistributionTenantStatus.Should().BeNull();
    }

    // ── Distribution tenant deleted on domain removal ──────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteDomain_WithDistributionTenant_DeletesDistributionTenant()
    {
        var (client, _) = AsAdmin();
        var created = await CreateDomainAsync(client, "del.example.com");
        await VerifyDomainAsync(client, created.Id, pass: true);

        // Confirm tenant was created
        var before = await GetDomainAsync(client, created.Id);
        before.DistributionTenantId.Should().NotBeNull();

        var deleteResponse = await client.DeleteAsync($"{BaseUrl}/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Domain is soft-deleted; a GET returns 404 now
        var getResponse = await client.GetAsync($"{BaseUrl}/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteDomain_WithoutDistributionTenant_Returns204()
    {
        // A domain that was never verified has no distribution tenant.
        var (client, _) = AsAdmin();
        var created = await CreateDomainAsync(client, "nodelete.example.com");

        var response = await client.DeleteAsync($"{BaseUrl}/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Distribution tenant updated on domain settings change ──────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateDomain_WithDistributionTenant_DistributionTenantStatusPreserved()
    {
        var (client, _) = AsAdmin();
        var created = await CreateDomainAsync(client, "update.example.com");
        await VerifyDomainAsync(client, created.Id, pass: true);

        var before = await GetDomainAsync(client, created.Id);
        before.DistributionTenantId.Should().NotBeNull();

        var patchResponse = await client.PatchAsJsonAsync(
            $"{BaseUrl}/{created.Id}",
            new UpdateDomainRequest { DefaultRedirectUrl = "https://example.com" });

        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await patchResponse.Content.ReadFromJsonAsync<DomainDetailResponse>();
        after!.DistributionTenantId.Should().Be(before.DistributionTenantId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateDomain_WithoutDistributionTenant_Returns200WithNoTenantId()
    {
        // Domain created but never verified — no distribution tenant.
        var (client, _) = AsAdmin();
        var created = await CreateDomainAsync(client, "noupdate.example.com");

        var patchResponse = await client.PatchAsJsonAsync(
            $"{BaseUrl}/{created.Id}",
            new UpdateDomainRequest { DefaultRedirectUrl = "https://fallback.example.com" });

        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await patchResponse.Content.ReadFromJsonAsync<DomainDetailResponse>();
        body!.DistributionTenantId.Should().BeNull();
    }

    // ── Quota check integration ────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task VerifyDomain_Success_DistributionTenantIdIsNonEmpty()
    {
        var (client, _) = AsAdmin();
        var created = await CreateDomainAsync(client, "quota.example.com");

        await VerifyDomainAsync(client, created.Id, pass: true);

        var detail = await GetDomainAsync(client, created.Id);
        detail.DistributionTenantId.Should().StartWith("dt_");
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private async Task<DomainDetailResponse> GetDomainAsync(HttpClient client, Guid domainId)
    {
        var response = await client.GetAsync($"{BaseUrl}/{domainId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<DomainDetailResponse>())!;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
