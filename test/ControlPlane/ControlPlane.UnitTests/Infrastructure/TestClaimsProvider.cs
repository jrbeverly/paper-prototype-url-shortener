using System.Security.Claims;
using ControlPlane.Api.Authorization;

namespace ControlPlane.UnitTests.Infrastructure;

public sealed class TestClaimsProvider
{
    private ClaimsPrincipal? _principal;

    public void SetClaims(ClaimsPrincipal principal) => _principal = principal;

    public ClaimsPrincipal? GetCurrent() => _principal;

    public static ClaimsPrincipal CreatePrincipal(
        string tenantId,
        string role,
        string? keyId = null,
        IEnumerable<string>? additionalPermissions = null)
    {
        var permissions = new HashSet<string>(Roles.GetPermissions(role), StringComparer.OrdinalIgnoreCase);
        if (additionalPermissions is not null)
            foreach (var p in additionalPermissions) permissions.Add(p);

        var claims = new List<Claim>
        {
            new("tenant_id", tenantId),
            new("permissions", string.Join(',', permissions)),
            new(ClaimTypes.Role, role),
            new("auth_scheme", "test"),
            new("sub", keyId ?? Guid.NewGuid().ToString())
        };

        if (keyId is not null)
            claims.Add(new Claim("key_id", keyId));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestScheme"));
    }
}
