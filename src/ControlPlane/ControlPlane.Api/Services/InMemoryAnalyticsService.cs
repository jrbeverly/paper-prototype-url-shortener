using System.Globalization;
using ControlPlane.Api.Models.Responses;

namespace ControlPlane.Api.Services;

public sealed class InMemoryAnalyticsService : IAnalyticsService
{
    private static readonly string[] _countryCodes =
        ["US", "GB", "DE", "FR", "JP", "CA", "BR", "IN", "AU", "NL"];

    private static readonly string[] _countryNames =
        ["United States", "United Kingdom", "Germany", "France", "Japan", "Canada", "Brazil", "India", "Australia", "Netherlands"];

    private static readonly string[] _referrerDomains =
        ["google.com", "twitter.com", "linkedin.com", "github.com", "facebook.com", "reddit.com", "t.co", "newsletter.example.com"];

    public Task<LinkAnalyticsSummary> GetLinkAnalyticsAsync(
        Guid tenantId, Guid linkId, long totalClicks, DateTime createdAt)
    {
        var daysSinceCreation = Math.Max(1, (int)(DateTime.UtcNow - createdAt).TotalDays);
        var days = Math.Min(daysSinceCreation, 30);
        var rng = new Random(HashCode.Combine(tenantId, linkId));

        var clicksToday = totalClicks > 0
            ? Math.Max(1, (long)(totalClicks * (0.01 + rng.NextDouble() * 0.1)))
            : 0;

        var uniqueClicks = totalClicks > 0
            ? (long)(totalClicks * (0.6 + rng.NextDouble() * 0.3))
            : 0;

        var trend = new List<DailyClickCount>(days);
        var baseDate = DateTime.UtcNow.Date.AddDays(-days + 1);
        long remaining = totalClicks;
        for (int i = 0; i < days; i++)
        {
            if (i == days - 1)
            {
                trend.Add(new DailyClickCount { Date = baseDate.AddDays(i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Count = remaining });
            }
            else
            {
                var share = remaining / (days - i);
                var jitter = (long)(share * (rng.NextDouble() * 0.5 + 0.25));
                var val = Math.Min(remaining, Math.Max(0, jitter));
                if (val < 0) val = 0;
                trend.Add(new DailyClickCount { Date = baseDate.AddDays(i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Count = val });
                remaining -= val;
            }
        }

        var topCountries = _countryCodes
            .Take(rng.Next(4, 8))
            .Select((code, i) => new CountryBreakdown
            {
                CountryCode = code,
                CountryName = _countryNames[Array.IndexOf(_countryCodes, code)],
                Count = (long)(totalClicks * (0.3 - i * 0.035) * (0.5 + rng.NextDouble()))
            })
            .Where(c => c.Count > 0)
            .OrderByDescending(c => c.Count)
            .ToList();

        var topReferrers = _referrerDomains
            .Take(rng.Next(3, 7))
            .Select((domain, i) => new ReferrerBreakdown
            {
                Domain = domain,
                Count = (long)(totalClicks * (0.35 - i * 0.05) * (0.5 + rng.NextDouble()))
            })
            .Where(r => r.Count > 0)
            .OrderByDescending(r => r.Count)
            .ToList();

        var rawDesktop = (long)(uniqueClicks * (0.5 + rng.NextDouble() * 0.2));
        var rawMobile = (long)(uniqueClicks * (0.25 + rng.NextDouble() * 0.2));
        var rawTablet = uniqueClicks - rawDesktop - rawMobile;

        var devices = new DeviceBreakdown
        {
            Desktop = Math.Max(1, rawDesktop),
            Mobile = Math.Max(1, rawMobile),
            Tablet = Math.Max(0, rawTablet)
        };

        return Task.FromResult(new LinkAnalyticsSummary
        {
            TotalClicks = totalClicks,
            UniqueClicks = uniqueClicks,
            ClicksToday = clicksToday,
            ClickTrend = trend,
            TopCountries = topCountries,
            TopReferrers = topReferrers,
            Devices = devices
        });
    }

    public Task<IReadOnlyList<LinkAuditEntry>> GetAuditTrailAsync(
        Guid tenantId, Guid linkId, int currentVersion, DateTime createdAt, DateTime? updatedAt)
    {
        var entries = new List<LinkAuditEntry>
        {
            new()
            {
                Version = 1,
                Action = "created",
                Description = "Link created",
                Timestamp = createdAt
            }
        };

        if (currentVersion > 1 && updatedAt.HasValue)
        {
            entries.Add(new LinkAuditEntry
            {
                Version = currentVersion,
                Action = "updated",
                Description = "Link configuration updated",
                Timestamp = updatedAt.Value
            });
        }

        return Task.FromResult<IReadOnlyList<LinkAuditEntry>>(entries);
    }
}
