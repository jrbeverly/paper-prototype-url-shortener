using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Common.ErrorHandling;
using ControlPlane.Api.Extensions;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;

namespace ControlPlane.Api.Endpoints.ApiKeys;

/// <summary>API key management endpoints for programmatic access.</summary>
public class ApiKeyEndpoints : IEndpointGroup
{
    private const string _keyPrefix = "sk_";

    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/tenants/{tenantId:guid}/api-keys")
            .WithTags("API Keys");

        group.MapPost("/", CreateApiKey)
            .WithName("CreateApiKey")
            .WithOpenApi()
            .WithDescription("Generate a new API key for programmatic access. The full key value is returned only in this response.")
            .RequireAuthorization("apikey:manage")
            .WithValidation<CreateApiKeyRequest>()
            .Produces<ApiKeyCreatedResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/", ListApiKeys)
            .WithName("ListApiKeys")
            .WithOpenApi()
            .WithDescription("List all API keys for a tenant. Key values are masked.")
            .RequireAuthorization("apikey:manage")
            .Produces<List<ApiKeyListItemResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{keyId:guid}", RevokeApiKey)
            .WithName("RevokeApiKey")
            .WithOpenApi()
            .WithDescription("Revoke an API key. Revoked keys are immediately invalid.")
            .RequireAuthorization("apikey:manage")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);
    }

    /// <summary>Generates a new API key for the tenant.</summary>
    private static async Task<IResult> CreateApiKey(
        Guid tenantId,
        CreateApiKeyRequest request,
        IApiKeyRepository repository,
        IAuditLogService auditLog,
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        var (fullKey, keyHash, prefix) = GenerateKey();

        var entity = new ApiKeyEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name,
            KeyHash = keyHash,
            KeyPrefix = prefix,
            CreatedAt = DateTime.UtcNow,
            LastUsedAt = null,
            Permissions = request.Permissions,
            IsRevoked = false
        };

        await repository.CreateAsync(entity);

        await auditLog.LogAsync(AuditLogEntryFactory.Create(
            tenantId,
            action: "api_key.created",
            resourceType: "api_key",
            resourceId: entity.Id.ToString(),
            actor: user,
            httpContext: httpContext,
            newValue: AuditLogEntryFactory.Snapshot(new
            {
                name = entity.Name,
                keyPrefix = entity.KeyPrefix,
                permissions = entity.Permissions
            })));

        var response = new ApiKeyCreatedResponse
        {
            Id = entity.Id,
            Name = entity.Name,
            Key = fullKey,
            KeyPrefix = entity.KeyPrefix,
            CreatedAt = entity.CreatedAt,
            Permissions = entity.Permissions
        };

        return Results.Created($"/tenants/{tenantId}/api-keys/{entity.Id}", response);
    }

    /// <summary>Lists all API keys for a tenant with masked key values.</summary>
    private static async Task<IResult> ListApiKeys(
        Guid tenantId,
        IApiKeyRepository repository)
    {
        var keys = await repository.GetByTenantAsync(tenantId);

        var response = keys.Select(k => new ApiKeyListItemResponse
        {
            Id = k.Id,
            Name = k.Name,
            KeyPreview = MaskKey(k.KeyPrefix),
            KeyPrefix = k.KeyPrefix,
            CreatedAt = k.CreatedAt,
            LastUsedAt = k.LastUsedAt,
            Permissions = k.Permissions,
            IsRevoked = k.IsRevoked
        }).ToList();

        return Results.Ok(response);
    }

    /// <summary>Revokes an API key, immediately invalidating it.</summary>
    private static async Task<IResult> RevokeApiKey(
        Guid tenantId,
        Guid keyId,
        IApiKeyRepository repository,
        IAuditLogService auditLog,
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        var key = await repository.GetByIdAsync(tenantId, keyId);
        if (key is null)
            throw new NotFoundException("API Key", keyId.ToString());

        if (key.IsRevoked)
            throw new BadRequestException("API key is already revoked");

        var revoked = await repository.RevokeAsync(tenantId, keyId);
        if (!revoked)
            throw new BadRequestException("API key is already revoked");

        await auditLog.LogAsync(AuditLogEntryFactory.Create(
            tenantId,
            action: "api_key.revoked",
            resourceType: "api_key",
            resourceId: keyId.ToString(),
            actor: user,
            httpContext: httpContext,
            oldValue: AuditLogEntryFactory.Snapshot(new { name = key.Name, isRevoked = false }),
            newValue: AuditLogEntryFactory.Snapshot(new { name = key.Name, isRevoked = true })));

        return Results.NoContent();
    }

    private static (string FullKey, string Hash, string Prefix) GenerateKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var encoded = Convert.ToBase64String(bytes)
            .Replace('+', '0')
            .Replace('/', '1')
            .Replace("=", "");
        var fullKey = _keyPrefix + encoded;
        var prefix = _keyPrefix + encoded[..8];
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullKey)));
        return (fullKey, hash, prefix);
    }

    private static string MaskKey(string prefix)
    {
        return $"{prefix}****...****{prefix[^4..]}";
    }
}
