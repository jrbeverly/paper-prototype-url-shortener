using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using AnalyticsService.FirehoseTransform.Models;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace AnalyticsService.FirehoseTransform;

/// <summary>
/// Kinesis Data Firehose transformation Lambda.
/// Validates each click event record against the canonical schema
/// (docs/schemas/click-event.schema.json). Records that pass are delivered
/// to the S3 destination; records that fail are written to the Firehose
/// error output bucket (dead-letter bucket).
/// </summary>
public sealed class Function
{
    // Supported schema versions. Events with an unknown version are rejected.
    private static readonly HashSet<string> _supportedVersions = ["1"];

    // Valid HTTP redirect status codes per RedirectRecord.RedirectType.
    private static readonly HashSet<int> _validStatusCodes = [301, 302, 307, 308];

    private static readonly JsonSerializerOptions _readOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Lambda handler method invoked by Kinesis Data Firehose for each batch of records.
    /// </summary>
    public FirehoseResponse HandleAsync(FirehoseEvent firehoseEvent, ILambdaContext context)
    {
        var results = new List<FirehoseRecordResult>(firehoseEvent.Records.Count);

        foreach (var record in firehoseEvent.Records)
        {
            var (result, reason) = ProcessRecord(record.Data);

            if (result == FirehoseResult.ProcessingFailed)
            {
                context.Logger.LogWarning(
                    "Click event validation failed: recordId={RecordId} reason={Reason}",
                    record.RecordId, reason);
            }

            results.Add(new FirehoseRecordResult
            {
                RecordId = record.RecordId,
                Result = result,
                Data = record.Data  // original bytes returned unchanged
            });
        }

        return new FirehoseResponse { Records = results };
    }

    /// <summary>
    /// Decodes and validates a single base64-encoded click event record.
    /// </summary>
    /// <returns>
    /// (<c>"Ok"</c>, <see langword="null"/>) when valid;
    /// (<c>"ProcessingFailed"</c>, reason) when invalid.
    /// </returns>
    public static (string Result, string? Reason) ProcessRecord(string base64Data)
    {
        ClickEvent clickEvent;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64Data));
            var deserialized = JsonSerializer.Deserialize<ClickEvent>(json, _readOptions);
            if (deserialized is null)
                return (FirehoseResult.ProcessingFailed, "Deserialised to null");
            clickEvent = deserialized;
        }
        catch (Exception ex)
        {
            return (FirehoseResult.ProcessingFailed, $"JSON decode error: {ex.Message}");
        }

        var (valid, reason) = Validate(clickEvent);
        return valid
            ? (FirehoseResult.Ok, null)
            : (FirehoseResult.ProcessingFailed, reason);
    }

    /// <summary>
    /// Validates all required fields and PII-hash format constraints.
    /// </summary>
    public static (bool Valid, string? Reason) Validate(ClickEvent e)
    {
        // ── Required fields ──────────────────────────────────────────────────────
        if (string.IsNullOrEmpty(e.EventId))
            return (false, "Missing required field: event_id");

        if (string.IsNullOrEmpty(e.SchemaVersion))
            return (false, "Missing required field: schema_version");

        if (!_supportedVersions.Contains(e.SchemaVersion))
            return (false, $"Unsupported schema_version: {e.SchemaVersion}");

        if (e.Timestamp is null)
            return (false, "Missing required field: timestamp");

        if (string.IsNullOrEmpty(e.TenantId))
            return (false, "Missing required field: tenant_id");

        if (string.IsNullOrEmpty(e.DomainId))
            return (false, "Missing required field: domain_id");

        if (string.IsNullOrEmpty(e.Domain))
            return (false, "Missing required field: domain");

        if (string.IsNullOrEmpty(e.Slug))
            return (false, "Missing required field: slug");

        if (string.IsNullOrEmpty(e.DestinationUrl))
            return (false, "Missing required field: destination_url");

        if (e.StatusCode is null)
            return (false, "Missing required field: status_code");

        if (!_validStatusCodes.Contains(e.StatusCode.Value))
            return (false, $"Invalid status_code: {e.StatusCode} (must be 301, 302, 307, or 308)");

        // ── PII hash format ──────────────────────────────────────────────────────
        // SHA-256 produces a 256-bit digest = 64 lowercase hex characters.
        // A non-null hash that doesn't match this pattern indicates a raw PII value leaked through.
        if (e.IpHash is not null && !IsValidSha256Hex(e.IpHash))
            return (false, "ip_hash must be a 64-character lowercase SHA-256 hex digest");

        if (e.UserAgentHash is not null && !IsValidSha256Hex(e.UserAgentHash))
            return (false, "user_agent_hash must be a 64-character lowercase SHA-256 hex digest");

        return (true, null);
    }

    private static bool IsValidSha256Hex(string value)
    {
        if (value.Length != 64) return false;
        foreach (var c in value)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                return false;
        }
        return true;
    }
}
