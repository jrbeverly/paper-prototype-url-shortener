using System.Collections.Concurrent;

namespace ControlPlane.Api.Services;

public sealed class InMemoryApiKeyRepository : IApiKeyRepository
{
    private readonly ConcurrentDictionary<Guid, ApiKeyEntity> _keys = new();
    private readonly ConcurrentDictionary<string, ApiKeyEntity> _byHash = new();

    public Task<ApiKeyEntity> CreateAsync(ApiKeyEntity entity)
    {
        _keys[entity.Id] = entity;
        _byHash[entity.KeyHash] = entity;
        return Task.FromResult(entity);
    }

    public Task<List<ApiKeyEntity>> GetByTenantAsync(Guid tenantId)
    {
        var keys = _keys.Values
            .Where(k => k.TenantId == tenantId)
            .OrderByDescending(k => k.CreatedAt)
            .ToList();
        return Task.FromResult(keys);
    }

    public Task<ApiKeyEntity?> GetByIdAsync(Guid tenantId, Guid keyId)
    {
        _keys.TryGetValue(keyId, out var entity);
        if (entity is null || entity.TenantId != tenantId)
            return Task.FromResult<ApiKeyEntity?>(null);
        return Task.FromResult<ApiKeyEntity?>(entity);
    }

    public Task<ApiKeyEntity?> GetByHashAsync(string hash)
    {
        _byHash.TryGetValue(hash, out var entity);
        return Task.FromResult<ApiKeyEntity?>(entity);
    }

    public Task<bool> RevokeAsync(Guid tenantId, Guid keyId)
    {
        if (!_keys.TryGetValue(keyId, out var entity) || entity.TenantId != tenantId)
            return Task.FromResult(false);

        var revoked = entity with { IsRevoked = true };
        _keys.TryUpdate(keyId, revoked, entity);
        _byHash.TryUpdate(entity.KeyHash, revoked, entity);
        return Task.FromResult(true);
    }
}
