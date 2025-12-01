using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Testcontainers.LocalStack;

namespace ControlPlane.Tests.Infrastructure;

public sealed class LocalStackFixture : IAsyncLifetime
{
    private readonly LocalStackContainer? _container;
    private readonly string? _endpointOverride;

    public IAmazonDynamoDB DynamoDb { get; private set; } = null!;
    public string TableName => "control-plane-api-keys";

    public LocalStackFixture()
    {
        _endpointOverride = Environment.GetEnvironmentVariable("DYNAMODB_ENDPOINT");
        if (string.IsNullOrEmpty(_endpointOverride))
        {
            _container = new LocalStackBuilder()
                .WithImage("localstack/localstack:3.8.1")
                .Build();
        }
    }

    public async Task InitializeAsync()
    {
        if (_container is not null)
        {
            await _container.StartAsync();
            var serviceUrl = _container.GetConnectionString();
            var config = new AmazonDynamoDBConfig { ServiceURL = serviceUrl };
            DynamoDb = new AmazonDynamoDBClient(new AnonymousAWSCredentials(), config);
        }
        else
        {
            var config = new AmazonDynamoDBConfig { ServiceURL = _endpointOverride };
            DynamoDb = new AmazonDynamoDBClient(new AnonymousAWSCredentials(), config);
        }

        await CreateTableAsync();
    }

    private async Task CreateTableAsync()
    {
        var existingTables = await DynamoDb.ListTablesAsync();
        if (existingTables.TableNames.Contains(TableName))
            return;

        var request = new CreateTableRequest
        {
            TableName = TableName,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            KeySchema =
            [
                new KeySchemaElement("PK", KeyType.HASH),
                new KeySchemaElement("SK", KeyType.RANGE)
            ],
            AttributeDefinitions =
            [
                new AttributeDefinition("PK", ScalarAttributeType.S),
                new AttributeDefinition("SK", ScalarAttributeType.S),
                new AttributeDefinition("GSI1PK", ScalarAttributeType.S)
            ],
            GlobalSecondaryIndexes =
            [
                new GlobalSecondaryIndex
                {
                    IndexName = "KeyHashIndex",
                    KeySchema =
                    [
                        new KeySchemaElement("GSI1PK", KeyType.HASH)
                    ],
                    Projection = new Projection { ProjectionType = ProjectionType.ALL }
                }
            ]
        };

        await DynamoDb.CreateTableAsync(request);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
        DynamoDb?.Dispose();
    }
}
