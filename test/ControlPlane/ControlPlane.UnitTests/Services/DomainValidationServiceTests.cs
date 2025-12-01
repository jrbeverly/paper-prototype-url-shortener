using ControlPlane.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ControlPlane.UnitTests.Services;

public sealed class DomainValidationServiceTests
{
    private const string Hostname = "links.example.com";
    private const string TxtValue = "short-io-verify=abc123";
    private const string CnameTarget = "domains.short.io";

    // ── Valid result ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_AllRecordsCorrect_ReturnsValid()
    {
        var service = CreateService(dns: PassDns());

        var result = await service.ValidateAsync(Hostname, TxtValue, CnameTarget, DateTime.UtcNow);

        result.Status.Should().Be(DomainValidationStatus.Valid);
        result.TxtVerified.Should().BeTrue();
        result.CnameVerified.Should().BeTrue();
        result.FailureReason.Should().BeNull();
        result.FromCache.Should().BeFalse();
    }

    // ── Grace period (Pending) ────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_MissingRecordsWithinGracePeriod_ReturnsPending()
    {
        var service = CreateService(dns: MissingBothDns(), gracePeriodHours: 24);

        var result = await service.ValidateAsync(
            Hostname, TxtValue, CnameTarget, domainCreatedAt: DateTime.UtcNow);

        result.Status.Should().Be(DomainValidationStatus.Pending);
        result.FailureReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_MissingTxtWithinGracePeriod_ReturnsPending()
    {
        var dns = StubDns(txtPassed: false, actualTxt: null, cnamePassed: true, actualCname: CnameTarget);
        var service = CreateService(dns: dns, gracePeriodHours: 24);

        var result = await service.ValidateAsync(
            Hostname, TxtValue, CnameTarget, domainCreatedAt: DateTime.UtcNow);

        result.Status.Should().Be(DomainValidationStatus.Pending);
        result.TxtVerified.Should().BeFalse();
        result.CnameVerified.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_MissingRecordsAfterGracePeriod_ReturnsFailed()
    {
        var service = CreateService(dns: MissingBothDns(), gracePeriodHours: 0);

        // Domain created 1 hour ago; 0-hour grace period → already expired.
        var result = await service.ValidateAsync(
            Hostname, TxtValue, CnameTarget,
            domainCreatedAt: DateTime.UtcNow.AddHours(-1));

        result.Status.Should().Be(DomainValidationStatus.Failed);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_MissingRecordsDomainCreatedLongAgo_ReturnsFailed()
    {
        var service = CreateService(dns: MissingBothDns(), gracePeriodHours: 24);

        var result = await service.ValidateAsync(
            Hostname, TxtValue, CnameTarget,
            domainCreatedAt: DateTime.UtcNow.AddDays(-2));

        result.Status.Should().Be(DomainValidationStatus.Failed);
    }

    // ── Wrong values → Failed regardless of grace period ─────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_WrongTxtValue_ReturnsFailedEvenWithinGracePeriod()
    {
        // ActualTxtValue is not null → record exists but has wrong value → misconfiguration.
        var dns = StubDns(txtPassed: false, actualTxt: "short-io-verify=wrong", cnamePassed: true, actualCname: CnameTarget);
        var service = CreateService(dns: dns, gracePeriodHours: 24);

        var result = await service.ValidateAsync(
            Hostname, TxtValue, CnameTarget, domainCreatedAt: DateTime.UtcNow);

        result.Status.Should().Be(DomainValidationStatus.Failed);
        result.TxtVerified.Should().BeFalse();
        result.CnameVerified.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_WrongCnameTarget_ReturnsFailedEvenWithinGracePeriod()
    {
        var dns = StubDns(txtPassed: true, actualTxt: TxtValue, cnamePassed: false, actualCname: "other.cloudfront.net");
        var service = CreateService(dns: dns, gracePeriodHours: 24);

        var result = await service.ValidateAsync(
            Hostname, TxtValue, CnameTarget, domainCreatedAt: DateTime.UtcNow);

        result.Status.Should().Be(DomainValidationStatus.Failed);
        result.TxtVerified.Should().BeTrue();
        result.CnameVerified.Should().BeFalse();
    }

    // ── Failure reasons ───────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_TxtFails_IncludesTxtFailureReason()
    {
        var dns = StubDns(txtPassed: false, actualTxt: null, txtError: "No TXT record found.",
            cnamePassed: true, actualCname: CnameTarget);
        var service = CreateService(dns: dns, gracePeriodHours: 0);

        var result = await service.ValidateAsync(
            Hostname, TxtValue, CnameTarget, domainCreatedAt: DateTime.UtcNow.AddDays(-1));

        result.FailureReason.Should().Contain("No TXT record found.");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_BothFail_CombinesBothReasons()
    {
        var dns = StubDns(
            txtPassed: false, actualTxt: null, txtError: "TXT missing.",
            cnamePassed: false, actualCname: null, cnameError: "CNAME missing.");
        var service = CreateService(dns: dns, gracePeriodHours: 0);

        var result = await service.ValidateAsync(
            Hostname, TxtValue, CnameTarget, domainCreatedAt: DateTime.UtcNow.AddDays(-1));

        result.FailureReason.Should().Contain("TXT missing.");
        result.FailureReason.Should().Contain("CNAME missing.");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_NoExplicitError_FallsBackToDefaultReason()
    {
        // dnsResult has no TxtError / CnameError strings → service builds a default message.
        var dns = StubDns(txtPassed: false, actualTxt: null, cnamePassed: false, actualCname: null);
        var service = CreateService(dns: dns, gracePeriodHours: 0);

        var result = await service.ValidateAsync(
            Hostname, TxtValue, CnameTarget, domainCreatedAt: DateTime.UtcNow.AddDays(-1));

        result.FailureReason.Should().NotBeNullOrEmpty();
        result.FailureReason.Should().Contain(TxtValue);
    }

    // ── Caching ───────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_ValidResult_IsCachedOnSecondCall()
    {
        var callCount = 0;
        var dns = new CountingDnsService(() =>
        {
            callCount++;
            return PassResult();
        });
        var service = CreateService(dns: dns);

        await service.ValidateAsync(Hostname, TxtValue, CnameTarget, DateTime.UtcNow);
        var second = await service.ValidateAsync(Hostname, TxtValue, CnameTarget, DateTime.UtcNow);

        callCount.Should().Be(1);
        second.FromCache.Should().BeTrue();
        second.Status.Should().Be(DomainValidationStatus.Valid);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_FailedResult_IsNotCached()
    {
        var callCount = 0;
        var dns = new CountingDnsService(() =>
        {
            callCount++;
            return FailBothResult();
        });
        var service = CreateService(dns: dns, gracePeriodHours: 0);

        await service.ValidateAsync(Hostname, TxtValue, CnameTarget, DateTime.UtcNow.AddDays(-1));
        await service.ValidateAsync(Hostname, TxtValue, CnameTarget, DateTime.UtcNow.AddDays(-1));

        callCount.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task InvalidateCache_ForcesNewDnsLookupOnNextCall()
    {
        var callCount = 0;
        var dns = new CountingDnsService(() =>
        {
            callCount++;
            return PassResult();
        });
        var service = CreateService(dns: dns);

        await service.ValidateAsync(Hostname, TxtValue, CnameTarget, DateTime.UtcNow);
        service.InvalidateCache(Hostname);
        await service.ValidateAsync(Hostname, TxtValue, CnameTarget, DateTime.UtcNow);

        callCount.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_CacheIsCaseInsensitiveOnHostname()
    {
        var callCount = 0;
        var dns = new CountingDnsService(() =>
        {
            callCount++;
            return PassResult();
        });
        var service = CreateService(dns: dns);

        await service.ValidateAsync("Links.Example.COM", TxtValue, CnameTarget, DateTime.UtcNow);
        await service.ValidateAsync("links.example.com", TxtValue, CnameTarget, DateTime.UtcNow);

        callCount.Should().Be(1);
    }

    // ── Timeout ───────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_DnsLookupExceedsTimeout_ReturnsTimeout()
    {
        // DNS delays 500 ms; timeout is 50 ms → should time out.
        var slowDns = new DelayedDnsService(
            TimeSpan.FromMilliseconds(500),
            PassResult());
        var service = CreateService(dns: slowDns, lookupTimeoutMs: 50);

        var result = await service.ValidateAsync(Hostname, TxtValue, CnameTarget, DateTime.UtcNow);

        result.Status.Should().Be(DomainValidationStatus.Timeout);
        result.TxtVerified.Should().BeFalse();
        result.CnameVerified.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ValidateAsync_DnsCompletesBeforeTimeout_IsNotTreatedAsTimeout()
    {
        // DNS returns instantly; timeout is generous → should not time out.
        var service = CreateService(dns: PassDns(), lookupTimeoutMs: 5_000);

        var result = await service.ValidateAsync(Hostname, TxtValue, CnameTarget, DateTime.UtcNow);

        result.Status.Should().Be(DomainValidationStatus.Valid);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DomainValidationService CreateService(
        IDnsVerificationService? dns = null,
        int lookupTimeoutMs = 10_000,
        int gracePeriodHours = 24,
        int successCacheTtlSeconds = 3_600)
    {
        var options = Options.Create(new DomainValidationOptions
        {
            LookupTimeoutMs = lookupTimeoutMs,
            GracePeriodHours = gracePeriodHours,
            SuccessCacheTtlSeconds = successCacheTtlSeconds
        });
        return new DomainValidationService(
            dns ?? PassDns(),
            options,
            NullLogger<DomainValidationService>.Instance);
    }

    private static IDnsVerificationService PassDns() =>
        new CountingDnsService(() => PassResult());

    private static IDnsVerificationService MissingBothDns() =>
        new CountingDnsService(() => FailBothResult());

    private static IDnsVerificationService StubDns(
        bool txtPassed, string? actualTxt, bool cnamePassed, string? actualCname,
        string? txtError = null, string? cnameError = null) =>
        new CountingDnsService(() => new DnsVerificationResult
        {
            TxtPassed = txtPassed,
            ExpectedTxtValue = TxtValue,
            ActualTxtValue = actualTxt,
            TxtError = txtError,
            CnamePassed = cnamePassed,
            ExpectedCnameValue = CnameTarget,
            ActualCnameValue = actualCname,
            CnameError = cnameError
        });

    private static DnsVerificationResult PassResult() => new()
    {
        TxtPassed = true,
        ExpectedTxtValue = TxtValue,
        ActualTxtValue = TxtValue,
        CnamePassed = true,
        ExpectedCnameValue = CnameTarget,
        ActualCnameValue = CnameTarget
    };

    private static DnsVerificationResult FailBothResult() => new()
    {
        TxtPassed = false,
        ExpectedTxtValue = TxtValue,
        ActualTxtValue = null,
        TxtError = "No TXT record found.",
        CnamePassed = false,
        ExpectedCnameValue = CnameTarget,
        ActualCnameValue = null,
        CnameError = "No CNAME record found."
    };

    /// <summary>Wraps a result factory so tests can assert how many DNS lookups occurred.</summary>
    private sealed class CountingDnsService(Func<DnsVerificationResult> factory) : IDnsVerificationService
    {
        public Task<DnsVerificationResult> VerifyAsync(
            string hostname, string expectedTxtValue, string expectedCnameValue) =>
            Task.FromResult(factory());
    }

    /// <summary>Simulates a slow DNS service for timeout tests.</summary>
    private sealed class DelayedDnsService(TimeSpan delay, DnsVerificationResult result)
        : IDnsVerificationService
    {
        public async Task<DnsVerificationResult> VerifyAsync(
            string hostname, string expectedTxtValue, string expectedCnameValue)
        {
            await Task.Delay(delay);
            return result;
        }
    }
}
