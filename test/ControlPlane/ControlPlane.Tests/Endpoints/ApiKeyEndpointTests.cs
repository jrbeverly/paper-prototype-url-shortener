using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;

namespace ControlPlane.Tests.Endpoints;

[Collection("DynamoDB")]
public sealed class ApiKeyEndpointTests : IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly Guid _tenantId = Guid.NewGuid();

    public ApiKeyEndpointTests(LocalStackFixture localStack)
    {
        _factory = new CustomWebApplicationFactory(localStack.DynamoDb, localStack.TableName);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateApiKey_WithAdminRole_Returns201WithFullKey()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var request = new CreateApiKeyRequest
        {
            Name = "My Test Key",
            Permissions = [Roles.Viewer]
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/api-keys", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();
        body.Should().NotBeNull();
        body!.Name.Should().Be("My Test Key");
        body.Key.Should().StartWith("sk_");
        body.KeyPrefix.Should().StartWith("sk_");
        body.Permissions.Should().Contain(Roles.Viewer);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListApiKeys_AfterCreate_ReturnsCreatedKey()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var createRequest = new CreateApiKeyRequest
        {
            Name = "List Test Key",
            Permissions = [Roles.Viewer]
        };

        var createResponse = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/api-keys", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var listResponse = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/api-keys");

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var keys = await listResponse.Content.ReadFromJsonAsync<List<ApiKeyListItemResponse>>();
        keys.Should().NotBeNull();
        keys!.Should().Contain(k => k.Id == created!.Id);
        keys!.First(k => k.Id == created!.Id).Name.Should().Be("List Test Key");
        keys!.First(k => k.Id == created!.Id).IsRevoked.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RevokeApiKey_WithAdminRole_Returns204()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/api-keys",
            new CreateApiKeyRequest { Name = "Revoke Test Key", Permissions = [Roles.Viewer] });
        var created = await createResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var revokeResponse = await client.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/api-keys/{created!.Id}");

        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RevokeNonExistentKey_Returns404()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var nonExistentId = Guid.NewGuid();

        var response = await client.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/api-keys/{nonExistentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RevokeAlreadyRevokedKey_Returns400()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/api-keys",
            new CreateApiKeyRequest { Name = "Double Revoke", Permissions = [Roles.Viewer] });
        var created = await createResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        await client.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/api-keys/{created!.Id}");

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var secondRevoke = await client.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/api-keys/{created.Id}");

        secondRevoke.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListApiKeys_EmptyTenant_ReturnsEmptyList()
    {
        var emptyTenantId = Guid.NewGuid();
        var (client, _) = _factory.CreateAuthenticatedClient(
            emptyTenantId.ToString(), Roles.Admin);

        var response = await client.GetAsync(
            $"/api/v1/tenants/{emptyTenantId}/api-keys");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var keys = await response.Content.ReadFromJsonAsync<List<ApiKeyListItemResponse>>();
        keys.Should().NotBeNull();
        keys!.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateApiKey_ResponseContainsLocationHeader()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/api-keys",
            new CreateApiKeyRequest { Name = "Location Test", Permissions = [Roles.Viewer] });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.OriginalString.Should()
            .Contain($"/tenants/{_tenantId}/api-keys/");
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
