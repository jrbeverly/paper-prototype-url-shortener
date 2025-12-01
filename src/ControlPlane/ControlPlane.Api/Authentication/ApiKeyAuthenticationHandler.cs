using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using ControlPlane.Api.Authorization;
using ControlPlane.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Authentication;

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "apikey";

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var options = Context.RequestServices.GetRequiredService<IOptionsMonitor<ApiKeyOptions>>();
        var headerName = options.CurrentValue.HeaderName;

        if (!Request.Headers.TryGetValue(headerName, out var headerValue))
            return AuthenticateResult.NoResult();

        var rawKey = headerValue.ToString();
        if (string.IsNullOrWhiteSpace(rawKey))
            return AuthenticateResult.NoResult();

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));

        var repository = Context.RequestServices.GetRequiredService<IApiKeyRepository>();
        var entity = await repository.GetByHashAsync(hash);

        if (entity is null)
            return AuthenticateResult.Fail("Invalid API key");

        if (entity.IsRevoked)
            return AuthenticateResult.Fail("API key has been revoked");

        var role = ResolveRole(entity.Permissions);

        var granularPermissions = Roles.GetPermissions(role);

        var claims = new List<Claim>
        {
            new("tenant_id", entity.TenantId.ToString()),
            new("key_id", entity.Id.ToString()),
            new("auth_scheme", SchemeName),
            new("permissions", string.Join(',', granularPermissions))
        };

        claims.Add(new Claim(ClaimTypes.Role, role));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }

    private static string ResolveRole(List<string> keyPermissions)
    {
        foreach (var perm in keyPermissions)
        {
            if (Roles.All.Contains(perm))
                return perm;
        }

        // Backward-compatible: old-style permissions map to new roles
        if (keyPermissions.Contains("admin", StringComparer.OrdinalIgnoreCase))
            return Roles.Admin;
        if (keyPermissions.Contains("write", StringComparer.OrdinalIgnoreCase))
            return Roles.Member;

        return Roles.Viewer;
    }
}
