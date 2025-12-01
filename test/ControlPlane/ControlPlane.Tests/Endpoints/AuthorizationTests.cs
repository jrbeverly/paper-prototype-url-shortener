using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;

namespace ControlPlane.Tests.Endpoints;

[Collection("DynamoDB")]
public sealed class AuthorizationTests : IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly Guid _tenantId = Guid.NewGuid();

    public AuthorizationTests(LocalStackFixture localStack)
    {
        _factory = new CustomWebApplicationFactory(localStack.DynamoDb, localStack.TableName);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateApiKey_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var request = new CreateApiKeyRequest
        {
            Name = "Unauthorized",
            Permissions = [Roles.Viewer]
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/api-keys", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListApiKeys_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/v1/tenants/{_tenantId}/api-keys");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RevokeApiKey_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.DeleteAsync(
            $"/api/v1/tenants/{_tenantId}/api-keys/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(Roles.Viewer)]
    [InlineData(Roles.Member)]
    [Trait("Category", "Integration")]
    public async Task CreateApiKey_WithNonAdminRole_Returns403(string role)
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), role);
        var request = new CreateApiKeyRequest
        {
            Name = "Forbidden",
            Permissions = [Roles.Viewer]
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/api-keys", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Owner)]
    [Trait("Category", "Integration")]
    public async Task CreateApiKey_WithAdminOrOwnerRole_Returns201(string role)
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), role);
        var request = new CreateApiKeyRequest
        {
            Name = "Allowed",
            Permissions = [Roles.Viewer]
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/api-keys", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CrossTenantAccess_ListKeys_Returns403()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            tenantA.ToString(), Roles.Admin);

        // Create a key in tenantA
        await client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantA}/api-keys",
            new CreateApiKeyRequest { Name = "Cross-tenant test", Permissions = [Roles.Viewer] });

        // Authenticate as tenantB, try to list tenantA's keys
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            tenantB.ToString(), Roles.Admin));
        var listResponse = await client.GetAsync(
            $"/api/v1/tenants/{tenantA}/api-keys");

        listResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CrossTenantAccess_RevokeKey_Returns403()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            tenantA.ToString(), Roles.Admin);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantA}/api-keys",
            new CreateApiKeyRequest { Name = "Cross-tenant revoke", Permissions = [Roles.Viewer] });
        var created = await createResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        // Authenticate as tenantB, try to revoke tenantA's key
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            tenantB.ToString(), Roles.Admin));
        var revokeResponse = await client.DeleteAsync(
            $"/api/v1/tenants/{tenantA}/api-keys/{created!.Id}");

        revokeResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SameTenantAccess_Succeeds()
    {
        var tenantA = Guid.NewGuid();

        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            tenantA.ToString(), Roles.Admin);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{tenantA}/api-keys",
            new CreateApiKeyRequest { Name = "Same-tenant", Permissions = [Roles.Viewer] });
        var created = await createResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            tenantA.ToString(), Roles.Admin));
        var listResponse = await client.GetAsync(
            $"/api/v1/tenants/{tenantA}/api-keys");

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var keys = await listResponse.Content.ReadFromJsonAsync<List<ApiKeyListItemResponse>>();
        keys!.Should().Contain(k => k.Id == created!.Id);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
