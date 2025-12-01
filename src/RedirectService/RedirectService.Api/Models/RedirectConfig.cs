namespace RedirectService.Api.Models;

/// <summary>
/// Redirect configuration resolved from DynamoDB for a given hostname + slug pair.
/// This is the hot-path read model: fetched on every click via <c>GetItem</c>.
/// </summary>
public sealed record RedirectConfig
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

    /// <summary>Link status. Only <c>active</c> links produce redirects; others return 404.</summary>
    public string Status { get; init; } = "active";

    /// <summary>UTC expiry timestamp. Null means the link never expires.</summary>
    public DateTime? ExpiresAt { get; init; }
}
