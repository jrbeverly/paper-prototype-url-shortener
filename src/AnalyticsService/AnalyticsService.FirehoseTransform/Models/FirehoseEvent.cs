using System.Text.Json.Serialization;

namespace AnalyticsService.FirehoseTransform.Models;

/// <summary>Kinesis Data Firehose transformation invocation payload.</summary>
public sealed record FirehoseEvent
{
    [JsonPropertyName("invocationId")] public required string InvocationId { get; init; }
    [JsonPropertyName("deliveryStreamArn")] public required string DeliveryStreamArn { get; init; }
    [JsonPropertyName("region")] public required string Region { get; init; }
    [JsonPropertyName("records")] public required IReadOnlyList<FirehoseRecord> Records { get; init; }
}

/// <summary>Single record within a Firehose transformation invocation.</summary>
public sealed record FirehoseRecord
{
    [JsonPropertyName("recordId")] public required string RecordId { get; init; }
    [JsonPropertyName("approximateArrivalTimestamp")] public required long ApproximateArrivalTimestamp { get; init; }
    /// <summary>Base64-encoded payload bytes.</summary>
    [JsonPropertyName("data")] public required string Data { get; init; }
}
