using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using ControlPlane.Api.Services;

namespace ControlPlane.Tests.Infrastructure;

public sealed class DynamoDbApiKeyRepository : IApiKeyRepository
{
    private readonly IAmazonDynamoDB _client;
    private readonly string _tableName;

    public DynamoDbApiKeyRepository(IAmazonDynamoDB client, string tableName)
    {
        _client = client;
        _tableName = tableName;
    }

    public async Task<ApiKeyEntity> CreateAsync(ApiKeyEntity entity)
    {
        var item = ToAttributes(entity);
        await _client.PutItemAsync(new PutItemRequest
        {
            TableName = _tableName,
            Item = item
        });
        return entity;
    }

    public async Task<List<ApiKeyEntity>> GetByTenantAsync(Guid tenantId)
    {
        var response = await _client.QueryAsync(new QueryRequest
        {
            TableName = _tableName,
            KeyConditionExpression = "PK = :pk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new AttributeValue { S = $"TENANT#{tenantId}" }
            }
        });

        return response.Items.Select(FromAttributes).ToList();
    }

    public async Task<ApiKeyEntity?> GetByIdAsync(Guid tenantId, Guid keyId)
    {
        var response = await _client.GetItemAsync(new GetItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new AttributeValue { S = $"TENANT#{tenantId}" },
                ["SK"] = new AttributeValue { S = $"KEY#{keyId}" }
            }
        });

        return response.IsItemSet ? FromAttributes(response.Item) : null;
    }

    public async Task<ApiKeyEntity?> GetByHashAsync(string hash)
    {
        var response = await _client.QueryAsync(new QueryRequest
        {
            TableName = _tableName,
            IndexName = "KeyHashIndex",
            KeyConditionExpression = "GSI1PK = :hash",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":hash"] = new AttributeValue { S = hash }
            }
        });

        return response.Items.Count > 0 ? FromAttributes(response.Items[0]) : null;
    }

    public async Task<bool> RevokeAsync(Guid tenantId, Guid keyId)
    {
        try
        {
            await _client.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["PK"] = new AttributeValue { S = $"TENANT#{tenantId}" },
                    ["SK"] = new AttributeValue { S = $"KEY#{keyId}" }
                },
                UpdateExpression = "SET is_revoked = :val",
                ConditionExpression = "attribute_not_exists(is_revoked) OR is_revoked = :false",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":val"] = new AttributeValue { BOOL = true },
                    [":false"] = new AttributeValue { BOOL = false }
                },
                ReturnValues = ReturnValue.ALL_NEW
            });
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    private static Dictionary<string, AttributeValue> ToAttributes(ApiKeyEntity entity)
    {
        return new Dictionary<string, AttributeValue>
        {
            ["PK"] = new AttributeValue { S = $"TENANT#{entity.TenantId}" },
            ["SK"] = new AttributeValue { S = $"KEY#{entity.Id}" },
            ["GSI1PK"] = new AttributeValue { S = entity.KeyHash },
            ["id"] = new AttributeValue { S = entity.Id.ToString() },
            ["tenant_id"] = new AttributeValue { S = entity.TenantId.ToString() },
            ["name"] = new AttributeValue { S = entity.Name },
            ["key_hash"] = new AttributeValue { S = entity.KeyHash },
            ["key_prefix"] = new AttributeValue { S = entity.KeyPrefix },
            ["created_at"] = new AttributeValue { S = entity.CreatedAt.ToString("O") },
            ["is_revoked"] = new AttributeValue { BOOL = entity.IsRevoked },
            ["permissions"] = new AttributeValue { SS = entity.Permissions }
        };
    }

    private static ApiKeyEntity FromAttributes(Dictionary<string, AttributeValue> item)
    {
        return new ApiKeyEntity
        {
            Id = Guid.Parse(item["id"].S),
            TenantId = Guid.Parse(item["tenant_id"].S),
            Name = item["name"].S,
            KeyHash = item["key_hash"].S,
            KeyPrefix = item["key_prefix"].S,
            CreatedAt = DateTime.Parse(item["created_at"].S, null, System.Globalization.DateTimeStyles.RoundtripKind),
            Permissions = item.TryGetValue("permissions", out var perms) && perms.SS?.Count > 0
                ? perms.SS
                : [],
            IsRevoked = item.TryGetValue("is_revoked", out var revoked) && revoked.BOOL
        };
    }
}
