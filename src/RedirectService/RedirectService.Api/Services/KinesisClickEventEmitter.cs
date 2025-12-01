using System.Text;
using System.Text.Json;
using Amazon.Kinesis;
using Amazon.Kinesis.Model;

namespace RedirectService.Api.Services;

/// <summary>
/// Emits click events to a Kinesis Data Stream for analytics processing.
/// Returns immediately after scheduling the put; the caller is not blocked.
/// Failures are written to stdout (ingested by CloudWatch Logs) and do not propagate.
/// </summary>
public sealed class KinesisClickEventEmitter(
    IAmazonKinesis kinesis,
    string streamName) : IClickEventEmitter
{
    // snake_case matches the canonical schema in docs/schemas/click-event.schema.json
    // and is the standard naming convention for analytics/data-pipeline payloads.
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <inheritdoc/>
    public Task EmitAsync(ClickEvent clickEvent, CancellationToken ct = default)
    {
        // Fire-and-forget: the redirect response does not wait for Kinesis acknowledgement.
        // CancellationToken.None: the caller's token may be cancelled once the response is sent;
        // analytics delivery should proceed regardless.
        _ = PutRecordAsync(clickEvent);
        return Task.CompletedTask;
    }

    private async Task PutRecordAsync(ClickEvent clickEvent)
    {
        try
        {
            var json = JsonSerializer.Serialize(clickEvent, _jsonOptions);
            await kinesis.PutRecordAsync(new PutRecordRequest
            {
                StreamName = streamName,
                Data = new MemoryStream(Encoding.UTF8.GetBytes(json)),
                // Shard by tenant so all clicks from one tenant are ordered on the same shard.
                PartitionKey = clickEvent.TenantId
            });
        }
        catch (Exception ex)
        {
            // Write structured warning to stdout so CloudWatch Logs Insights can query it.
            Console.Error.WriteLine(
                $"{{\"level\":\"warn\",\"event\":\"click_emit_failed\",\"stream\":\"{streamName}\",\"error\":\"{ex.Message}\"}}");
        }
    }
}
