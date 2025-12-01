using System.Collections.Concurrent;

namespace ControlPlane.Api.Services;

public sealed class InMemoryTenantRepository : ITenantRepository
{
    private readonly ConcurrentDictionary<Guid, TenantEntity> _tenants = new();

    public Task<TenantEntity> CreateAsync(TenantEntity entity)
    {
        _tenants[entity.Id] = entity;
        return Task.FromResult(entity);
    }

    public Task<TenantEntity?> GetByIdAsync(Guid tenantId)
    {
        _tenants.TryGetValue(tenantId, out var entity);
        return Task.FromResult(entity);
    }

    public Task<TenantEntity?> GetByEmailAsync(string email)
    {
        var entity = _tenants.Values
            .FirstOrDefault(t => t.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(entity);
    }

    public Task<TenantEntity?> GetByStripeCustomerIdAsync(string stripeCustomerId)
    {
        var entity = _tenants.Values
            .FirstOrDefault(t => t.StripeCustomerId == stripeCustomerId);
        return Task.FromResult(entity);
    }

    public Task<TenantEntity> UpdateAsync(TenantEntity entity)
    {
        _tenants[entity.Id] = entity;
        return Task.FromResult(entity);
    }

    public Task<IReadOnlyList<TenantEntity>> GetExpiringTrialsAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        IReadOnlyList<TenantEntity> result = _tenants.Values
            .Where(t => t.Status == "trialing"
                        && t.TrialEndsAt.HasValue
                        && t.TrialEndsAt.Value >= from
                        && t.TrialEndsAt.Value < to)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<(IReadOnlyList<TenantEntity> Items, int TotalCount)> ListAsync(
        string? status = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var query = _tenants.Values.AsEnumerable();

        if (status is not null)
            query = query.Where(t => t.Status.Equals(status, StringComparison.OrdinalIgnoreCase));

        var ordered = query.OrderBy(t => t.CreatedAt).ToList();
        var totalCount = ordered.Count;
        IReadOnlyList<TenantEntity> items = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult((items, totalCount));
    }
}
