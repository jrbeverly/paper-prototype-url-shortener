namespace RedirectService.PerformanceTests.Infrastructure;

[CollectionDefinition("DynamoDB")]
public sealed class DynamoDbCollection : ICollectionFixture<LocalStackFixture>
{
}
