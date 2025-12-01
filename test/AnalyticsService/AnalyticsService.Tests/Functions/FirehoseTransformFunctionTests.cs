using System.Text;
using System.Text.Json;

namespace AnalyticsService.Tests.Functions;

public sealed class FirehoseTransformFunctionTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static string Encode(object payload)
    {
        var json = JsonSerializer.Serialize(payload, _writeOptions);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static object ValidPayload(
        string? eventId = "550e8400-e29b-41d4-a716-446655440000",
        string? schemaVersion = "1",
        string? timestamp = "2024-06-01T12:00:00Z",
        string? tenantId = "tenant-abc",
        string? domainId = "domain-xyz",
        string? domain = "go.example.com",
        string? slug = "promo",
        string? destinationUrl = "https://example.com/promo",
        int statusCode = 302,
        string? ipHash = null,
        string? userAgentHash = null) => new
        {
            event_id = eventId,
            schema_version = schemaVersion,
            timestamp,
            tenant_id = tenantId,
            domain_id = domainId,
            domain,
            slug,
            destination_url = destinationUrl,
            status_code = statusCode,
            ip_hash = ipHash,
            user_agent_hash = userAgentHash
        };

    private static FirehoseEvent BuildFirehoseEvent(params string[] base64Records)
    {
        var records = base64Records.Select((data, i) => new FirehoseRecord
        {
            RecordId = $"record-{i}",
            ApproximateArrivalTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = data
        }).ToList();

        return new FirehoseEvent
        {
            InvocationId = "inv-1",
            DeliveryStreamArn = "arn:aws:firehose:us-east-1:123456789012:deliverystream/clicks",
            Region = "us-east-1",
            Records = records
        };
    }

    // ── Validation: valid event ───────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void ProcessRecord_WithValidEvent_ReturnsOk()
    {
        var (result, reason) = Function.ProcessRecord(Encode(ValidPayload()));

        result.Should().Be(FirehoseResult.Ok);
        reason.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ProcessRecord_WithAllOptionalFieldsPopulated_ReturnsOk()
    {
        var payload = new
        {
            event_id = "550e8400-e29b-41d4-a716-446655440000",
            schema_version = "1",
            timestamp = "2024-06-01T12:00:00Z",
            tenant_id = "tenant-abc",
            domain_id = "domain-xyz",
            domain = "go.example.com",
            slug = "promo",
            destination_url = "https://example.com/promo",
            status_code = 301,
            country = "CA",
            region = "CA-ON",
            city = "Toronto",
            device_type = "mobile",
            browser = "Chrome",
            os = "iOS",
            referrer = "https://google.com",
            utm_source = "newsletter",
            utm_medium = "email",
            utm_campaign = "summer24",
            bot_score = 5,
            latency_ms = 42,
            ip_hash = new string('a', 64),
            user_agent_hash = new string('b', 64)
        };

        var (result, _) = Function.ProcessRecord(Encode(payload));

        result.Should().Be(FirehoseResult.Ok);
    }

    // ── Validation: required fields ───────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("event_id")]
    [InlineData("schema_version")]
    [InlineData("timestamp")]
    [InlineData("tenant_id")]
    [InlineData("domain_id")]
    [InlineData("domain")]
    [InlineData("slug")]
    [InlineData("destination_url")]
    [InlineData("status_code")]
    public void ProcessRecord_WithMissingRequiredField_ReturnsProcessingFailed(string missingField)
    {
        // Build payload as dictionary so we can remove any field by name.
        var dict = new Dictionary<string, object?>
        {
            ["event_id"] = "550e8400-e29b-41d4-a716-446655440000",
            ["schema_version"] = "1",
            ["timestamp"] = "2024-06-01T12:00:00Z",
            ["tenant_id"] = "tenant-abc",
            ["domain_id"] = "domain-xyz",
            ["domain"] = "go.example.com",
            ["slug"] = "promo",
            ["destination_url"] = "https://example.com/promo",
            ["status_code"] = 302
        };
        dict.Remove(missingField);

        var (result, reason) = Function.ProcessRecord(Encode(dict));

        result.Should().Be(FirehoseResult.ProcessingFailed);
        reason.Should().NotBeNullOrEmpty();
    }

    // ── Validation: schema_version ────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void ProcessRecord_WithUnknownSchemaVersion_ReturnsProcessingFailed()
    {
        var (result, reason) = Function.ProcessRecord(Encode(ValidPayload(schemaVersion: "99")));

        result.Should().Be(FirehoseResult.ProcessingFailed);
        reason.Should().Contain("schema_version");
    }

    // ── Validation: status_code ───────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(308)]
    public void ProcessRecord_WithValidStatusCode_ReturnsOk(int statusCode)
    {
        var (result, _) = Function.ProcessRecord(Encode(ValidPayload(statusCode: statusCode)));

        result.Should().Be(FirehoseResult.Ok);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(200)]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(0)]
    public void ProcessRecord_WithInvalidStatusCode_ReturnsProcessingFailed(int statusCode)
    {
        var (result, reason) = Function.ProcessRecord(Encode(ValidPayload(statusCode: statusCode)));

        result.Should().Be(FirehoseResult.ProcessingFailed);
        reason.Should().Contain("status_code");
    }

    // ── Validation: PII hashing ───────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void ProcessRecord_WithValidIpHash_ReturnsOk()
    {
        var validHash = new string('a', 64); // 64 lowercase hex chars
        var (result, _) = Function.ProcessRecord(Encode(ValidPayload(ipHash: validHash)));

        result.Should().Be(FirehoseResult.Ok);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ProcessRecord_WithValidUserAgentHash_ReturnsOk()
    {
        var validHash = new string('f', 64);
        var (result, _) = Function.ProcessRecord(Encode(ValidPayload(userAgentHash: validHash)));

        result.Should().Be(FirehoseResult.Ok);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("192.168.1.1")]                       // raw IP — not hashed
    [InlineData("abc")]                               // too short
    [InlineData("AABBCCDDEEFF00112233445566778899AABBCCDDEEFF00112233445566778899")] // uppercase
    public void ProcessRecord_WithInvalidIpHash_ReturnsProcessingFailed(string ipHash)
    {
        var (result, reason) = Function.ProcessRecord(Encode(ValidPayload(ipHash: ipHash)));

        result.Should().Be(FirehoseResult.ProcessingFailed);
        reason.Should().Contain("ip_hash");
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("Mozilla/5.0")]  // raw User-Agent — not hashed
    [InlineData("abc")]          // too short
    public void ProcessRecord_WithInvalidUserAgentHash_ReturnsProcessingFailed(string userAgentHash)
    {
        var (result, reason) = Function.ProcessRecord(Encode(ValidPayload(userAgentHash: userAgentHash)));

        result.Should().Be(FirehoseResult.ProcessingFailed);
        reason.Should().Contain("user_agent_hash");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ProcessRecord_WithNullIpHash_ReturnsOk()
    {
        var (result, _) = Function.ProcessRecord(Encode(ValidPayload(ipHash: null)));

        result.Should().Be(FirehoseResult.Ok);
    }

    // ── Malformed input ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void ProcessRecord_WithInvalidBase64_ReturnsProcessingFailed()
    {
        var (result, reason) = Function.ProcessRecord("not-valid-base64!!!");

        result.Should().Be(FirehoseResult.ProcessingFailed);
        reason.Should().Contain("JSON decode error");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ProcessRecord_WithMalformedJson_ReturnsProcessingFailed()
    {
        var bad = Convert.ToBase64String(Encoding.UTF8.GetBytes("{ not json }"));

        var (result, reason) = Function.ProcessRecord(bad);

        result.Should().Be(FirehoseResult.ProcessingFailed);
        reason.Should().Contain("JSON decode error");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ProcessRecord_WithEmptyJson_ReturnsProcessingFailed()
    {
        var empty = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"));

        var (result, reason) = Function.ProcessRecord(empty);

        result.Should().Be(FirehoseResult.ProcessingFailed);
        reason.Should().Contain("event_id");
    }

    // ── Batch processing ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void HandleAsync_BatchWithMixedValidity_ProcessesEachRecordIndependently()
    {
        var function = new Function();
        var valid = Encode(ValidPayload());
        var invalid = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"));

        var firehoseEvent = BuildFirehoseEvent(valid, invalid, valid);
        var response = function.HandleAsync(firehoseEvent, new TestLambdaContext());

        response.Records.Should().HaveCount(3);
        response.Records[0].Result.Should().Be(FirehoseResult.Ok);
        response.Records[1].Result.Should().Be(FirehoseResult.ProcessingFailed);
        response.Records[2].Result.Should().Be(FirehoseResult.Ok);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void HandleAsync_ReturnsOriginalDataUnchanged()
    {
        var function = new Function();
        var data = Encode(ValidPayload());
        var firehoseEvent = BuildFirehoseEvent(data);

        var response = function.HandleAsync(firehoseEvent, new TestLambdaContext());

        response.Records[0].Data.Should().Be(data);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void HandleAsync_PreservesRecordIds()
    {
        var function = new Function();
        var firehoseEvent = new FirehoseEvent
        {
            InvocationId = "inv-1",
            DeliveryStreamArn = "arn:aws:firehose:us-east-1:123:deliverystream/clicks",
            Region = "us-east-1",
            Records =
            [
                new FirehoseRecord { RecordId = "my-record-id-abc", ApproximateArrivalTimestamp = 0, Data = Encode(ValidPayload()) }
            ]
        };

        var response = function.HandleAsync(firehoseEvent, new TestLambdaContext());

        response.Records[0].RecordId.Should().Be("my-record-id-abc");
    }

    // ── Direct validator tests ────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void Validate_WithMinimalValidEvent_ReturnsValid()
    {
        var e = new ClickEvent
        {
            EventId = "550e8400-e29b-41d4-a716-446655440000",
            SchemaVersion = "1",
            Timestamp = DateTimeOffset.UtcNow,
            TenantId = "tenant-abc",
            DomainId = "domain-xyz",
            Domain = "go.example.com",
            Slug = "promo",
            DestinationUrl = "https://example.com/promo",
            StatusCode = 302
        };

        var (valid, reason) = Function.Validate(e);

        valid.Should().BeTrue();
        reason.Should().BeNull();
    }
}
