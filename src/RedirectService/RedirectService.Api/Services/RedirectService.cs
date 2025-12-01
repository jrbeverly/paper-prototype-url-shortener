using System.Security.Cryptography;
using System.Text;
using RedirectService.Api.Models;
using RedirectService.Api.Repositories;

namespace RedirectService.Api.Services;

/// <summary>
/// Core redirect resolution service.
/// Normalises the incoming host+slug, resolves through the repository, evaluates expiration,
/// builds the final destination URL (appending configured UTM parameters), and emits a click event.
/// </summary>
public sealed class RedirectService(
    IRedirectRepository repository,
    IClickEventEmitter clickEventEmitter) : IRedirectService
{
    public async Task<RedirectResult> ResolveAsync(RedirectRequest request, CancellationToken ct = default)
    {
        var hostname = NormalizeHostname(request.Hostname);
        var slug = NormalizeSlug(request.Slug);

        var record = await repository.GetAsync(hostname, slug, ct);
        if (record is null)
            return RedirectResult.NotFound();

        if (record.Status == "suspended")
            return RedirectResult.Suspended();

        if (record.Status == "quarantined")
            return RedirectResult.Quarantined();

        if (record.ExpiresAt.HasValue && record.ExpiresAt.Value <= DateTime.UtcNow)
            return RedirectResult.Expired("time");

        if (record.MaxClicks.HasValue && record.CurrentClicks >= record.MaxClicks.Value)
            return RedirectResult.Expired("clicks");

        var ok = await repository.TryIncrementClickAsync(hostname, slug, ct);
        if (!ok)
            return RedirectResult.Expired("clicks");

        var destination = BuildDestinationUrl(record.DestinationUrl, record.UtmParameters);

        try
        {
            await clickEventEmitter.EmitAsync(BuildClickEvent(record, destination, request), ct);
        }
        catch (Exception)
        {
            // Emission failure must not break the redirect response.
        }

        return RedirectResult.Redirect(destination, record.RedirectType);
    }

    private static ClickEvent BuildClickEvent(
        RedirectRecord record,
        string destination,
        RedirectRequest request)
    {
        var h = request.Headers;
        return new ClickEvent
        {
            EventId = Guid.NewGuid().ToString(),
            SchemaVersion = "1",
            Timestamp = DateTimeOffset.UtcNow,
            TenantId = record.TenantId,
            DomainId = record.DomainId,
            Domain = record.Hostname,
            Slug = record.Slug,
            DestinationUrl = destination,
            StatusCode = record.RedirectType,
            Country = GetHeader(h, "CloudFront-Viewer-Country"),
            // Region and City require extended CloudFront geographic headers not yet forwarded.
            Region = null,
            City = null,
            DeviceType = DeriveDeviceType(h),
            // Browser and OS require UA parsing; only the hash is captured here.
            Browser = null,
            Os = null,
            Referrer = GetHeader(h, "Referer"),
            UtmSource = record.UtmParameters?.GetValueOrDefault("utm_source"),
            UtmMedium = record.UtmParameters?.GetValueOrDefault("utm_medium"),
            UtmCampaign = record.UtmParameters?.GetValueOrDefault("utm_campaign"),
            BotScore = ParseBotScore(GetHeader(h, "X-Bot-Score")),
            // LatencyMs is set by the Lambda handler after ResolveAsync returns.
            LatencyMs = null,
            IpHash = HashField(ExtractIp(GetHeader(h, "CloudFront-Viewer-Address"))),
            UserAgentHash = HashField(GetHeader(h, "User-Agent"))
        };
    }

    private static string? GetHeader(IReadOnlyDictionary<string, string> headers, string name)
        => headers.TryGetValue(name, out var value) ? value : null;

    private static string? DeriveDeviceType(IReadOnlyDictionary<string, string> headers)
    {
        if (GetHeader(headers, "CloudFront-Is-Mobile-Viewer") == "true") return "mobile";
        if (GetHeader(headers, "CloudFront-Is-Tablet-Viewer") == "true") return "tablet";
        if (GetHeader(headers, "CloudFront-Is-SmartTV-Viewer") == "true") return "tv";
        if (GetHeader(headers, "CloudFront-Is-Desktop-Viewer") == "true") return "desktop";
        return null;
    }

    // CloudFront-Viewer-Address format: "IP:port" for both IPv4 and IPv6.
    // Strip the trailing :port so only the IP address is hashed.
    private static string? ExtractIp(string? viewerAddress)
    {
        if (string.IsNullOrEmpty(viewerAddress)) return null;
        var lastColon = viewerAddress.LastIndexOf(':');
        if (lastColon > 0)
        {
            var suffix = viewerAddress[(lastColon + 1)..];
            if (suffix.All(char.IsDigit))
                return viewerAddress[..lastColon];
        }
        return viewerAddress;
    }

    private static string? HashField(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static int? ParseBotScore(string? value)
        => int.TryParse(value, out var score) ? score : null;

    private static string NormalizeHostname(string hostname)
        => hostname.ToLowerInvariant().TrimEnd('.');

    private static string NormalizeSlug(string slug)
        => slug.TrimStart('/').ToLowerInvariant();

    private static string BuildDestinationUrl(
        string destinationUrl,
        IReadOnlyDictionary<string, string>? utmParameters)
    {
        if (utmParameters is null || utmParameters.Count == 0)
            return destinationUrl;

        var separator = destinationUrl.Contains('?') ? '&' : '?';
        var sb = new StringBuilder(destinationUrl).Append(separator);
        var first = true;
        foreach (var (key, value) in utmParameters)
        {
            if (!first) sb.Append('&');
            sb.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
            first = false;
        }
        return sb.ToString();
    }
}
