using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;

namespace ControlPlane.UnitTests.Endpoints;

public sealed class ApiKeyEndpointTests : IAsyncDisposable
{
    private readonly UnitTestWebApplicationFactory _factory = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    private string BaseUrl => $"/api/v1/tenants/{_tenantId}/api-keys";

    private (HttpClient Client, TestClaimsProvider Claims) AsAdmin() =>
        _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Admin);

    // ── CreateApiKey ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateApiKey_ValidRequest_Returns201WithFullKey()
    {
        var (client, _) = AsAdmin();
        var request = new CreateApiKeyRequest { Name = "CI Key", Permissions = [Roles.Viewer] };

        var response = await client.PostAsJsonAsync(BaseUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();
        body.Should().NotBeNull();
        body!.Id.Should().NotBeEmpty();
        body.Name.Should().Be("CI Key");
        body.Key.Should().StartWith("sk_");
        body.Key.Length.Should().BeGreaterThan(10);
        body.Permissions.Should().Contain(Roles.Viewer);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateApiKey_ResponseHasLocationHeader()
    {
        var (client, _) = AsAdmin();
        var request = new CreateApiKeyRequest { Name = "Header Key", Permissions = [Roles.Viewer] };

        var response = await client.PostAsJsonAsync(BaseUrl, request);

        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.OriginalString.Should().Contain("/api-keys/");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateApiKey_EmptyName_Returns400()
    {
        var (client, _) = AsAdmin();
        var request = new CreateApiKeyRequest { Name = "", Permissions = [Roles.Viewer] };

        var response = await client.PostAsJsonAsync(BaseUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateApiKey_InvalidPermission_Returns400()
    {
        var (client, _) = AsAdmin();
        var request = new CreateApiKeyRequest { Name = "Bad Key", Permissions = ["godmode"] };

        var response = await client.PostAsJsonAsync(BaseUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── ListApiKeys ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListApiKeys_EmptyTenant_ReturnsEmptyList()
    {
        var (client, _) = AsAdmin();

        var response = await client.GetAsync(BaseUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<ApiKeyListItemResponse>>();
        body.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListApiKeys_AfterCreation_ReturnsMaskedKey()
    {
        var (client, _) = AsAdmin();
        await client.PostAsJsonAsync(BaseUrl,
            new CreateApiKeyRequest { Name = "Listed Key", Permissions = [Roles.Viewer] });

        var response = await client.GetAsync(BaseUrl);

        var body = await response.Content.ReadFromJsonAsync<List<ApiKeyListItemResponse>>();
        body.Should().HaveCount(1);
        body![0].Name.Should().Be("Listed Key");
        body[0].KeyPreview.Should().NotBeNullOrEmpty();
        // Full key must not be present in list response
        body[0].Should().NotBeOfType<ApiKeyCreatedResponse>();
    }

    // ── RevokeApiKey ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RevokeApiKey_ExistingKey_Returns204()
    {
        var (client, _) = AsAdmin();
        var created = await (await client.PostAsJsonAsync(BaseUrl,
            new CreateApiKeyRequest { Name = "Revoke Me", Permissions = [Roles.Viewer] }))
            .Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        var response = await client.DeleteAsync($"{BaseUrl}/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RevokeApiKey_AlreadyRevoked_Returns400()
    {
        var (client, _) = AsAdmin();
        var created = await (await client.PostAsJsonAsync(BaseUrl,
            new CreateApiKeyRequest { Name = "Double Revoke", Permissions = [Roles.Viewer] }))
            .Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        await client.DeleteAsync($"{BaseUrl}/{created!.Id}");
        var response = await client.DeleteAsync($"{BaseUrl}/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RevokeApiKey_NotFound_Returns404()
    {
        var (client, _) = AsAdmin();

        var response = await client.DeleteAsync($"{BaseUrl}/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
