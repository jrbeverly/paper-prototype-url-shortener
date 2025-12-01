namespace RedirectService.Tests.Infrastructure;

[CollectionDefinition("DynamoDB")]
public sealed class DynamoDbCollection : ICollectionFixture<LocalStackFixture>
{
}
