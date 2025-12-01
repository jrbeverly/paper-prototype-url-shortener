namespace ControlPlane.Api.Models.Responses;

public sealed record CreateLinkResponse
{
    /// <summary>The unique identifier of the link.</summary>
    public required Guid Id { get; init; }

    /// <summary>The domain this link belongs to.</summary>
    public required Guid DomainId { get; init; }

    /// <summary>The destination URL visitors are redirected to.</summary>
    public required string DestinationUrl { get; init; }

    /// <summary>The short slug for this link.</summary>
    public required string Slug { get; init; }

    /// <summary>The full short URL (https://{hostname}/{slug}).</summary>
    public required string ShortUrl { get; init; }

    /// <summary>The HTTP redirect type: 301, 302, 307, or 308.</summary>
    public required string RedirectType { get; init; }

    /// <summary>The link status: "active", "paused", or "deleted".</summary>
    public string Status { get; init; } = "active";

    /// <summary>When the link expires, or null if it never expires.</summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>Maximum number of clicks before expiration, or null if unlimited.</summary>
    public int? MaxClicks { get; init; }

    /// <summary>When the link was soft-deleted, or null if not deleted.</summary>
    public DateTime? DeletedAt { get; init; }

    /// <summary>Total number of clicks (redirects) this link has served.</summary>
    public long ClickCount { get; init; }

    /// <summary>When the link was last updated.</summary>
    public DateTime? UpdatedAt { get; init; }

    /// <summary>When the link was created.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>Monotonically incremented on each update, for cache invalidation and audit trail.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Key-value routing rules for this link.</summary>
    public Dictionary<string, string>? Rules { get; init; }

    /// <summary>Near-expiry warning metadata, or null if the link is not near expiry.</summary>
    public LinkExpiryWarning? ExpiryWarning { get; init; }

    /// <summary>URL safety scan warning, or null if the destination passed clean or was not scanned.</summary>
    public UrlSafetyWarning? SafetyWarning { get; init; }
}

/// <summary>
/// Warning produced when a URL safety scan returns a suspicious or timed-out verdict.
/// The link is still created but the destination may be risky.
/// </summary>
public sealed record UrlSafetyWarning
{
    public required string Verdict { get; init; }
    public required string Reason { get; init; }
    public required string Source { get; init; }
}

public sealed record LinkListItemResponse
{
    /// <summary>The unique identifier of the link.</summary>
    public required Guid Id { get; init; }

    /// <summary>The domain this link belongs to.</summary>
    public required Guid DomainId { get; init; }

    /// <summary>The hostname of the domain (resolved for display).</summary>
    public required string DomainHostname { get; init; }

    /// <summary>The destination URL visitors are redirected to.</summary>
    public required string DestinationUrl { get; init; }

    /// <summary>The short slug for this link.</summary>
    public required string Slug { get; init; }

    /// <summary>The full short URL (https://{hostname}/{slug}).</summary>
    public required string ShortUrl { get; init; }

    /// <summary>The HTTP redirect type: 301, 302, 307, or 308.</summary>
    public required string RedirectType { get; init; }

    /// <summary>The link status: "active", "paused", or "deleted".</summary>
    public required string Status { get; init; }

    /// <summary>Total number of clicks (redirects) this link has served.</summary>
    public required long ClickCount { get; init; }

    /// <summary>Maximum number of clicks before expiration, or null if unlimited.</summary>
    public int? MaxClicks { get; init; }

    /// <summary>When the link expires, or null if it never expires.</summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>When the link was last updated.</summary>
    public DateTime? UpdatedAt { get; init; }

    /// <summary>When the link was created.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>Monotonically incremented on each update.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Near-expiry warning metadata, or null if the link is not near expiry.</summary>
    public LinkExpiryWarning? ExpiryWarning { get; init; }
}

public sealed record LinkListResponse
{
    /// <summary>The list of link items for the current page/cursor.</summary>
    public required List<LinkListItemResponse> Items { get; init; }

    /// <summary>Cursor for the next page of results, or null if this is the last page.</summary>
    public string? NextCursor { get; init; }

    /// <summary>Total number of links matching the query (across all pages).</summary>
    public required int TotalCount { get; init; }
}

public sealed record LinkDetailResponse
{
    /// <summary>The unique identifier of the link.</summary>
    public required Guid Id { get; init; }

    /// <summary>The domain this link belongs to.</summary>
    public required Guid DomainId { get; init; }

