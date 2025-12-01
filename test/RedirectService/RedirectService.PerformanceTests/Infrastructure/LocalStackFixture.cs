using Amazon.Runtime;
using Testcontainers.LocalStack;

namespace RedirectService.PerformanceTests.Infrastructure;

/// <summary>
/// Manages a LocalStack DynamoDB instance for performance tests.
/// Starts a container unless <c>DYNAMODB_ENDPOINT</c> is set (for CI with a service container
/// or a real DynamoDB endpoint when validating production latency targets).
/// </summary>
public sealed class LocalStackFixture : IAsyncLifetime
{
    private readonly LocalStackContainer? _container;
    private readonly string? _endpointOverride;

    public IAmazonDynamoDB DynamoDb { get; private set; } = null!;
    public string TableName => "perf-redirects";

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

        await DynamoDb.CreateTableAsync(new CreateTableRequest
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
        });
    }

    /// <summary>Seeds a redirect link into the table for use in benchmarks.</summary>
    public async Task SeedLinkAsync(
        string hostname,
        string slug,
        string destination = "https://example.com/landing",
        int redirectType = 302)
    {
        await DynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = TableName,
            Item = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = $"HOST#{hostname}#SLUG#{slug}" },
                ["SK"] = new AttributeValue { S = "CONFIG" },
                ["TenantId"] = new AttributeValue { S = "perf-tenant" },
                ["DomainId"] = new AttributeValue { S = "perf-domain" },
                ["Hostname"] = new AttributeValue { S = hostname },
                ["Slug"] = new AttributeValue { S = slug },
                ["DestinationUrl"] = new AttributeValue { S = destination },
                ["RedirectType"] = new AttributeValue { N = redirectType.ToString() },
                ["Status"] = new AttributeValue { S = "active" },
                ["CurrentClicks"] = new AttributeValue { N = "0" }
            }
        });
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
        DynamoDb?.Dispose();
    }
}
