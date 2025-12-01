using System.Text.Json.Serialization;

namespace AnalyticsService.FirehoseTransform.Models;

/// <summary>
/// Canonical click event record as produced by the redirect Lambda and stored in S3.
/// All fields are nullable to enable explicit missing-field validation.
/// Field names use snake_case to match the JSON schema in docs/schemas/click-event.schema.json.
/// </summary>
public sealed record ClickEvent
{
    [JsonPropertyName("event_id")] public string? EventId { get; init; }
    [JsonPropertyName("schema_version")] public string? SchemaVersion { get; init; }
    [JsonPropertyName("timestamp")] public DateTimeOffset? Timestamp { get; init; }
    [JsonPropertyName("tenant_id")] public string? TenantId { get; init; }
    [JsonPropertyName("domain_id")] public string? DomainId { get; init; }
    [JsonPropertyName("domain")] public string? Domain { get; init; }
    [JsonPropertyName("slug")] public string? Slug { get; init; }
    [JsonPropertyName("destination_url")] public string? DestinationUrl { get; init; }
    [JsonPropertyName("status_code")] public int? StatusCode { get; init; }
    [JsonPropertyName("country")] public string? Country { get; init; }
    [JsonPropertyName("region")] public string? Region { get; init; }
    [JsonPropertyName("city")] public string? City { get; init; }
    [JsonPropertyName("device_type")] public string? DeviceType { get; init; }
    [JsonPropertyName("browser")] public string? Browser { get; init; }
    [JsonPropertyName("os")] public string? Os { get; init; }
    [JsonPropertyName("referrer")] public string? Referrer { get; init; }
    [JsonPropertyName("utm_source")] public string? UtmSource { get; init; }
    [JsonPropertyName("utm_medium")] public string? UtmMedium { get; init; }
    [JsonPropertyName("utm_campaign")] public string? UtmCampaign { get; init; }
    [JsonPropertyName("bot_score")] public int? BotScore { get; init; }
    [JsonPropertyName("latency_ms")] public long? LatencyMs { get; init; }
    [JsonPropertyName("ip_hash")] public string? IpHash { get; init; }
    [JsonPropertyName("user_agent_hash")] public string? UserAgentHash { get; init; }
}
