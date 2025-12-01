using System.Text.Json.Serialization;

namespace AnalyticsService.FirehoseTransform.Models;

/// <summary>Response returned to Kinesis Data Firehose after processing a batch of records.</summary>
public sealed record FirehoseResponse
{
    [JsonPropertyName("records")] public required IReadOnlyList<FirehoseRecordResult> Records { get; init; }
}

/// <summary>Per-record transformation result.</summary>
public sealed record FirehoseRecordResult
{
    [JsonPropertyName("recordId")] public required string RecordId { get; init; }

    /// <summary>
    /// Disposition of this record.
    /// <list type="bullet">
    ///   <item><c>Ok</c> — record passed validation and is delivered to S3.</item>
    ///   <item><c>ProcessingFailed</c> — record failed validation and is written to the Firehose error output (dead-letter bucket).</item>
    ///   <item><c>Dropped</c> — record is silently discarded (not used by this transform).</item>
    /// </list>
    /// </summary>
    [JsonPropertyName("result")] public required string Result { get; init; }

    /// <summary>Base64-encoded payload bytes (returned unchanged for Ok; original bytes for ProcessingFailed).</summary>
    [JsonPropertyName("data")] public required string Data { get; init; }
}

/// <summary>Well-known result strings returned to Firehose.</summary>
public static class FirehoseResult
{
    public const string Ok = "Ok";
    public const string ProcessingFailed = "ProcessingFailed";
}
