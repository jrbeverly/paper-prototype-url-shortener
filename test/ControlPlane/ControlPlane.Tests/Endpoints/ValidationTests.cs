using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;

namespace ControlPlane.Tests.Endpoints;


[Collection("DynamoDB")]
public sealed class ValidationTests : IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly Guid _tenantId = Guid.NewGuid();

    public ValidationTests(LocalStackFixture localStack)
    {
        _factory = new CustomWebApplicationFactory(localStack.DynamoDb, localStack.TableName);
    }

    private (HttpClient Client, TestClaimsProvider ClaimsProvider) SetupAdmin()
    {
        return _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Admin);
    }

    private async Task<Guid> CreateVerifiedDomainAsync(HttpClient client, TestClaimsProvider claimsProvider)
    {
        var hostname = $"val-{Guid.NewGuid():N}.example.com";
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(_tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = hostname });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(_tenantId.ToString(), Roles.Admin));
        await client.PostAsync($"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/verify", null);

        return created.Id;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateApiKey_WithEmptyName_Returns400WithFieldError()
    {
        var (client, _) = SetupAdmin();
        var request = new { Name = "", Permissions = new[] { Roles.Viewer } };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/api-keys", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.TryGetProperty("Name", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateApiKey_WithNameExceedingMaxLength_Returns400()
    {
        var (client, _) = SetupAdmin();
        var longName = new string('x', 101);
        var request = new { Name = longName, Permissions = new[] { Roles.Viewer } };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/api-keys", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateApiKey_WithEmptyPermissions_Returns400()
    {
        var (client, _) = SetupAdmin();
        var request = new { Name = "Test Key", Permissions = Array.Empty<string>() };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/api-keys", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("errors", out var errors).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateApiKey_WithInvalidPermission_Returns400()
    {
        var (client, _) = SetupAdmin();
        var request = new { Name = "Test Key", Permissions = new[] { "nonexistent_role" } };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/api-keys", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateApiKey_WithEmptyBody_Returns400()
    {
        var (client, _) = SetupAdmin();
        var content = new StringContent("", System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/api-keys", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateApiKey_WithInvalidTenantIdFormat_Returns404()
    {
        var (client, _) = SetupAdmin();
        var request = new { Name = "Test Key", Permissions = new[] { Roles.Viewer } };

        var response = await client.PostAsJsonAsync(
            "/api/v1/tenants/not-a-guid/api-keys", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── BulkCreateLinks ──────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BulkCreateLinks_WithEmptyLinksArray_Returns400WithFieldError()
    {
        var (client, _) = SetupAdmin();
        var request = new BulkCreateLinksRequest { Links = [] };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/bulk", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.TryGetProperty("Links", out _).Should().BeTrue();
    }

    // ── CreateLink ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateLink_WithInvalidDestinationUrl_Returns400WithFieldError()
    {
        var (client, claimsProvider) = SetupAdmin();
        var domainId = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(_tenantId.ToString(), Roles.Member));
        var request = new { DomainId = domainId, DestinationUrl = "not-a-url", RedirectType = "302" };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.TryGetProperty("DestinationUrl", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateLink_WithInvalidSlug_Returns400WithFieldError()
    {
        var (client, claimsProvider) = SetupAdmin();
        var domainId = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(_tenantId.ToString(), Roles.Member));
        var request = new { DomainId = domainId, DestinationUrl = "https://example.com", Slug = "-invalid-start-", RedirectType = "302" };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.TryGetProperty("Slug", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateLink_WithInvalidRedirectType_Returns400WithFieldError()
    {
        var (client, claimsProvider) = SetupAdmin();
        var domainId = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(_tenantId.ToString(), Roles.Member));
        var request = new { DomainId = domainId, DestinationUrl = "https://example.com", RedirectType = "303" };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.TryGetProperty("RedirectType", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateLink_WithInvalidDestinationUrl_Returns400WithFieldError()
    {
        var (client, claimsProvider) = SetupAdmin();
        var domainId = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(_tenantId.ToString(), Roles.Member));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links",
            new CreateLinkRequest { DomainId = domainId, DestinationUrl = "https://example.com" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<CreateLinkResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(_tenantId.ToString(), Roles.Member));
        var update = new { DestinationUrl = "not-a-url" };
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links/{created!.Id}", update);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.TryGetProperty("DestinationUrl", out _).Should().BeTrue();
    }

    // ── CreateDomain ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateDomain_WithInvalidHostname_Returns400WithFieldError()
    {
        var (client, _) = SetupAdmin();
        var request = new CreateDomainRequest { Hostname = "https://not-a-hostname.com/path" };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.TryGetProperty("Hostname", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateDomain_WithEmptyHostname_Returns400WithFieldError()
    {
        var (client, _) = SetupAdmin();
        var request = new CreateDomainRequest { Hostname = "" };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.TryGetProperty("Hostname", out _).Should().BeTrue();
    }

    // ── Consistent error format ───────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ValidationError_HasConsistentProblemDetailsFormat()
    {
        var (client, _) = SetupAdmin();
        var request = new { Name = "", Permissions = new[] { Roles.Viewer } };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/api-keys", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("type", out var type).Should().BeTrue();
        type.GetString().Should().Be("https://api.short.io/errors/validation-error");
        body.TryGetProperty("title", out var title).Should().BeTrue();
        title.GetString().Should().Be("Validation Error");
        body.TryGetProperty("status", out var status).Should().BeTrue();
        status.GetInt32().Should().Be(400);
        body.TryGetProperty("errors", out _).Should().BeTrue();
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
