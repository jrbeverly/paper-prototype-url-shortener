using Amazon.DynamoDBv2;

namespace RedirectService.Tests.Repositories;

public sealed class DynamoDbRedirectRepositoryTests
{
    private const string _tableName = "test-redirects";
    private const string _hostname = "go.example.com";
    private const string _slug = "test-slug";
    private const string _destination = "https://example.com/landing";

    private static DynamoDbRedirectRepository CreateRepository(StubDynamoDB stub)
        => new(stub, _tableName);

    private static Dictionary<string, AttributeValue> BuildItem(
        string? destination = null,
        string? status = "active",
        int redirectType = 302,
        string? expiresAt = null,
        int? maxClicks = null,
        long currentClicks = 0)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["TenantId"] = new AttributeValue { S = "tenant-123" },
            ["DomainId"] = new AttributeValue { S = "domain-456" },
            ["Hostname"] = new AttributeValue { S = _hostname },
            ["Slug"] = new AttributeValue { S = _slug },
            ["DestinationUrl"] = new AttributeValue { S = destination ?? _destination },
            ["RedirectType"] = new AttributeValue { N = redirectType.ToString() },
            ["CurrentClicks"] = new AttributeValue { N = currentClicks.ToString() }
        };
        if (status is not null)
            item["Status"] = new AttributeValue { S = status };
        if (expiresAt is not null)
            item["ExpiresAt"] = new AttributeValue { S = expiresAt };
        if (maxClicks.HasValue)
            item["MaxClicks"] = new AttributeValue { N = maxClicks.Value.ToString() };
        return item;
    }

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_ActiveLink_ReturnsRecord()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse { Item = BuildItem() });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname, _slug);

        result.Should().NotBeNull();
        result!.DestinationUrl.Should().Be(_destination);
        result.Status.Should().Be("active");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_ActiveLink_MapsAllFields()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse { Item = BuildItem(redirectType: 301, maxClicks: 100, currentClicks: 42) });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname, _slug);

        result.Should().NotBeNull();
        result!.TenantId.Should().Be("tenant-123");
        result.DomainId.Should().Be("domain-456");
        result.Hostname.Should().Be(_hostname);
        result.Slug.Should().Be(_slug);
        result.RedirectType.Should().Be(301);
        result.ExpiresAt.Should().BeNull();
        result.MaxClicks.Should().Be(100);
        result.CurrentClicks.Should().Be(42);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_LinkWithFutureExpiry_ReturnsRecord()
    {
        var stub = new StubDynamoDB();
        var future = DateTime.UtcNow.AddDays(1).ToString("O");
        stub.Returns(new GetItemResponse { Item = BuildItem(expiresAt: future) });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname, _slug);

        result.Should().NotBeNull();
        result!.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_MissingRedirectType_DefaultsTo302()
    {
        var stub = new StubDynamoDB();
        var item = BuildItem();
        item.Remove("RedirectType");
        stub.Returns(new GetItemResponse { Item = item });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname, _slug);

        result!.RedirectType.Should().Be(302);
    }

    // ── Null cases ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_ItemNotFound_ReturnsNull()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse()); // empty = not found
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname, _slug);

        result.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_SuspendedLink_ReturnsRecord()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse { Item = BuildItem(status: "suspended") });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname, _slug);

        result.Should().NotBeNull();
        result!.Status.Should().Be("suspended");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_QuarantinedLink_ReturnsRecord()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse { Item = BuildItem(status: "quarantined") });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname, _slug);

        result.Should().NotBeNull();
        result!.Status.Should().Be("quarantined");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_DeletedLink_ReturnsNull()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse { Item = BuildItem(status: "deleted") });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname, _slug);

        result.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_ExpiredLink_ReturnsRecordWithPastExpiry()
    {
        // Expiration is now evaluated by RedirectService, not the repository.
        // The repository returns the record so the service can distinguish "expired" from "not found".
        var stub = new StubDynamoDB();
        var past = DateTime.UtcNow.AddDays(-1).ToString("O");
        stub.Returns(new GetItemResponse { Item = BuildItem(expiresAt: past) });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname, _slug);

        result.Should().NotBeNull();
        result!.ExpiresAt.Should().BeBefore(DateTime.UtcNow);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_MissingDestinationUrl_ReturnsNull()
    {
        var stub = new StubDynamoDB();
        var item = BuildItem();
        item.Remove("DestinationUrl");
        stub.Returns(new GetItemResponse { Item = item });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname, _slug);

        result.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_MissingStatus_TreatsAsActive()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse { Item = BuildItem(status: null) });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname, _slug);

        result.Should().NotBeNull();
    }

    // ── Key structure ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_UsesCompositeHostSlugKey()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse());
        var repo = CreateRepository(stub);

        await repo.GetAsync("go.customer.com", "summer-sale");

        stub.LastRequest.Should().NotBeNull();
        stub.LastRequest!.Key["PK"].S.Should().Be("HOST#go.customer.com#SLUG#summer-sale");
        stub.LastRequest.Key["SK"].S.Should().Be("CONFIG");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_QueriesCorrectTable()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse());
        var repo = CreateRepository(stub);

        await repo.GetAsync(_hostname, _slug);

        stub.LastRequest!.TableName.Should().Be(_tableName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_UsesProjectionExpression()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse());
        var repo = CreateRepository(stub);

        await repo.GetAsync(_hostname, _slug);

        stub.LastRequest!.ProjectionExpression.Should().NotBeNullOrEmpty();
        stub.LastRequest.ExpressionAttributeNames.Should().NotBeEmpty();
    }

    // ── Error propagation ────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_ThrottlingException_Propagates()
    {
        var stub = new StubDynamoDB();
        stub.Throws(new ProvisionedThroughputExceededException("Throttled"));
        var repo = CreateRepository(stub);

        var act = () => repo.GetAsync(_hostname, _slug);

        await act.Should().ThrowAsync<ProvisionedThroughputExceededException>();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_TransientDynamoException_Propagates()
    {
        var stub = new StubDynamoDB();
        stub.Throws(new AmazonDynamoDBException("Service unavailable"));
        var repo = CreateRepository(stub);

        var act = () => repo.GetAsync(_hostname, _slug);

        await act.Should().ThrowAsync<AmazonDynamoDBException>();
    }

    // ── TryIncrementClickAsync ────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TryIncrementClickAsync_Success_ReturnsTrue()
    {
        var stub = new StubDynamoDB();
        var repo = CreateRepository(stub);

        var result = await repo.TryIncrementClickAsync(_hostname, _slug);

        result.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TryIncrementClickAsync_UsesCompositeKey()
    {
        var stub = new StubDynamoDB();
        var repo = CreateRepository(stub);

        await repo.TryIncrementClickAsync("go.customer.com", "summer-sale");

        stub.LastUpdateRequest.Should().NotBeNull();
        stub.LastUpdateRequest!.Key["PK"].S.Should().Be("HOST#go.customer.com#SLUG#summer-sale");
        stub.LastUpdateRequest.Key["SK"].S.Should().Be("CONFIG");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TryIncrementClickAsync_UsesUpdateExpression()
    {
        var stub = new StubDynamoDB();
        var repo = CreateRepository(stub);

        await repo.TryIncrementClickAsync(_hostname, _slug);

        stub.LastUpdateRequest.Should().NotBeNull();
        stub.LastUpdateRequest!.UpdateExpression.Should().Be("ADD CurrentClicks :inc");
        stub.LastUpdateRequest.ExpressionAttributeValues.Should().ContainKey(":inc");
        stub.LastUpdateRequest.ExpressionAttributeValues[":inc"].N.Should().Be("1");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TryIncrementClickAsync_ConditionCheckFails_ReturnsFalse()
    {
        var stub = new StubDynamoDB();
        stub.SetUpdateConditionalCheckFails(true);
        var repo = CreateRepository(stub);

        var result = await repo.TryIncrementClickAsync(_hostname, _slug);

        result.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TryIncrementClickAsync_TransientException_Propagates()
    {
        var stub = new StubDynamoDB();
        stub.SetUpdateThrows(new AmazonDynamoDBException("Service unavailable"));
        var repo = CreateRepository(stub);

        var act = () => repo.TryIncrementClickAsync(_hostname, _slug);

        await act.Should().ThrowAsync<AmazonDynamoDBException>();
    }
}
