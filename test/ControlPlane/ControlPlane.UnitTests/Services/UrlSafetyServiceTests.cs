using ControlPlane.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ControlPlane.UnitTests.Services;

public sealed class UrlSafetyServiceTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScanAsync_SafeUrl_ReturnsSafe()
    {
        var service = CreateService();

        var result = await service.ScanAsync("https://example.com");

        result.Verdict.Should().Be(UrlSafetyVerdict.Safe);
        result.Source.Should().Be("local");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScanAsync_BlockedHost_ReturnsMalicious()
    {
        var service = CreateService(blockedHosts: ["evil.com"]);

        var result = await service.ScanAsync("https://evil.com/phishing");

        result.Verdict.Should().Be(UrlSafetyVerdict.Malicious);
        result.Source.Should().Be("blocklist");
        result.Reason.Should().Contain("evil.com");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScanAsync_BlockedHostSubdomain_ReturnsMalicious()
    {
        var service = CreateService(blockedHosts: ["evil.com"]);

        var result = await service.ScanAsync("https://sub.evil.com/phishing");

        result.Verdict.Should().Be(UrlSafetyVerdict.Malicious);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScanAsync_SuspiciousPattern_ReturnsSuspicious()
    {
        var service = CreateService(suspiciousPatterns: ["free-iphone"]);

        var result = await service.ScanAsync("https://free-iphone-winner.example.com");

        result.Verdict.Should().Be(UrlSafetyVerdict.Suspicious);
        result.Source.Should().Be("suspicious_patterns");
        result.Reason.Should().Contain("free-iphone");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScanAsync_NoScheme_AddsHttps()
    {
        var service = CreateService(blockedHosts: ["evil.com"]);

        var result = await service.ScanAsync("evil.com/path");

        result.Verdict.Should().Be(UrlSafetyVerdict.Malicious);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScanAsync_CachesResult()
    {
        var service = CreateService(blockedHosts: ["evil.com"]);

        var first = await service.ScanAsync("https://example.com/safe");
        first.FromCache.Should().BeFalse();

        var second = await service.ScanAsync("https://example.com/safe");
        second.FromCache.Should().BeTrue();
        second.Verdict.Should().Be(first.Verdict);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScanAsync_MaliciousBeforeSuspicious_BlocklistTakesPriority()
    {
        var service = CreateService(
            blockedHosts: ["evil.com"],
            suspiciousPatterns: ["evil"]);

        var result = await service.ScanAsync("https://evil.com/page");

        result.Verdict.Should().Be(UrlSafetyVerdict.Malicious);
        result.Source.Should().Be("blocklist");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScanAsync_InvalidUrl_ReturnsSuspicious()
    {
        var service = CreateService();

        var result = await service.ScanAsync("not a valid url at all!!!");

        result.Verdict.Should().Be(UrlSafetyVerdict.Suspicious);
        result.Source.Should().Be("validation");
    }

    private static InMemoryUrlSafetyService CreateService(
        string[]? blockedHosts = null,
        string[]? suspiciousPatterns = null)
    {
        var options = Options.Create(new UrlSafetyOptions
        {
            BlockedHosts = blockedHosts ?? [],
            SuspiciousPatterns = suspiciousPatterns ?? [],
            CacheTtlSeconds = 60,
            ScanTimeoutMs = 1000
        });
        return new InMemoryUrlSafetyService(options, NullLogger<InMemoryUrlSafetyService>.Instance);
    }
}
