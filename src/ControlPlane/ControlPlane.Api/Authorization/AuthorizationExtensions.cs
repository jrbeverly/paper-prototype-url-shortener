using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using ControlPlane.Api.Authentication;

namespace ControlPlane.Api.Authorization;

public static class AuthorizationExtensions
{
    public static IServiceCollection AddAuthorizationPolicies(this IServiceCollection services, bool apiKeysEnabled)
    {
        // ── Handlers ─────────────────────────────────────────────────────
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, TenantAuthorizationHandler>();

        // ── Policies ─────────────────────────────────────────────────────
        services.AddAuthorization(options =>
        {
            var schemes = new List<string> { JwtBearerDefaults.AuthenticationScheme };
            if (apiKeysEnabled)
                schemes.Add(ApiKeyAuthenticationHandler.SchemeName);

            var schemesArray = schemes.ToArray();

            // Default policy: authenticated + tenant-scoped
            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes(schemesArray)
                .RequireAuthenticatedUser()
                .AddRequirements(new TenantAuthorizationRequirement())
                .Build();

            // Coarse-grained role policies (backward-compatible with existing code)
            options.AddPolicy("admin", policy => policy
                .AddAuthenticationSchemes(schemesArray)
                .AddRequirements(new PermissionRequirement(Permissions.ApiKeyManage))
                .AddRequirements(new TenantAuthorizationRequirement()));

            options.AddPolicy("write", policy => policy
                .AddAuthenticationSchemes(schemesArray)
                .AddRequirements(new PermissionRequirement(Permissions.LinkWrite))
                .AddRequirements(new TenantAuthorizationRequirement()));

            // Fine-grained permission policies
            AddPermissionPolicy(options, "tenant:read", Permissions.TenantRead, schemesArray);
            AddPermissionPolicy(options, "tenant:write", Permissions.TenantWrite, schemesArray);
            AddPermissionPolicy(options, "domain:read", Permissions.DomainRead, schemesArray);
            AddPermissionPolicy(options, "domain:write", Permissions.DomainWrite, schemesArray);
            AddPermissionPolicy(options, "link:read", Permissions.LinkRead, schemesArray);
            AddPermissionPolicy(options, "link:write", Permissions.LinkWrite, schemesArray);
            AddPermissionPolicy(options, "billing:read", Permissions.BillingRead, schemesArray);
            AddPermissionPolicy(options, "billing:write", Permissions.BillingWrite, schemesArray);
            AddPermissionPolicy(options, "apikey:manage", Permissions.ApiKeyManage, schemesArray);
            AddPermissionPolicy(options, "flag:read", Permissions.FlagRead, schemesArray);
            AddPermissionPolicy(options, "flag:write", Permissions.FlagWrite, schemesArray);
            AddPermissionPolicy(options, "audit:read", Permissions.AuditRead, schemesArray);
        });

        return services;
    }

    private static void AddPermissionPolicy(AuthorizationOptions options, string name, string permission, string[] schemes)
    {
        options.AddPolicy(name, policy => policy
            .AddAuthenticationSchemes(schemes)
            .AddRequirements(new PermissionRequirement(permission))
            .AddRequirements(new TenantAuthorizationRequirement()));
    }
}
