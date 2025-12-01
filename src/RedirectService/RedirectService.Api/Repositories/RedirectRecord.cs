namespace RedirectService.Api.Repositories;

/// <summary>
/// Redirect data resolved from DynamoDB for a given hostname + slug pair.
/// Returned by <see cref="IRedirectRepository"/>; consumed by the redirect handler.
/// </summary>
public sealed record RedirectRecord
{
    /// <summary>Tenant that owns this link.</summary>
    public required string TenantId { get; init; }

    /// <summary>Domain record that owns this link.</summary>
    public required string DomainId { get; init; }

    /// <summary>Custom hostname (e.g. <c>go.customer.com</c>).</summary>
    public required string Hostname { get; init; }

    /// <summary>Short slug (e.g. <c>summer-sale</c>).</summary>
    public required string Slug { get; init; }

    /// <summary>Full destination URL to redirect to.</summary>
    public required string DestinationUrl { get; init; }

    /// <summary>HTTP redirect status code: 301, 302, 307, or 308. Defaults to 302.</summary>
    public int RedirectType { get; init; } = 302;

    /// <summary>Link status. Only <c>active</c> links are returned by the repository.</summary>
    public string Status { get; init; } = "active";

    /// <summary>UTC expiry timestamp. <see langword="null"/> means the link never expires.</summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>Maximum number of clicks before the link expires. <see langword="null"/> means unlimited.</summary>
    public int? MaxClicks { get; init; }

    /// <summary>Number of clicks served for this link so far (approximate; updated atomically).</summary>
    public long CurrentClicks { get; init; }

    /// <summary>
    /// Optional UTM tracking parameters appended to the destination URL on redirect.
    /// <see langword="null"/> or empty means no UTM parameters are configured for this link.
    /// </summary>
    public IReadOnlyDictionary<string, string>? UtmParameters { get; init; }
}
