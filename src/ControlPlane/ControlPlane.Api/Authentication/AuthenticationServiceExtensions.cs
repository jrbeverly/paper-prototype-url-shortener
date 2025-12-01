using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ControlPlane.Api.Authorization;

namespace ControlPlane.Api.Authentication;

public static class AuthenticationServiceExtensions
{
    public static IServiceCollection AddAppAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtOptions = configuration
            .GetSection(JwtOptions.SectionName)
            .Get<JwtOptions>() ?? new JwtOptions();

        var apiKeyOptions = configuration
            .GetSection(ApiKeyOptions.SectionName)
            .Get<ApiKeyOptions>() ?? new ApiKeyOptions();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<ApiKeyOptions>(configuration.GetSection(ApiKeyOptions.SectionName));

        services.AddHttpContextAccessor();
        services.AddScoped<CurrentUserContext>();

        var authBuilder = services.AddAuthentication();

        // ── JWT Bearer ─────────────────────────────────────────────────
        authBuilder.AddJwtBearer(options =>
        {
            ConfigureJwtBearer(options, jwtOptions);
        });

        // ── API Key ────────────────────────────────────────────────────
        if (apiKeyOptions.Enabled)
        {
            authBuilder.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.SchemeName,
                null);
        }

        // ── Authorization (RBAC policies) ──────────────────────────────
        services.AddAuthorizationPolicies(apiKeyOptions.Enabled);

        return services;
    }

    private static void ConfigureJwtBearer(JwtBearerOptions options, JwtOptions jwtOptions)
    {
        options.RequireHttpsMetadata = true;
        options.SaveToken = true;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.ValidIssuer,

            ValidateAudience = true,
            ValidAudience = jwtOptions.ValidAudience,

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),

            ValidateIssuerSigningKey = true,
            NameClaimType = "sub",
            RoleClaimType = "role"
        };

        if (!string.IsNullOrEmpty(jwtOptions.IssuerSigningKey))
        {
            options.TokenValidationParameters.IssuerSigningKey =
                new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtOptions.IssuerSigningKey));
        }

        // When Authority is set, the middleware automatically fetches and caches JWKS.
        // ConfigurationManager<OpenIdConnectConfiguration> handles caching with
        // default AutomaticRefreshInterval of 5 days and RefreshInterval of 24h.
        if (!string.IsNullOrEmpty(jwtOptions.Authority))
        {
            options.Authority = jwtOptions.Authority;
        }

        // ── Custom challenge to return detailed 401 ────────────────────
        options.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/problem+json";

                string title;
                string detail;
                string type;

                if (context.AuthenticateFailure is SecurityTokenExpiredException)
                {
                    title = "Token Expired";
                    detail = "The access token has expired.";
                    type = "https://api.short.io/errors/token-expired";
                }
                else
                {
                    title = "Unauthorized";
                    detail = context.AuthenticateFailure?.Message
                        ?? context.ErrorDescription
                        ?? "A valid authentication token is required.";
                    type = "https://api.short.io/errors/unauthorized";
                }

                var payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    type,
                    title,
                    status = 401,
                    detail
                });

                return context.Response.WriteAsync(payload);
            },
            OnForbidden = context =>
            {
                context.Response.StatusCode = 403;
                context.Response.ContentType = "application/problem+json";

                var payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    type = "https://api.short.io/errors/forbidden",
                    title = "Forbidden",
                    status = 403,
                    detail = "Insufficient permissions to access this resource."
                });

                return context.Response.WriteAsync(payload);
            }
        };
    }
}
