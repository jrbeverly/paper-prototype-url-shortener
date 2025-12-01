using System.Security.Claims;
using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;

namespace ControlPlane.Tests.Infrastructure;

internal static class TestData
{
    public static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static CreateApiKeyRequest ValidCreateRequest(string? name = null) => new()
    {
        Name = name ?? "Test API Key",
        Permissions = [Roles.Viewer]
    };

    public static CreateApiKeyRequest CreateRequestWithRole(string role) => new()
    {
        Name = "Test API Key",
        Permissions = [role]
    };

    public static (HttpClient Client, TestClaimsProvider ClaimsProvider) CreateAuthenticatedClient(
        this CustomWebApplicationFactory factory,
        string tenantId,
        string role)
    {
        var claimsProvider = factory.Services.GetRequiredService<TestClaimsProvider>();
        var principal = TestClaimsProvider.CreatePrincipal(tenantId, role);
        claimsProvider.SetClaims(principal);
        var client = factory.CreateClient();
        return (client, claimsProvider);
    }

    public static StringContent ToJsonContent<T>(this T obj)
    {
        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        return new StringContent(json, System.Text.Encoding.UTF8, "application/json");
    }
}
