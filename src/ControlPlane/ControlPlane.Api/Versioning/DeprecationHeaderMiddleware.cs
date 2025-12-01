using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Versioning;

/// <summary>
/// Adds <c>Sunset</c> and <c>Deprecation</c> HTTP headers to responses
/// for deprecated API versions, per RFC 8594.
/// </summary>
public sealed class DeprecationHeaderMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ApiVersionConfig _config;

    public DeprecationHeaderMiddleware(RequestDelegate next, IOptions<ApiVersionConfig> config)
    {
        _next = next;
        _config = config.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var version = ResolveVersion(context.Request.Path);

        if (version is not null && _config.DeprecatedVersions.TryGetValue(version.Value, out var info))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers["Deprecation"] = info.Message;
                context.Response.Headers["Sunset"] = info.SunsetDate;
                return Task.CompletedTask;
            });
        }

        await _next(context);
    }

    private static int? ResolveVersion(PathString path)
    {
        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments is ["api", var v, ..] &&
            v.StartsWith('v') &&
            int.TryParse(v[1..], out var version))
        {
            return version;
        }
        return null;
    }
}
