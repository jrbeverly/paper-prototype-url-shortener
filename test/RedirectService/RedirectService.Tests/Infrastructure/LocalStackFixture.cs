using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Testcontainers.LocalStack;

namespace RedirectService.Tests.Infrastructure;

/// <summary>
/// Manages a LocalStack DynamoDB instance for integration tests.
/// Starts a container unless <c>DYNAMODB_ENDPOINT</c> is set (for CI with a service container).
/// </summary>
public sealed class LocalStackFixture : IAsyncLifetime
{
    private readonly LocalStackContainer? _container;
    private readonly string? _endpointOverride;

    public IAmazonDynamoDB DynamoDb { get; private set; } = null!;
    public string TableName => "test-redirects";

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

        // Redirect records: PK=HOST#{hostname}#SLUG#{slug} / SK=CONFIG
        // Domain config records: PK=HOST#{hostname} / SK=DOMAIN_CONFIG
        // Both share this table with no GSI required (all lookups are GetItem by known PK+SK).
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
                new AttributeDefinition("SK", ScalarAttributeType.S)
            ]
        };

        await DynamoDb.CreateTableAsync(request);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
        DynamoDb?.Dispose();
    }
}
