using System.Collections.Concurrent;

namespace ControlPlane.Api.Services;

public sealed class InMemoryFeatureFlagRepository : IFeatureFlagRepository
{
    private readonly ConcurrentDictionary<string, FeatureFlagEntity> _flags =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<FeatureFlagEntity?> GetByKeyAsync(string key, CancellationToken ct = default)
    {
        _flags.TryGetValue(key, out var flag);
        return Task.FromResult(flag);
    }

    public Task<IReadOnlyList<FeatureFlagEntity>> ListAsync(CancellationToken ct = default)
    {
        var list = _flags.Values
            .OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
            .ToList() as IReadOnlyList<FeatureFlagEntity>;
        return Task.FromResult(list);
    }

    public Task<FeatureFlagEntity> CreateAsync(FeatureFlagEntity entity, CancellationToken ct = default)
    {
        _flags[entity.Key] = entity;
        return Task.FromResult(entity);
    }

    public Task<FeatureFlagEntity?> UpdateAsync(
        string key,
        FeatureFlagUpdate update,
        FlagAuditEntry auditEntry,
        CancellationToken ct = default)
    {
        if (!_flags.TryGetValue(key, out var existing))
            return Task.FromResult<FeatureFlagEntity?>(null);

        var updated = existing with
        {
            Name = update.Name ?? existing.Name,
            Description = update.Description ?? existing.Description,
            Enabled = update.Enabled ?? existing.Enabled,
            FlagType = update.FlagType ?? existing.FlagType,
            RolloutPercentage = update.RolloutPercentage ?? existing.RolloutPercentage,
            EnabledTenantIds = update.EnabledTenantIds ?? existing.EnabledTenantIds,
            EnabledPlanIds = update.EnabledPlanIds ?? existing.EnabledPlanIds,
            UpdatedBy = auditEntry.PerformedBy,
            UpdatedAt = auditEntry.Timestamp,
            AuditTrail = [.. existing.AuditTrail, auditEntry]
        };

        _flags[key] = updated;
        return Task.FromResult<FeatureFlagEntity?>(updated);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        var removed = _flags.TryRemove(key, out _);
        return Task.FromResult(removed);
    }
}
