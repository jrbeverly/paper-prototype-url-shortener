using System.Collections.Concurrent;

namespace ControlPlane.Api.Services;

public sealed class InMemoryAuditLogService : IAuditLogService
{
    private readonly ConcurrentBag<AuditLogEntry> _entries = new();

    public Task LogAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<AuditLogPage> QueryAsync(
        Guid tenantId,
        string? action = null,
        string? resourceType = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = _entries.Where(e => e.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(e => e.Action.StartsWith(action, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(resourceType))
            query = query.Where(e => e.ResourceType.Equals(resourceType, StringComparison.OrdinalIgnoreCase));

        if (from.HasValue)
            query = query.Where(e => e.Timestamp >= from.Value);

        if (to.HasValue)
            query = query.Where(e => e.Timestamp <= to.Value);

        var ordered = query.OrderByDescending(e => e.Timestamp).ToList();
        var total = ordered.Count;
        var items = (IReadOnlyList<AuditLogEntry>)ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult(new AuditLogPage
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }
}
