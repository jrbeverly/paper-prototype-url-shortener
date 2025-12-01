namespace RedirectService.Api.Models;

/// <summary>
/// Incoming redirect lookup request parsed from an HTTP request.
/// </summary>
public sealed record RedirectRequest
{
    /// <summary>Hostname from the Host header (e.g. <c>go.customer.com</c>).</summary>
    public required string Hostname { get; init; }

    /// <summary>Slug extracted from the request path (e.g. <c>summer-sale</c>).</summary>
    public required string Slug { get; init; }

    /// <summary>Query parameters from the incoming URL, forwarded for rule evaluation.</summary>
    public IReadOnlyDictionary<string, string> QueryParameters { get; init; }
        = new Dictionary<string, string>();

    /// <summary>HTTP headers from the incoming request, used for geo and device rule evaluation.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; }
        = new Dictionary<string, string>();
}
