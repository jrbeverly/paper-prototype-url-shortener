namespace ControlPlane.Tests.Infrastructure;

[CollectionDefinition("DynamoDB")]
public sealed class DynamoDbCollection : ICollectionFixture<LocalStackFixture>
{
}
