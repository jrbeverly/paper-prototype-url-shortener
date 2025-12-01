using ControlPlane.Api.Models.Responses;

namespace ControlPlane.Api.Services;

public interface IApiKeyRepository
{
    Task<ApiKeyEntity> CreateAsync(ApiKeyEntity entity);
    Task<List<ApiKeyEntity>> GetByTenantAsync(Guid tenantId);
    Task<ApiKeyEntity?> GetByIdAsync(Guid tenantId, Guid keyId);
    Task<ApiKeyEntity?> GetByHashAsync(string hash);
    Task<bool> RevokeAsync(Guid tenantId, Guid keyId);
}

public sealed record ApiKeyEntity
{
    public required Guid Id { get; init; }
    public required Guid TenantId { get; init; }
    public required string Name { get; init; }
    public required string KeyHash { get; init; }
    public required string KeyPrefix { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? LastUsedAt { get; init; }
    public required List<string> Permissions { get; init; }
    public required bool IsRevoked { get; init; }
}
