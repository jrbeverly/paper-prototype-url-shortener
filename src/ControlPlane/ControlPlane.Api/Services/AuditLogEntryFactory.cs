using System.Security.Claims;
using System.Text.Json;
using ControlPlane.Api.Authentication;

namespace ControlPlane.Api.Services;

/// <summary>
/// Builds AuditLogEntry instances from request context. Centralises actor extraction
/// (user ID, auth scheme, IP, user-agent) so endpoint handlers stay concise.
/// </summary>
internal static class AuditLogEntryFactory
{
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);

    /// <summary>Creates a fully-populated AuditLogEntry from the current request context.</summary>
    internal static AuditLogEntry Create(
        Guid tenantId,
        string action,
        string resourceType,
        string resourceId,
        ClaimsPrincipal actor,
        HttpContext? httpContext = null,
        string? oldValue = null,
        string? newValue = null,
        string? details = null) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Action = action,
            ActorId = actor.GetUserId() ?? "unknown",
            ActorType = actor.GetAuthScheme() == ApiKeyAuthenticationHandler.SchemeName ? "api_key" : "user",
            ResourceType = resourceType,
            ResourceId = resourceId,
            OldValue = oldValue,
            NewValue = newValue,
            Details = details,
            IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
            UserAgent = httpContext?.Request.Headers.UserAgent.ToString(),
            Timestamp = DateTime.UtcNow
        };

    /// <summary>Serialises a value to a compact JSON string for use as OldValue/NewValue.</summary>
    internal static string Snapshot<T>(T value) => JsonSerializer.Serialize(value, _opts);
}