    /// <summary>The hostname of the domain (resolved for display).</summary>
    public required string DomainHostname { get; init; }

    /// <summary>The destination URL visitors are redirected to.</summary>
    public required string DestinationUrl { get; init; }

    /// <summary>The short slug for this link.</summary>
    public required string Slug { get; init; }

    /// <summary>The full short URL (https://{hostname}/{slug}).</summary>
    public required string ShortUrl { get; init; }

    /// <summary>The HTTP redirect type: 301, 302, 307, or 308.</summary>
    public required string RedirectType { get; init; }

    /// <summary>The link status: "active", "paused", or "deleted".</summary>
    public required string Status { get; init; }

    /// <summary>Total number of clicks (redirects) this link has served.</summary>
    public required long ClickCount { get; init; }

    /// <summary>Maximum number of clicks before expiration, or null if unlimited.</summary>
    public int? MaxClicks { get; init; }

    /// <summary>When the link expires, or null if it never expires.</summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>When the link was soft-deleted, or null if not deleted.</summary>
    public DateTime? DeletedAt { get; init; }

    /// <summary>When the link was last updated.</summary>
    public DateTime? UpdatedAt { get; init; }

    /// <summary>When the link was created.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>Monotonically incremented on each update.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Key-value routing rules for this link.</summary>
    public Dictionary<string, string>? Rules { get; init; }

    /// <summary>Aggregated analytics data for this link.</summary>
    public required LinkAnalyticsSummary Analytics { get; init; }

    /// <summary>Version audit trail, oldest first.</summary>
    public required IReadOnlyList<LinkAuditEntry> AuditTrail { get; init; }

    /// <summary>Near-expiry warning metadata, or null if the link is not near expiry.</summary>
    public LinkExpiryWarning? ExpiryWarning { get; init; }
}

public sealed record LinkAnalyticsSummary
{
    /// <summary>Total number of clicks (redirects) this link has served.</summary>
    public required long TotalClicks { get; init; }

    /// <summary>Estimated number of unique visitors (deduplicated by IP + UA hash).</summary>
    public required long UniqueClicks { get; init; }

    /// <summary>Number of clicks received today (UTC day bucket).</summary>
    public required long ClicksToday { get; init; }

    /// <summary>Daily click counts for the last 30 days, oldest first.</summary>
    public required IReadOnlyList<DailyClickCount> ClickTrend { get; init; }

    /// <summary>Top countries by click count, highest first.</summary>
    public required IReadOnlyList<CountryBreakdown> TopCountries { get; init; }

    /// <summary>Top referrer domains by click count, highest first.</summary>
    public required IReadOnlyList<ReferrerBreakdown> TopReferrers { get; init; }

    /// <summary>Click counts by device type.</summary>
    public required DeviceBreakdown Devices { get; init; }
}

public sealed record DailyClickCount
{
    public required string Date { get; init; }
    public required long Count { get; init; }
}

public sealed record CountryBreakdown
{
    public required string CountryCode { get; init; }
    public required string CountryName { get; init; }
    public required long Count { get; init; }
}

public sealed record ReferrerBreakdown
{
    public required string Domain { get; init; }
    public required long Count { get; init; }
}

public sealed record DeviceBreakdown
{
    public required long Desktop { get; init; }
    public required long Mobile { get; init; }
    public required long Tablet { get; init; }
}

public sealed record LinkAuditEntry
{
    public required int Version { get; init; }
    public required string Action { get; init; }
    public required string Description { get; init; }
    public required DateTime Timestamp { get; init; }
}

/// <summary>
/// Near-expiry warning metadata for a link.
/// Computed in the management API: time-based within 7 days of <c>ExpiresAt</c>,
/// click-based within the last 10% of remaining clicks.
/// </summary>
public sealed record LinkExpiryWarning
{
    /// <summary>Whether the link is nearing its expiry threshold.</summary>
    public bool IsNearingExpiry { get; init; }

    /// <summary>The type of expiry threshold: <c>"time"</c> or <c>"clicks"</c>.</summary>
    public string? Type { get; init; }

    /// <summary>Human-readable message describing the near-expiry state.</summary>
    public string? Message { get; init; }

    /// <summary>Remaining clicks before the limit is reached (only when <see cref="Type"/> is <c>"clicks"</c>).</summary>
    public long? RemainingClicks { get; init; }

    /// <summary>Days remaining before expiration (only when <see cref="Type"/> is <c>"time"</c>).</summary>
    public double? RemainingDays { get; init; }
}
