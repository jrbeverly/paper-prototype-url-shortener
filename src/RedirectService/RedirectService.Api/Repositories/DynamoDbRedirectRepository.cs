using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace RedirectService.Api.Repositories;

/// <summary>
/// DynamoDB implementation of <see cref="IRedirectRepository"/>.
/// Uses single-table design with composite key <c>HOST#{hostname}#SLUG#{slug}</c> / <c>CONFIG</c>.
/// The AWS SDK's built-in retry policy handles transient throttling; exceptions from exhausted retries propagate.
/// </summary>
public sealed class DynamoDbRedirectRepository : IRedirectRepository
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;

    public DynamoDbRedirectRepository(IAmazonDynamoDB dynamoDb, string tableName)
    {
        _dynamoDb = dynamoDb;
        _tableName = tableName;
    }

    /// <inheritdoc/>
    public async Task<RedirectRecord?> GetAsync(string hostname, string slug, CancellationToken ct = default)
    {
        var response = await _dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["PK"] = new AttributeValue { S = $"HOST#{hostname}#SLUG#{slug}" },
                    ["SK"] = new AttributeValue { S = "CONFIG" }
                },
                // ProjectionExpression with ExpressionAttributeNames avoids fetching
                // GSI keys and other large attributes not needed on the hot path,
                // and sidesteps conflicts with DynamoDB reserved words (e.g. Status, Name).
                ProjectionExpression = "#tenantId, #domainId, #hostname, #slug, #dest, #redirectType, #status, #expiresAt, #currentClicks, #maxClicks",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#tenantId"] = "TenantId",
                    ["#domainId"] = "DomainId",
                    ["#hostname"] = "Hostname",
                    ["#slug"] = "Slug",
                    ["#dest"] = "DestinationUrl",
                    ["#redirectType"] = "RedirectType",
                    ["#status"] = "Status",
                    ["#expiresAt"] = "ExpiresAt",
                    ["#currentClicks"] = "CurrentClicks",
                    ["#maxClicks"] = "MaxClicks"
                }
            }, ct);

        return response.IsItemSet ? FromAttributes(response.Item) : null;
    }

    /// <inheritdoc/>
    public async Task<bool> TryIncrementClickAsync(string hostname, string slug, CancellationToken ct = default)
    {
        try
        {
            await _dynamoDb.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["PK"] = new AttributeValue { S = $"HOST#{hostname}#SLUG#{slug}" },
                    ["SK"] = new AttributeValue { S = "CONFIG" }
                },
                UpdateExpression = "ADD CurrentClicks :inc",
                ConditionExpression = "attribute_not_exists(MaxClicks) OR attribute_not_exists(CurrentClicks) OR CurrentClicks < MaxClicks",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":inc"] = new AttributeValue { N = "1" }
                }
            }, ct);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    private static RedirectRecord? FromAttributes(Dictionary<string, AttributeValue> item)
    {
        // Active, suspended, and quarantined links are returned to the service layer for evaluation.
        // Deleted and other terminal statuses are treated as missing (null).
        var status = item.TryGetValue("Status", out var statusAttr) ? statusAttr.S ?? "active" : "active";
        if (status is not "active" and not "suspended" and not "quarantined")
            return null;

        // DestinationUrl is required; a missing or empty value indicates a malformed item.
        if (!item.TryGetValue("DestinationUrl", out var destAttr) || string.IsNullOrEmpty(destAttr.S))
            return null;

        // Expiration is evaluated by RedirectService, not here, so records are returned even when expired.
        DateTime? expiresAt = null;
        if (item.TryGetValue("ExpiresAt", out var expAttr) && expAttr.S is string expStr)
            expiresAt = DateTime.Parse(expStr, null, DateTimeStyles.RoundtripKind);

        int? maxClicks = null;
        if (item.TryGetValue("MaxClicks", out var mcAttr) && mcAttr.N is string mcStr && int.TryParse(mcStr, out var mcParsed))
            maxClicks = mcParsed;

        long currentClicks = 0;
        if (item.TryGetValue("CurrentClicks", out var ccAttr) && ccAttr.N is string ccStr && long.TryParse(ccStr, out var ccParsed))
            currentClicks = ccParsed;

        var redirectType = 302;
        if (item.TryGetValue("RedirectType", out var rtAttr) && rtAttr.N is string rtStr && int.TryParse(rtStr, out var rtParsed))
            redirectType = rtParsed;

        return new RedirectRecord
        {
            TenantId = item.TryGetValue("TenantId", out var tid) ? tid.S ?? "" : "",
            DomainId = item.TryGetValue("DomainId", out var did) ? did.S ?? "" : "",
            Hostname = item.TryGetValue("Hostname", out var h) ? h.S ?? "" : "",
            Slug = item.TryGetValue("Slug", out var sl) ? sl.S ?? "" : "",
            DestinationUrl = destAttr.S!,
            RedirectType = redirectType,
            Status = status,
            ExpiresAt = expiresAt,
            MaxClicks = maxClicks,
            CurrentClicks = currentClicks
        };
    }
}
