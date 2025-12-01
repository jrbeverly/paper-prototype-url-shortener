using System.Security.Claims;

namespace ControlPlane.Api.Authentication;

public static class ClaimsPrincipalExtensions
{
    public static string? GetUserId(this ClaimsPrincipal principal)
    {
        return principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("user_id")?.Value
            ?? principal.FindFirst("sub")?.Value;
    }

    public static string? GetTenantId(this ClaimsPrincipal principal)
    {
        return principal.FindFirst("tenant_id")?.Value;
    }

    public static string? GetRole(this ClaimsPrincipal principal)
    {
        return principal.FindFirst(ClaimTypes.Role)?.Value
            ?? principal.FindFirst("role")?.Value;
    }

    public static string? GetAuthScheme(this ClaimsPrincipal principal)
    {
        return principal.FindFirst("auth_scheme")?.Value;
    }

    public static IReadOnlyList<string> GetPermissions(this ClaimsPrincipal principal)
    {
        return principal.FindFirst("permissions")?.Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? (IReadOnlyList<string>)Array.Empty<string>();
    }
}
