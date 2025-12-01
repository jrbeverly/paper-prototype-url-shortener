namespace RedirectService.Tests.Services;

public sealed class RedirectRateLimiterTests
{
    private static InMemoryRedirectRateLimiter CreateLimiter(
        int domainLimit = 10,
        int ipLimit = 5,
        double burstFactor = 1.0,
        string[]? exemptIps = null,
        string[]? exemptUaPrefixes = null)
    {
        var options = new RedirectRateLimitOptions
        {
            DomainLimitPerMinute = domainLimit,
            IpLimitPerMinute = ipLimit,
            BurstFactor = burstFactor,
            ExemptIps = exemptIps ?? [],
            ExemptUserAgentPrefixes = exemptUaPrefixes ?? []
        };
        return new InMemoryRedirectRateLimiter(options);
    }

    // ── CheckDomain ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void CheckDomain_FirstRequest_IsAllowed()
    {
        using var limiter = CreateLimiter(domainLimit: 5, burstFactor: 1.0);

        var result = limiter.CheckDomain("go.example.com");

        result.IsAllowed.Should().BeTrue();
        result.Limit.Should().Be(5);
        result.RetryAfterSeconds.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CheckDomain_UnderLimit_RemainingDecrementsEachRequest()
    {
        using var limiter = CreateLimiter(domainLimit: 5, burstFactor: 1.0);

        limiter.CheckDomain("go.example.com"); // consumes 1, remaining = 4
        var result = limiter.CheckDomain("go.example.com"); // consumes 1, remaining = 3

        result.IsAllowed.Should().BeTrue();
        result.Remaining.Should().Be(3);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CheckDomain_AtBucketCapacity_SubsequentRequestBlocked()
    {
        using var limiter = CreateLimiter(domainLimit: 3, burstFactor: 1.0);

        for (var i = 0; i < 3; i++)
            limiter.CheckDomain("go.example.com");

        var blocked = limiter.CheckDomain("go.example.com");

        blocked.IsAllowed.Should().BeFalse();
        blocked.Remaining.Should().Be(0);
        blocked.RetryAfterSeconds.Should().BeGreaterThan(0);
        blocked.Limit.Should().Be(3);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CheckDomain_DifferentHostnames_HaveIndependentBuckets()
    {
        using var limiter = CreateLimiter(domainLimit: 1, burstFactor: 1.0);

        var first = limiter.CheckDomain("a.example.com");
        limiter.CheckDomain("a.example.com"); // exhausts a.example.com bucket

        var second = limiter.CheckDomain("b.example.com");

        first.IsAllowed.Should().BeTrue();
        second.IsAllowed.Should().BeTrue(); // separate bucket, still has tokens
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CheckDomain_WithBurstFactor_AllowsBurstAboveSteadyRate()
    {
        // burst capacity = 5 * 2 = 10 tokens
        using var limiter = CreateLimiter(domainLimit: 5, burstFactor: 2.0);

        var results = Enumerable.Range(0, 10)
            .Select(_ => limiter.CheckDomain("go.example.com"))
            .ToList();

        results.Should().AllSatisfy(r => r.IsAllowed.Should().BeTrue(),
            "burst bucket starts at 10 tokens and should absorb 10 requests");

        var blocked = limiter.CheckDomain("go.example.com");
        blocked.IsAllowed.Should().BeFalse("bucket is exhausted after 10 requests");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CheckDomain_BlockedResult_HasPositiveRetryAfter()
    {
        using var limiter = CreateLimiter(domainLimit: 1, burstFactor: 1.0);

        limiter.CheckDomain("go.example.com"); // consume the single token

        var blocked = limiter.CheckDomain("go.example.com");

        blocked.IsAllowed.Should().BeFalse();
        blocked.RetryAfterSeconds.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CheckDomain_ResetUnixSeconds_IsInTheFuture()
    {
        using var limiter = CreateLimiter(domainLimit: 5, burstFactor: 1.0);

        var result = limiter.CheckDomain("go.example.com");

        result.ResetUnixSeconds.Should().BeGreaterThan(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    // ── CheckIp ──────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void CheckIp_FirstRequest_IsAllowed()
    {
        using var limiter = CreateLimiter(ipLimit: 5, burstFactor: 1.0);

        var result = limiter.CheckIp("1.2.3.4");

        result.IsAllowed.Should().BeTrue();
        result.Limit.Should().Be(5);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CheckIp_AtBucketCapacity_SubsequentRequestBlocked()
    {
        using var limiter = CreateLimiter(ipLimit: 3, burstFactor: 1.0);

        for (var i = 0; i < 3; i++)
            limiter.CheckIp("1.2.3.4");

        var blocked = limiter.CheckIp("1.2.3.4");

        blocked.IsAllowed.Should().BeFalse();
        blocked.RetryAfterSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CheckIp_DifferentIps_HaveIndependentBuckets()
    {
        using var limiter = CreateLimiter(ipLimit: 1, burstFactor: 1.0);

        limiter.CheckIp("1.2.3.4"); // exhaust 1.2.3.4

        var other = limiter.CheckIp("5.6.7.8");

        other.IsAllowed.Should().BeTrue();
    }

    // ── IsExemptIp ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void IsExemptIp_ConfiguredIp_ReturnsTrue()
    {
        using var limiter = CreateLimiter(exemptIps: ["10.0.0.1"]);

        limiter.IsExemptIp("10.0.0.1").Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsExemptIp_UnknownIp_ReturnsFalse()
    {
        using var limiter = CreateLimiter(exemptIps: ["10.0.0.1"]);

        limiter.IsExemptIp("1.2.3.4").Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsExemptIp_IsCaseInsensitive()
    {
        using var limiter = CreateLimiter(exemptIps: ["::1"]);

        limiter.IsExemptIp("::1").Should().BeTrue();
        limiter.IsExemptIp("::1").Should().BeTrue();
    }

    // ── IsExemptUserAgent ────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void IsExemptUserAgent_MatchingPrefix_ReturnsTrue()
    {
        using var limiter = CreateLimiter(exemptUaPrefixes: ["Googlebot"]);

        limiter.IsExemptUserAgent("Googlebot/2.1 (+http://www.google.com/bot.html)")
            .Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsExemptUserAgent_PrefixMatchIsCaseInsensitive()
    {
        using var limiter = CreateLimiter(exemptUaPrefixes: ["Googlebot"]);

        limiter.IsExemptUserAgent("googlebot/2.1").Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsExemptUserAgent_NoMatchingPrefix_ReturnsFalse()
    {
        using var limiter = CreateLimiter(exemptUaPrefixes: ["Googlebot"]);

        limiter.IsExemptUserAgent("Mozilla/5.0 (compatible; Chrome/120)").Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsExemptUserAgent_EmptyUserAgent_ReturnsFalse()
    {
        using var limiter = CreateLimiter(exemptUaPrefixes: ["Googlebot"]);

        limiter.IsExemptUserAgent("").Should().BeFalse();
    }

    // ── Default options include standard crawlers ─────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("Googlebot/2.1 (+http://www.google.com/bot.html)")]
    [InlineData("Bingbot/2.0")]
    [InlineData("facebookexternalhit/1.1")]
    [InlineData("Twitterbot/1.0")]
    [InlineData("LinkedInBot/1.0")]
    public void IsExemptUserAgent_DefaultOptions_ExemptKnownCrawlers(string ua)
    {
        using var limiter = new InMemoryRedirectRateLimiter(new RedirectRateLimitOptions());

        limiter.IsExemptUserAgent(ua).Should().BeTrue();
    }
}
