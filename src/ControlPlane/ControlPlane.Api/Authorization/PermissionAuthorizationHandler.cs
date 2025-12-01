using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Authorization;

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly ILogger<PermissionAuthorizationHandler> _logger;

    public PermissionAuthorizationHandler(ILogger<PermissionAuthorizationHandler> logger)
    {
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        // Check explicit permissions claim (granular permissions)
        var permissionsClaim = user.FindFirst("permissions")?.Value;
        if (!string.IsNullOrEmpty(permissionsClaim))
        {
            var permissions = permissionsClaim.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (permissions.Contains(requirement.Permission, StringComparer.OrdinalIgnoreCase))
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }
        }

        // Derive permissions from role claim
        var role = user.FindFirst(ClaimTypes.Role)?.Value
                ?? user.FindFirst("role")?.Value;

        if (!string.IsNullOrEmpty(role) && Roles.GetPermissions(role).Contains(requirement.Permission))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        _logger.LogWarning(
            "Authorization denied: missing permission '{Permission}'. User: {User}, Role: {Role}, Resource: {Resource}",
            requirement.Permission,
            user.FindFirst("sub")?.Value ?? user.FindFirst("key_id")?.Value ?? "unknown",
            role ?? "none",
            context.Resource?.ToString() ?? "unknown");

        return Task.CompletedTask;
    }
}
