using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using RedirectService.Api.Models;

namespace RedirectService.Api.Repositories;

/// <summary>
/// DynamoDB implementation of <see cref="IDomainConfigRepository"/>.
/// Reads the domain-level configuration item at <c>HOST#{hostname}</c> / <c>DOMAIN_CONFIG</c>
/// from the same single-table as link records (<c>REDIRECTS_TABLE_NAME</c>).
/// Invalid field values (bad hex colors, non-http/https URLs, oversized or injection-prone CSS)
/// are silently treated as absent so callers receive a safe record.
/// </summary>
public sealed class DynamoDbDomainConfigRepository(IAmazonDynamoDB dynamoDb, string tableName)
    : IDomainConfigRepository
{
    /// <inheritdoc/>
    public async Task<DomainConfig?> GetAsync(string hostname, CancellationToken ct = default)
    {
        var response = await dynamoDb.GetItemAsync(
            new GetItemRequest
            {
                TableName = tableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["PK"] = new AttributeValue { S = $"HOST#{hostname}" },
                    ["SK"] = new AttributeValue { S = "DOMAIN_CONFIG" }
                },
                // PK is included so IsItemSet returns true even when all optional fields are absent.
                ProjectionExpression = "#pk, #fallbackUrl, #brandColor, #logoUrl, #customMessage, #supportUrl, #customCss",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#pk"] = "PK",
                    ["#fallbackUrl"] = "FallbackUrl",
                    ["#brandColor"] = "BrandColor",
                    ["#logoUrl"] = "LogoUrl",
                    ["#customMessage"] = "CustomMessage",
                    ["#supportUrl"] = "SupportUrl",
                    ["#customCss"] = "CustomCss"
                }
            }, ct);

        if (!response.IsItemSet)
            return null;

        var item = response.Item;
        return new DomainConfig
        {
            Hostname = hostname,
            FallbackUrl = ParseSafeUrl(item.TryGetValue("FallbackUrl", out var fu) ? fu.S : null),
            BrandColor = ParseHexColor(item.TryGetValue("BrandColor", out var bc) ? bc.S : null),
            LogoUrl = ParseSafeUrl(item.TryGetValue("LogoUrl", out var lu) ? lu.S : null),
            CustomMessage = ParseCustomMessage(item.TryGetValue("CustomMessage", out var cm) ? cm.S : null),
            SupportUrl = ParseSafeUrl(item.TryGetValue("SupportUrl", out var su) ? su.S : null),
            CustomCss = ParseCustomCss(item.TryGetValue("CustomCss", out var cc) ? cc.S : null),
        };
    }

    // Accepts only http/https absolute URLs. Non-conforming values are dropped to prevent
    // header injection (Location) or attribute injection (<img src>, href).
    private static string? ParseSafeUrl(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? value
            : null;
    }

    // Accepts only #RRGGBB or #RGB hex strings to prevent CSS injection.
    private static string? ParseHexColor(string? value)
    {
        if (value is null) return null;
        if (value.Length is not 7 and not 4) return null;
        if (value[0] != '#') return null;
        foreach (var c in value.AsSpan(1))
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return null;
        }
        return value;
    }

    // Trims whitespace and truncates to 500 characters.
    // HTML-encoding happens at render time, so no filtering here.
    private static string? ParseCustomMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > 500 ? trimmed[..500] : trimmed;
    }

    // Rejects CSS that exceeds 2 000 chars or contains sequences that could
    // break out of the enclosing <style> block or inject script elements.
    private static string? ParseCustomCss(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Length > 2000) return null;
        if (value.Contains("</style", StringComparison.OrdinalIgnoreCase)) return null;
        if (value.Contains("<script", StringComparison.OrdinalIgnoreCase)) return null;
        return value;
    }
}
