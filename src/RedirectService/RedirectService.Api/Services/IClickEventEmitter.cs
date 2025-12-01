namespace RedirectService.Api.Services;

/// <summary>
/// Analytics event emitted for every successful redirect click.
/// Serialised to snake_case JSON and written to the Kinesis click-events stream.
/// Schema version: <c>"1"</c>  —  see <c>docs/schemas/click-event.schema.json</c>.
/// </summary>
public sealed record ClickEvent
{
    /// <summary>UUID that uniquely identifies this event across all pipeline stages.</summary>
    public required string EventId { get; init; }

    /// <summary>Schema version. Increment when breaking changes are introduced. Current value: <c>"1"</c>.</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>UTC timestamp of the redirect.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Tenant that owns the short link.</summary>
    public required string TenantId { get; init; }

    /// <summary>Domain entity identifier in the control plane.</summary>
    public required string DomainId { get; init; }

    /// <summary>Hostname of the short link (e.g. <c>go.customer.com</c>).</summary>
    public required string Domain { get; init; }

    /// <summary>Slug component of the short link (e.g. <c>summer-sale</c>).</summary>
    public required string Slug { get; init; }

    /// <summary>Resolved destination URL, including any configured UTM parameters.</summary>
    public required string DestinationUrl { get; init; }

    /// <summary>HTTP redirect status code returned to the client (301, 302, 307, or 308).</summary>
    public required int StatusCode { get; init; }

    /// <summary>ISO 3166-1 alpha-2 country code from <c>CloudFront-Viewer-Country</c>.</summary>
    public string? Country { get; init; }

    /// <summary>
    /// ISO 3166-2 region subdivision code (e.g. <c>CA-ON</c>).
    /// Populated when extended CloudFront geographic headers are forwarded.
    /// </summary>
    public string? Region { get; init; }

    /// <summary>
    /// City name as reported by CloudFront.
    /// Populated when extended CloudFront geographic headers are forwarded.
    /// </summary>
    public string? City { get; init; }

    /// <summary>Device category: <c>mobile</c>, <c>tablet</c>, <c>tv</c>, or <c>desktop</c>.</summary>
    public string? DeviceType { get; init; }

    /// <summary>
    /// Browser family (e.g. <c>Chrome</c>, <c>Safari</c>).
    /// <see langword="null"/> when only the hashed User-Agent is available; populated by enrichment stages.
    /// </summary>
    public string? Browser { get; init; }

    /// <summary>
    /// Operating system (e.g. <c>Windows</c>, <c>iOS</c>).
    /// <see langword="null"/> when only the hashed User-Agent is available; populated by enrichment stages.
    /// </summary>
    public string? Os { get; init; }

    /// <summary>Referrer URL from the <c>Referer</c> header.</summary>
    public string? Referrer { get; init; }

    /// <summary>UTM source parameter configured on the short link.</summary>
    public string? UtmSource { get; init; }

    /// <summary>UTM medium parameter configured on the short link.</summary>
    public string? UtmMedium { get; init; }

    /// <summary>UTM campaign parameter configured on the short link.</summary>
    public string? UtmCampaign { get; init; }

    /// <summary>Bot score injected by WAF via <c>X-Bot-Score</c> (0–100), or <see langword="null"/> if absent.</summary>
    public int? BotScore { get; init; }

    /// <summary>End-to-end request latency in milliseconds. Set by the Lambda handler; <see langword="null"/> when not measurable.</summary>
    public long? LatencyMs { get; init; }

    /// <summary>SHA-256 hex digest of the viewer IP from <c>CloudFront-Viewer-Address</c>. Raw IP is never stored.</summary>
    public string? IpHash { get; init; }

    /// <summary>SHA-256 hex digest of the <c>User-Agent</c> header. Raw value is never stored.</summary>
    public string? UserAgentHash { get; init; }
}

/// <summary>
/// Emits click events for analytics processing.
/// Implementations must be non-blocking; callers do not wait for downstream persistence.
/// </summary>
public interface IClickEventEmitter
{
    Task EmitAsync(ClickEvent clickEvent, CancellationToken ct = default);
}
