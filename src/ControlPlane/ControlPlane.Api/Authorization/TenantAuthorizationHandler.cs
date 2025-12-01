using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace ControlPlane.Api.Authorization;

public sealed class TenantAuthorizationHandler : AuthorizationHandler<TenantAuthorizationRequirement>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<TenantAuthorizationHandler> _logger;

    public TenantAuthorizationHandler(IHttpContextAccessor httpContextAccessor, ILogger<TenantAuthorizationHandler> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantAuthorizationRequirement requirement)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
            return Task.CompletedTask;

        // Only enforce tenant scoping when the route includes a tenantId parameter
        if (!httpContext.Request.RouteValues.TryGetValue("tenantId", out var routeTenantId))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var routeValue = routeTenantId?.ToString();
        var userTenantId = context.User.FindFirst("tenant_id")?.Value;

        if (string.IsNullOrEmpty(userTenantId))
        {
            _logger.LogWarning("Tenant authorization failed: user has no tenant_id claim. Resource: {Resource}", httpContext.Request.Path);
            return Task.CompletedTask;
        }

        if (string.Equals(routeValue, userTenantId, StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var userId = context.User.FindFirst("sub")?.Value
                  ?? context.User.FindFirst("key_id")?.Value
                  ?? "unknown";

        _logger.LogWarning(
            "Cross-tenant access denied. UserId: {UserId}, UserTenant: {UserTenant}, RequestedTenant: {RequestedTenant}, Resource: {Resource}",
            userId,
            userTenantId,
            routeValue,
            httpContext.Request.Path);

        return Task.CompletedTask;
    }
}
