using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;

namespace ControlPlane.UnitTests.Infrastructure;

/// <summary>
/// Controllable DynamoDB stub for unit tests. Overrides only <see cref="ListTablesAsync"/>
/// (the sole method used by <c>HealthCheckService</c>) and tracks call count.
/// </summary>
internal sealed class TestAmazonDynamoDB : AmazonDynamoDBClient
{
    private int _listCallCount;

    public bool ShouldFail { get; set; }
    public int ListCallCount => Volatile.Read(ref _listCallCount);

    public TestAmazonDynamoDB()
        : base(
            new BasicAWSCredentials("test-key", "test-secret"),
            new AmazonDynamoDBConfig { ServiceURL = "http://localhost:1", MaxErrorRetry = 0 })
    {
    }

    public override Task<ListTablesResponse> ListTablesAsync(
        ListTablesRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _listCallCount);

        if (ShouldFail)
            throw new AmazonDynamoDBException("Simulated DynamoDB failure");

        return Task.FromResult(new ListTablesResponse { TableNames = ["test-table"] });
    }
}
