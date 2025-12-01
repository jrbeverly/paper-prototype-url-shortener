using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Text;
using ControlPlane.Api.Models.Requests;

namespace ControlPlane.Api.Services;

public sealed class InMemoryLinkRepository : ILinkRepository
{
    private readonly ConcurrentDictionary<Guid, LinkEntity> _links = new();

    public Task<LinkEntity> CreateAsync(LinkEntity entity)
    {
        _links[entity.Id] = entity;
        return Task.FromResult(entity);
    }

    public Task<LinkEntity?> GetByIdAsync(Guid tenantId, Guid linkId)
    {
        _links.TryGetValue(linkId, out var entity);
        if (entity is null || entity.TenantId != tenantId || entity.DeletedAt is not null)
            return Task.FromResult<LinkEntity?>(null);
        return Task.FromResult<LinkEntity?>(entity);
    }

    public Task<LinkEntity?> GetByDomainAndSlugAsync(Guid tenantId, Guid domainId, string slug)
    {
        var entity = _links.Values
            .FirstOrDefault(l => l.TenantId == tenantId &&
                l.DomainId == domainId &&
                l.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase) &&
                l.DeletedAt is null);
        return Task.FromResult(entity);
    }

    public Task<int> GetCountByTenantAsync(Guid tenantId)
    {
        var count = _links.Values.Count(l => l.TenantId == tenantId && l.DeletedAt is null);
        return Task.FromResult(count);
    }

    public Task<int> GetCountByDomainAsync(Guid tenantId, Guid domainId)
    {
        var count = _links.Values.Count(l => l.TenantId == tenantId && l.DomainId == domainId && l.DeletedAt is null);
        return Task.FromResult(count);
    }

    public Task<IReadOnlyList<LinkEntity>> GetAllByTenantAsync(Guid tenantId)
    {
        var links = _links.Values
            .Where(l => l.TenantId == tenantId && l.DeletedAt is null)
            .OrderByDescending(l => l.CreatedAt)
            .ToList() as IReadOnlyList<LinkEntity>;
        return Task.FromResult(links);
    }

    public Task<ListLinksResult> ListAsync(Guid tenantId, LinkListQuery query)
    {
        var filtered = _links.Values
            .Where(l => l.TenantId == tenantId && l.DeletedAt is null);

        if (query.DomainId.HasValue)
            filtered = filtered.Where(l => l.DomainId == query.DomainId.Value);

        if (!string.IsNullOrWhiteSpace(query.Status))
            filtered = filtered.Where(l => l.Status == query.Status);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.ToLowerInvariant();
            filtered = filtered.Where(l =>
                l.Slug.StartsWith(search, StringComparison.OrdinalIgnoreCase) ||
                l.DestinationUrl.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = query.Sort switch
        {
            "updated_at" => filtered.OrderByDescending(l => l.UpdatedAt ?? l.CreatedAt),
            "click_count" => filtered.OrderByDescending(l => l.ClickCount),
            _ => filtered.OrderByDescending(l => l.CreatedAt)
        };

        var allMatches = ordered.ToList();
        var totalCount = allMatches.Count;

        // Cursor-based pagination using the item index
        var limit = Math.Clamp(query.Limit, 1, 100);
        var startIndex = 0;

        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            var decoded = DecodeCursor(query.Cursor);
            if (decoded.HasValue)
            {
                var cursorIndex = allMatches.FindIndex(e => e.Id == decoded.Value);
                if (cursorIndex >= 0)
                    startIndex = cursorIndex + 1;
            }
        }

        var page = allMatches.Skip(startIndex).Take(limit).ToList();
        string? nextCursor = null;

        if (startIndex + page.Count < totalCount)
        {
            var lastItem = page[^1];
            nextCursor = EncodeCursor(lastItem.Id);
        }

        return Task.FromResult(new ListLinksResult
        {
            Items = page,
            NextCursor = nextCursor,
            TotalCount = totalCount
        });
    }

    public Task<LinkEntity?> SoftDeleteAsync(Guid tenantId, Guid linkId)
    {
        if (!_links.TryGetValue(linkId, out var existing))
            return Task.FromResult<LinkEntity?>(null);

        if (existing.TenantId != tenantId || existing.DeletedAt is not null)
            return Task.FromResult<LinkEntity?>(null);

        var deleted = existing with
        {
            Status = "deleted",
            DeletedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _links[linkId] = deleted;
        return Task.FromResult<LinkEntity?>(deleted);
    }

    public Task<LinkEntity?> RestoreAsync(Guid tenantId, Guid linkId)
    {
        if (!_links.TryGetValue(linkId, out var existing))
            return Task.FromResult<LinkEntity?>(null);

        if (existing.TenantId != tenantId)
            return Task.FromResult<LinkEntity?>(null);

        if (existing.Status != "deleted" || existing.DeletedAt is null)
            return Task.FromResult<LinkEntity?>(null);

        var deletedDuration = DateTime.UtcNow - existing.DeletedAt.Value;
        if (deletedDuration > TimeSpan.FromDays(30))
            return Task.FromResult<LinkEntity?>(null);

        var restored = existing with
        {
            Status = "active",
            DeletedAt = null,
            UpdatedAt = DateTime.UtcNow
        };
        _links[linkId] = restored;
        return Task.FromResult<LinkEntity?>(restored);
    }

    public Task<int> DeactivateByDomainAsync(Guid tenantId, Guid domainId)
    {
        var toDeactivate = _links.Values
            .Where(l => l.TenantId == tenantId && l.DomainId == domainId &&
                        l.DeletedAt is null && l.Status == "active")
            .ToList();

        foreach (var link in toDeactivate)
            _links[link.Id] = link with { Status = "inactive", UpdatedAt = DateTime.UtcNow };

        return Task.FromResult(toDeactivate.Count);
    }

    public Task<int> ReactivateByDomainAsync(Guid tenantId, Guid domainId)
    {
        var toReactivate = _links.Values
            .Where(l => l.TenantId == tenantId && l.DomainId == domainId &&
                        l.DeletedAt is null && l.Status == "inactive")
            .ToList();

        foreach (var link in toReactivate)
            _links[link.Id] = link with { Status = "active", UpdatedAt = DateTime.UtcNow };

        return Task.FromResult(toReactivate.Count);
    }

    public Task<IReadOnlyList<LinkEntity>> GetLinksForReScanAsync(CancellationToken ct = default)
    {
        var links = _links.Values
            .Where(l => l.DeletedAt is null &&
                (l.Status == "active" || l.Status == "quarantined"))
            .OrderByDescending(l => l.ClickCount)
            .ToList() as IReadOnlyList<LinkEntity>;
        return Task.FromResult(links!);
    }

    public Task<bool> HardDeleteAsync(Guid tenantId, Guid linkId)
    {
        if (!_links.TryGetValue(linkId, out var existing))
            return Task.FromResult(false);

        if (existing.TenantId != tenantId)
            return Task.FromResult(false);

        return Task.FromResult(_links.TryRemove(linkId, out _));
    }

    public Task<LinkEntity?> UpdateAsync(Guid tenantId, Guid linkId, UpdateLinkRequest update)
    {
        if (!_links.TryGetValue(linkId, out var existing))
            return Task.FromResult<LinkEntity?>(null);

        if (existing.TenantId != tenantId || existing.DeletedAt is not null)
            return Task.FromResult<LinkEntity?>(null);

        var updated = existing with
        {
            DestinationUrl = update.DestinationUrl ?? existing.DestinationUrl,
            RedirectType = update.RedirectType ?? existing.RedirectType,
            ExpiresAt = update.ClearExpiresAt ? null :
                update.ExpiresAt.HasValue ? update.ExpiresAt.Value : existing.ExpiresAt,
            MaxClicks = update.ClearMaxClicks ? null :
                update.MaxClicks.HasValue ? update.MaxClicks.Value : existing.MaxClicks,
            Rules = update.ClearRules ? null :
                update.Rules is not null ? new Dictionary<string, string>(update.Rules) : existing.Rules,
            Status = update.Status ?? existing.Status,
            Version = existing.Version + 1,
            UpdatedAt = DateTime.UtcNow
        };

        _links[linkId] = updated;
        return Task.FromResult<LinkEntity?>(updated);
    }

    private static string EncodeCursor(Guid id)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(id.ToString("N")));
    }

    private static Guid? DecodeCursor(string cursor)
    {
        try
        {
            var bytes = Convert.FromBase64String(cursor);
            var guidStr = Encoding.UTF8.GetString(bytes);
            if (Guid.TryParseExact(guidStr, "N", out var guid))
                return guid;
        }
        catch
        {
            // Invalid cursor — reset to beginning
        }
        return null;
    }
}
