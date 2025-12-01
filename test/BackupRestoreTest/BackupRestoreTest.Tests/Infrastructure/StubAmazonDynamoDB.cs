using System.Net;
using Amazon.DynamoDBv2;
using Amazon.Runtime;

namespace BackupRestoreTest.Tests.Infrastructure;

/// <summary>
/// Controllable DynamoDB stub for unit tests.
/// Records requests and returns configurable responses for the operations
/// used by the restore test Lambda.
/// </summary>
internal sealed class StubAmazonDynamoDB : AmazonDynamoDBClient
{
    private bool _restoreThrows;
    private Exception? _restoreException;
    private TableStatus _describeStatus = TableStatus.ACTIVE;
    private int _describeCallCount;
    private readonly long _itemCount;
    private bool _deleteTableThrows;
    private Exception? _deleteTableException;
    private bool _describeTableThrows;
    private Exception? _describeTableExceptionUsedForRestore;
    private readonly List<AttributeDefinition> _attributeDefinitions;
    private readonly List<GlobalSecondaryIndexDescription>? _globalSecondaryIndexes;

    public StubAmazonDynamoDB()
        : base(
            new BasicAWSCredentials("test-key", "test-secret"),
            new AmazonDynamoDBConfig { ServiceURL = "http://localhost:1", MaxErrorRetry = 0 })
    {
        _itemCount = 42;
        _attributeDefinitions = [
            new AttributeDefinition { AttributeName = "PK", AttributeType = "S" },
            new AttributeDefinition { AttributeName = "SK", AttributeType = "S" },
            new AttributeDefinition { AttributeName = "GSI1PK", AttributeType = "S" },
            new AttributeDefinition { AttributeName = "GSI1SK", AttributeType = "S" }
        ];
        _globalSecondaryIndexes = [
            new GlobalSecondaryIndexDescription
            {
                IndexName = "GSI1",
                KeySchema = [
                    new KeySchemaElement { AttributeName = "GSI1PK", KeyType = "HASH" },
                    new KeySchemaElement { AttributeName = "GSI1SK", KeyType = "RANGE" }
                ]
            }
        ];
    }

    /// <summary>Last restore table request received.</summary>
    public RestoreTableToPointInTimeRequest? LastRestoreRequest { get; private set; }

    /// <summary>Table names passed to DescribeTable.</summary>
    public List<string> DescribeTableNames { get; } = [];

    /// <summary>Last table name passed to DeleteTable.</summary>
    public string? LastDeleteTableName { get; private set; }

    /// <summary>Number of times DescribeTable was called.</summary>
    public int DescribeCallCount => _describeCallCount;

    // ── Configuration methods ──────────────────────────────────────────

    public void SetRestoreThrows(Exception exception)
    {
        _restoreThrows = true;
        _restoreException = exception;
    }

    /// <summary>Set the status returned by DescribeTable. First call returns
    /// CREATING; subsequent calls return the configured status.</summary>
    public void SetDescribeStatus(TableStatus status) => _describeStatus = status;

    public void SetItemCount(long count) => _ = count;

    /// <summary>Override attributes for testing schema validation failures.</summary>
    public void SetExtraAttribute(string name, string type)
    {
        _attributeDefinitions.Add(new AttributeDefinition { AttributeName = name, AttributeType = type });
    }

    /// <summary>Remove a required attribute for testing missing attribute detection.</summary>
    public void RemoveGs1Index()
    {
        _globalSecondaryIndexes!.Clear();
    }

    public void SetDescribeTableThrows(Exception exception)
    {
        _describeTableThrows = true;
        _describeTableExceptionUsedForRestore = exception;
    }

    public void SetDeleteTableThrows(Exception exception)
    {
        _deleteTableThrows = true;
        _deleteTableException = exception;
    }

    // ── Overrides ──────────────────────────────────────────────────────

    public override Task<RestoreTableToPointInTimeResponse> RestoreTableToPointInTimeAsync(
        RestoreTableToPointInTimeRequest request, CancellationToken ct = default)
    {
        LastRestoreRequest = request;

        if (_restoreThrows)
            throw _restoreException!;

        return Task.FromResult(new RestoreTableToPointInTimeResponse
        {
            HttpStatusCode = HttpStatusCode.OK
        });
    }

    public override Task<DescribeTableResponse> DescribeTableAsync(
        string tableName, CancellationToken ct = default)
    {
        DescribeTableNames.Add(tableName);
        _describeCallCount++;

        if (_describeTableThrows)
            throw _describeTableExceptionUsedForRestore!;

        return Task.FromResult(new DescribeTableResponse
        {
            Table = new TableDescription
            {
                TableName = tableName,
                TableStatus = _describeStatus,
                ItemCount = _itemCount,
                AttributeDefinitions = _attributeDefinitions,
                GlobalSecondaryIndexes = _globalSecondaryIndexes
            }
        });
    }

    public override Task<DeleteTableResponse> DeleteTableAsync(
        string tableName, CancellationToken ct = default)
    {
        LastDeleteTableName = tableName;

        if (_deleteTableThrows)
            throw _deleteTableException!;

        return Task.FromResult(new DeleteTableResponse
        {
            HttpStatusCode = HttpStatusCode.OK
        });
    }
}
