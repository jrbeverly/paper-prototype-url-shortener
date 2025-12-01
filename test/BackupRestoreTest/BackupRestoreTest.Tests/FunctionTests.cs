using System.Text;
using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Moq;
using RestoreFn = BackupRestoreTest.Function.Function;

namespace BackupRestoreTest.Tests;

public sealed class FunctionTests
{
    private const string _sourceTable = "redirects-prod";

    private static Stream EmptyEventStream()
        => new MemoryStream(Encoding.UTF8.GetBytes("{}"));

    private static ILambdaContext TestContext
    {
        get
        {
            var mock = new Mock<ILambdaContext>();
            mock.Setup(c => c.Logger).Returns(new Mock<ILambdaLogger>().Object);
            mock.Setup(c => c.FunctionName).Returns("restore-test");
            mock.Setup(c => c.AwsRequestId).Returns("test-request-id");
            return mock.Object;
        }
    }

    // ── Happy path ───────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_SuccessfulRestore_ReturnsPassed()
    {
        var stub = new StubAmazonDynamoDB();
        stub.SetDescribeStatus(TableStatus.ACTIVE);
        var fn = new RestoreFn(stub, _sourceTable);

        var result = await fn.HandleAsync(EmptyEventStream(), TestContext);

        result.Passed.Should().BeTrue();
        result.SchemaValid.Should().BeTrue();
        result.ItemCount.Should().Be(42);
        result.Error.Should().BeNull();
        stub.LastDeleteTableName.Should().NotBeNull();
        stub.LastRestoreRequest.Should().NotBeNull();
        stub.LastRestoreRequest!.SourceTableName.Should().Be(_sourceTable);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_RestoredTableName_ContainsDrTestSuffix()
    {
        var stub = new StubAmazonDynamoDB();
        stub.SetDescribeStatus(TableStatus.ACTIVE);
        var fn = new RestoreFn(stub, _sourceTable);

        var result = await fn.HandleAsync(EmptyEventStream(), TestContext);

        result.TestTable.Should().Contain($"{_sourceTable}-dr-test-");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_DeletesTestTableAfterValidation()
    {
        var stub = new StubAmazonDynamoDB();
        stub.SetDescribeStatus(TableStatus.ACTIVE);
        var fn = new RestoreFn(stub, _sourceTable);

        var result = await fn.HandleAsync(EmptyEventStream(), TestContext);

        stub.LastDeleteTableName.Should().Be(result.TestTable);
    }

    // ── Schema validation ────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_MissingGs1Index_FailsSchemaValidation()
    {
        var stub = new StubAmazonDynamoDB();
        stub.SetDescribeStatus(TableStatus.ACTIVE);
        stub.RemoveGs1Index();
        var fn = new RestoreFn(stub, _sourceTable);

        var result = await fn.HandleAsync(EmptyEventStream(), TestContext);

        result.Passed.Should().BeFalse();
        result.SchemaValid.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_NonZeroItemCount_PassesDataValidation()
    {
        var stub = new StubAmazonDynamoDB();
        stub.SetDescribeStatus(TableStatus.ACTIVE);
        var fn = new RestoreFn(stub, _sourceTable);

        var result = await fn.HandleAsync(EmptyEventStream(), TestContext);

        // Default item count from stub is 42; > 0 means data check passes.
        result.ItemCount.Should().Be(42);
        result.Passed.Should().BeTrue();
    }

    // ── Cleanup on failure ────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_RestoreException_StillAttemptsCleanup()
    {
        var stub = new StubAmazonDynamoDB();
        stub.SetRestoreThrows(new AmazonDynamoDBException("PITR not enabled"));
        var fn = new RestoreFn(stub, _sourceTable);

        await Assert.ThrowsAsync<AmazonDynamoDBException>(
            () => fn.HandleAsync(EmptyEventStream(), TestContext));

        stub.LastDeleteTableName.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_DescribeTableFails_StillAttemptsCleanup()
    {
        var stub = new StubAmazonDynamoDB();
        stub.SetRestoreThrows(new AmazonDynamoDBException("DescribeTable failed"));
        var fn = new RestoreFn(stub, _sourceTable);

        await Assert.ThrowsAsync<AmazonDynamoDBException>(
            () => fn.HandleAsync(EmptyEventStream(), TestContext));

        stub.LastDeleteTableName.Should().NotBeNull();
    }

    // ── SNS notification on failure ──────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_MissingSchema_SendsSnsNotification()
    {
        var stub = new StubAmazonDynamoDB();
        stub.SetDescribeStatus(TableStatus.ACTIVE);
        stub.RemoveGs1Index();

        var snsMock = new Mock<IAmazonSimpleNotificationService>();
        snsMock.Setup(s => s.PublishAsync(It.IsAny<PublishRequest>(), default))
            .ReturnsAsync(new PublishResponse());

        Environment.SetEnvironmentVariable("SNS_TOPIC_ARN", "arn:aws:sns:us-east-1:123456789:backup-alerts-prod");

        var fn = new RestoreFn(stub, _sourceTable, snsMock.Object);

        var result = await fn.HandleAsync(EmptyEventStream(), TestContext);

        result.Passed.Should().BeFalse();
        snsMock.Verify(s => s.PublishAsync(
            It.Is<PublishRequest>(r => r.Subject!.Contains("FAILED")),
            default), Times.Once);

        Environment.SetEnvironmentVariable("SNS_TOPIC_ARN", null);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HandleAsync_SnsNotConfigured_DoesNotThrow()
    {
        var stub = new StubAmazonDynamoDB();
        stub.SetDescribeStatus(TableStatus.ACTIVE);
        stub.RemoveGs1Index();
        var fn = new RestoreFn(stub, _sourceTable);

        var result = await fn.HandleAsync(EmptyEventStream(), TestContext);

        result.Passed.Should().BeFalse();
    }
}
