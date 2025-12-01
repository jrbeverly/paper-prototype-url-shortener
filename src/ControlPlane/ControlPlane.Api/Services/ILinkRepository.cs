using ControlPlane.Api.Models.Requests;

namespace ControlPlane.Api.Services;

public interface ILinkRepository
{
    Task<LinkEntity> CreateAsync(LinkEntity entity);
    Task<LinkEntity?> GetByIdAsync(Guid tenantId, Guid linkId);
    Task<LinkEntity?> GetByDomainAndSlugAsync(Guid tenantId, Guid domainId, string slug);
    Task<int> GetCountByTenantAsync(Guid tenantId);
    Task<int> GetCountByDomainAsync(Guid tenantId, Guid domainId);
    Task<IReadOnlyList<LinkEntity>> GetAllByTenantAsync(Guid tenantId);
    Task<ListLinksResult> ListAsync(Guid tenantId, LinkListQuery query);
    Task<LinkEntity?> SoftDeleteAsync(Guid tenantId, Guid linkId);
    Task<LinkEntity?> RestoreAsync(Guid tenantId, Guid linkId);
    Task<bool> HardDeleteAsync(Guid tenantId, Guid linkId);
    Task<LinkEntity?> UpdateAsync(Guid tenantId, Guid linkId, UpdateLinkRequest update);

    /// <summary>
    /// Sets all non-deleted links for a domain to "inactive" status. Returns the count changed.
    /// Called when a domain is deactivated so the redirect service stops serving those links.
    /// Only "active" links are affected; quarantined links are left as-is.
    /// </summary>
    Task<int> DeactivateByDomainAsync(Guid tenantId, Guid domainId);

    /// <summary>
    /// Restores all "inactive" links for a domain back to "active" status. Returns the count changed.
    /// Called when a previously deactivated domain is reactivated within the grace period.
    /// </summary>
    Task<int> ReactivateByDomainAsync(Guid tenantId, Guid domainId);

    /// <summary>
    /// Returns all links eligible for periodic re-scanning (status "active" or "quarantined", not deleted)
    /// across all tenants, ordered by click count descending so high-traffic links are scanned first.
    /// </summary>
    Task<IReadOnlyList<LinkEntity>> GetLinksForReScanAsync(CancellationToken ct = default);
}

public sealed record LinkListQuery
{
    public string? Cursor { get; init; }
    public int Limit { get; init; } = 20;
    public string? Search { get; init; }
    public Guid? DomainId { get; init; }
    public string? Status { get; init; }
    public string Sort { get; init; } = "created_at";
}

public sealed record ListLinksResult
{
    public required IReadOnlyList<LinkEntity> Items { get; init; }
    public string? NextCursor { get; init; }
    public required int TotalCount { get; init; }
}

public sealed record LinkEntity
{
    public required Guid Id { get; init; }
    public required Guid TenantId { get; init; }
    public required Guid DomainId { get; init; }
    public required string DestinationUrl { get; init; }
    public required string Slug { get; init; }
    public string RedirectType { get; init; } = "302";
    public string Status { get; init; } = "active";
    public long ClickCount { get; init; }
    public int? MaxClicks { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public DateTime? DeletedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public required DateTime CreatedAt { get; init; }
    public int Version { get; init; } = 1;
    public Dictionary<string, string>? Rules { get; init; }
}
