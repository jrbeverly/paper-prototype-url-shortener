using System.Collections.Concurrent;

namespace ControlPlane.Api.Services;

public sealed class InMemoryDomainRepository : IDomainRepository
{
    private readonly ConcurrentDictionary<Guid, DomainEntity> _domains = new();

    public Task<DomainEntity> CreateAsync(DomainEntity entity)
    {
        _domains[entity.Id] = entity;
        return Task.FromResult(entity);
    }

    public Task<DomainEntity?> GetByHostnameAsync(Guid tenantId, string hostname)
    {
        var entity = _domains.Values
            .FirstOrDefault(d => d.TenantId == tenantId &&
                d.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase) &&
                d.DeletedAt is null);
        return Task.FromResult(entity);
    }

    public Task<DomainEntity?> GetByIdAsync(Guid tenantId, Guid domainId)
    {
        _domains.TryGetValue(domainId, out var entity);
        if (entity is null || entity.TenantId != tenantId || entity.DeletedAt is not null)
            return Task.FromResult<DomainEntity?>(null);
        return Task.FromResult<DomainEntity?>(entity);
    }

    public Task<List<DomainEntity>> GetByTenantAsync(Guid tenantId)
    {
        var domains = _domains.Values
            .Where(d => d.TenantId == tenantId && d.DeletedAt is null)
            .OrderByDescending(d => d.CreatedAt)
            .ToList();
        return Task.FromResult(domains);
    }

    public Task<int> GetCountByTenantAsync(Guid tenantId)
    {
        var count = _domains.Values.Count(d => d.TenantId == tenantId && d.DeletedAt is null);
        return Task.FromResult(count);
    }

    public Task<List<DomainEntity>> GetPendingVerificationAsync()
    {
        var domains = _domains.Values
            .Where(d => d.DeletedAt is null &&
                (d.Status == "pending_verification" || d.Status == "verifying"))
            .ToList();
        return Task.FromResult(domains);
    }

    public Task<(List<DomainEntity> Items, int TotalCount)> ListAsync(Guid tenantId, string? statusFilter, int page, int pageSize)
    {
        var query = _domains.Values
            .Where(d => d.TenantId == tenantId && d.DeletedAt is null);

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            query = query.Where(d => d.Status.Equals(statusFilter, StringComparison.OrdinalIgnoreCase));
        }

        var totalCount = query.Count();
        var items = query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult((items, totalCount));
    }

    public Task<DomainEntity?> UpdateAsync(DomainEntity entity)
    {
        if (!_domains.TryGetValue(entity.Id, out var existing))
            return Task.FromResult<DomainEntity?>(null);

        if (existing.DeletedAt is not null)
            return Task.FromResult<DomainEntity?>(null);

        _domains[entity.Id] = entity;
        return Task.FromResult<DomainEntity?>(entity);
    }

    public Task<DomainEntity?> SoftDeleteAsync(Guid tenantId, Guid domainId)
    {
        if (!_domains.TryGetValue(domainId, out var existing))
            return Task.FromResult<DomainEntity?>(null);

        if (existing.TenantId != tenantId || existing.DeletedAt is not null)
            return Task.FromResult<DomainEntity?>(null);

        var deleted = existing with { DeletedAt = DateTime.UtcNow };
        _domains[domainId] = deleted;
        return Task.FromResult<DomainEntity?>(deleted);
    }
}
