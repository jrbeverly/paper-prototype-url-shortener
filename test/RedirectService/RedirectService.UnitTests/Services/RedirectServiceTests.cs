namespace RedirectService.UnitTests.Services;

public sealed class RedirectServiceTests
{
    private const string _hostname = "go.example.com";
    private const string _slug = "summer-sale";
    private const string _destination = "https://example.com/landing";

    private static Api.Services.RedirectService CreateService(
        FakeRedirectRepository? repo = null,
        IClickEventEmitter? emitter = null)
        => new(repo ?? new FakeRedirectRepository(), emitter ?? new SpyClickEventEmitter());

    private static RedirectRecord BuildRecord(
        string? destination = null,
        int redirectType = 302,
        DateTime? expiresAt = null,
        int? maxClicks = null,
        long currentClicks = 0,
        IReadOnlyDictionary<string, string>? utmParameters = null,
        string status = "active")
        => new()
        {
            TenantId = "tenant-abc",
            DomainId = "domain-xyz",
            Hostname = _hostname,
            Slug = _slug,
            DestinationUrl = destination ?? _destination,
            RedirectType = redirectType,
            ExpiresAt = expiresAt,
            MaxClicks = maxClicks,
            CurrentClicks = currentClicks,
            UtmParameters = utmParameters,
            Status = status
        };

    private static RedirectRequest BuildRequest(
        string? hostname = null,
        string? slug = null,
        IReadOnlyDictionary<string, string>? headers = null)
        => new()
        {
            Hostname = hostname ?? _hostname,
            Slug = slug ?? _slug,
            Headers = headers ?? new Dictionary<string, string>()
        };

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_ActiveLink_ReturnsRedirectOutcome()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.Outcome.Should().Be(RedirectOutcome.Redirect);
        result.DestinationUrl.Should().Be(_destination);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_ActiveLink_HonorsRedirectType301()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(redirectType: 301));
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.StatusCode.Should().Be(301);
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(308)]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_ActiveLink_HonorsAllSupportedRedirectTypes(int redirectType)
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(redirectType: redirectType));
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.StatusCode.Should().Be(redirectType);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_LinkWithFutureExpiry_ReturnsRedirect()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(expiresAt: DateTime.UtcNow.AddDays(1)));
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.Outcome.Should().Be(RedirectOutcome.Redirect);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_ActiveLink_IncrementsClickCount()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var service = CreateService(repo);

        await service.ResolveAsync(BuildRequest());

        repo.IncrementCallCount.Should().Be(1);
        repo.LastIncrementHostname.Should().Be(_hostname);
        repo.LastIncrementSlug.Should().Be(_slug);
    }

    // ── Normalization — case-insensitive host + slug ──────────────────────────

    [Theory]
    [InlineData("GO.EXAMPLE.COM", "go.example.com")]
    [InlineData("Go.Example.Com", "go.example.com")]
    [InlineData("go.example.com.", "go.example.com")]
    [InlineData("GO.EXAMPLE.COM.", "go.example.com")]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_NormalizesHostnameToLowercase(string rawHostname, string expectedHostname)
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(null);
        var service = CreateService(repo);

        await service.ResolveAsync(BuildRequest(hostname: rawHostname));

        repo.LastHostname.Should().Be(expectedHostname);
    }

    [Theory]
    [InlineData("summer-sale", "summer-sale")]
    [InlineData("/summer-sale", "summer-sale")]
    [InlineData("SUMMER-SALE", "summer-sale")]
    [InlineData("/SUMMER-SALE", "summer-sale")]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_NormalizesSlugToLowercase(string rawSlug, string expectedSlug)
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(null);
        var service = CreateService(repo);

        await service.ResolveAsync(BuildRequest(slug: rawSlug));

        repo.LastSlug.Should().Be(expectedSlug);
    }

    // ── Not found ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_MissingLink_ReturnsNotFound()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(null);
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.Outcome.Should().Be(RedirectOutcome.NotFound);
        result.StatusCode.Should().Be(404);
        result.DestinationUrl.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_MissingLink_DoesNotIncrement()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(null);
        var service = CreateService(repo);

        await service.ResolveAsync(BuildRequest());

        repo.IncrementCallCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_MissingLink_DoesNotEmitClickEvent()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(null);
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest());

        emitter.EmitCount.Should().Be(0);
    }

    // ── Time-based expiration ────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_PastExpiresAt_ReturnsExpiredWithTimeReason()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(expiresAt: DateTime.UtcNow.AddDays(-1)));
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.Outcome.Should().Be(RedirectOutcome.Expired);
        result.StatusCode.Should().Be(410);
        result.ExpiredReason.Should().Be("time");
        result.DestinationUrl.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_ExpiredLink_DoesNotIncrement()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(expiresAt: DateTime.UtcNow.AddDays(-1)));
        var service = CreateService(repo);

        await service.ResolveAsync(BuildRequest());

        repo.IncrementCallCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_ExpiredLink_DoesNotEmitClickEvent()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(expiresAt: DateTime.UtcNow.AddDays(-1)));
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest());

        emitter.EmitCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_NoExpiresAt_NeverExpiresByTime()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(expiresAt: null));
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.Outcome.Should().Be(RedirectOutcome.Redirect);
    }

    // ── Click-limit exhaustion ───────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_ClicksAtMaxLimit_ReturnsExpiredWithClicksReason()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(maxClicks: 100, currentClicks: 100));
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.Outcome.Should().Be(RedirectOutcome.Expired);
        result.StatusCode.Should().Be(410);
        result.ExpiredReason.Should().Be("clicks");
        result.DestinationUrl.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_ClicksExceedLimit_ReturnsExpiredWithClicksReason()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(maxClicks: 50, currentClicks: 75));
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.Outcome.Should().Be(RedirectOutcome.Expired);
        result.ExpiredReason.Should().Be("clicks");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_ClickLimitExhausted_DoesNotCallIncrement()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(maxClicks: 100, currentClicks: 100));
        var service = CreateService(repo);

        await service.ResolveAsync(BuildRequest());

        repo.IncrementCallCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_ClickLimitExhausted_DoesNotEmitClickEvent()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(maxClicks: 100, currentClicks: 100));
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest());

        emitter.EmitCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_ClicksBelowLimit_IncrementsAndRedirects()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(maxClicks: 100, currentClicks: 99));
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.Outcome.Should().Be(RedirectOutcome.Redirect);
        repo.IncrementCallCount.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_NoClickLimit_NeverExpiresByClicks()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(maxClicks: null, currentClicks: 0));
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.Outcome.Should().Be(RedirectOutcome.Redirect);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_IncrementReturnsFalse_ReturnsExpiredWithClicksReason()
    {
        // Simulates race: limit reached between the read and the conditional update.
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(maxClicks: 100, currentClicks: 99));
        repo.SetIncrementResult(false);
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        var result = await service.ResolveAsync(BuildRequest());

        result.Outcome.Should().Be(RedirectOutcome.Expired);
        result.ExpiredReason.Should().Be("clicks");
        emitter.EmitCount.Should().Be(0);
    }

    // ── Suspended / quarantined (inactive) links ─────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_SuspendedLink_ReturnsSuspended()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(status: "suspended"));
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.Outcome.Should().Be(RedirectOutcome.Suspended);
        result.StatusCode.Should().Be(451);
        result.DestinationUrl.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_SuspendedLink_DoesNotIncrement()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(status: "suspended"));
        var service = CreateService(repo);

        await service.ResolveAsync(BuildRequest());

        repo.IncrementCallCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_SuspendedLink_DoesNotEmitClickEvent()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(status: "suspended"));
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest());

        emitter.EmitCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_QuarantinedLink_ReturnsQuarantined()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(status: "quarantined"));
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.Outcome.Should().Be(RedirectOutcome.Quarantined);
        result.StatusCode.Should().Be(403);
        result.DestinationUrl.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_QuarantinedLink_DoesNotIncrement()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(status: "quarantined"));
        var service = CreateService(repo);

        await service.ResolveAsync(BuildRequest());

        repo.IncrementCallCount.Should().Be(0);
    }

    // ── UTM parameter appending ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithUtmParameters_AppendsToUrlAsQueryString()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(utmParameters: new Dictionary<string, string>
        {
            ["utm_source"] = "newsletter",
            ["utm_medium"] = "email"
        }));
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.DestinationUrl.Should().Contain("utm_source=newsletter");
        result.DestinationUrl.Should().Contain("utm_medium=email");
        result.DestinationUrl.Should().StartWith(_destination + "?");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithUtmParameters_AppendsAmpersandWhenDestinationHasQueryString()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(
            destination: "https://example.com/landing?ref=direct",
            utmParameters: new Dictionary<string, string> { ["utm_source"] = "email" }));
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.DestinationUrl.Should().StartWith("https://example.com/landing?ref=direct&");
        result.DestinationUrl.Should().Contain("utm_source=email");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithoutUtmParameters_ReturnsUnmodifiedDestinationUrl()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.DestinationUrl.Should().Be(_destination);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithEmptyUtmParameters_ReturnsUnmodifiedDestinationUrl()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(utmParameters: new Dictionary<string, string>()));
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.DestinationUrl.Should().Be(_destination);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithUtmParameters_UrlEncodesSpecialCharacters()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(utmParameters: new Dictionary<string, string>
        {
            ["utm_campaign"] = "summer & sale"
        }));
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.DestinationUrl.Should().Contain("utm_campaign=summer%20%26%20sale");
    }

    // ── Geo rule evaluation (CloudFront-Viewer-Country header) ───────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithCountryHeader_CapturesCountryInClickEvent()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest(
            headers: new Dictionary<string, string> { ["CloudFront-Viewer-Country"] = "CA" }));

        emitter.LastEvent!.Country.Should().Be("CA");
    }

    [Theory]
    [InlineData("US")]
    [InlineData("DE")]
    [InlineData("JP")]
    [InlineData("BR")]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithCountryHeader_CapturesAnyCountryCode(string countryCode)
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest(
            headers: new Dictionary<string, string> { ["CloudFront-Viewer-Country"] = countryCode }));

        emitter.LastEvent!.Country.Should().Be(countryCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithoutCountryHeader_LeavesCountryNull()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest());

        emitter.LastEvent!.Country.Should().BeNull();
    }

    // ── Device rule evaluation (CloudFront device-type headers) ──────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithMobileViewerHeader_SetsDeviceTypeToMobile()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest(
            headers: new Dictionary<string, string> { ["CloudFront-Is-Mobile-Viewer"] = "true" }));

        emitter.LastEvent!.DeviceType.Should().Be("mobile");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithTabletViewerHeader_SetsDeviceTypeToTablet()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest(
            headers: new Dictionary<string, string> { ["CloudFront-Is-Tablet-Viewer"] = "true" }));

        emitter.LastEvent!.DeviceType.Should().Be("tablet");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithDesktopViewerHeader_SetsDeviceTypeToDesktop()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest(
            headers: new Dictionary<string, string> { ["CloudFront-Is-Desktop-Viewer"] = "true" }));

        emitter.LastEvent!.DeviceType.Should().Be("desktop");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithSmartTVViewerHeader_SetsDeviceTypeToTv()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest(
            headers: new Dictionary<string, string> { ["CloudFront-Is-SmartTV-Viewer"] = "true" }));

        emitter.LastEvent!.DeviceType.Should().Be("tv");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithNoDeviceHeaders_LeavesDeviceTypeNull()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest());

        emitter.LastEvent!.DeviceType.Should().BeNull();
    }

    // ── Click event emission ─────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_SuccessfulRedirect_EmitsOneClickEvent()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest());

        emitter.EmitCount.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_SuccessfulRedirect_SetsLinkIdentifiersOnEvent()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest());

        emitter.LastEvent!.TenantId.Should().Be("tenant-abc");
        emitter.LastEvent.DomainId.Should().Be("domain-xyz");
        emitter.LastEvent.Domain.Should().Be(_hostname);
        emitter.LastEvent.Slug.Should().Be(_slug);
        emitter.LastEvent.DestinationUrl.Should().Be(_destination);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_SuccessfulRedirect_SetsSchemaVersion()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest());

        emitter.LastEvent!.SchemaVersion.Should().Be("1");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_SuccessfulRedirect_SetsUniqueEventIdEachTime()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest());
        var firstId = emitter.LastEvent!.EventId;

        await service.ResolveAsync(BuildRequest());
        var secondId = emitter.LastEvent!.EventId;

        firstId.Should().NotBeNullOrEmpty();
        secondId.Should().NotBeNullOrEmpty();
        firstId.Should().NotBe(secondId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithReferrerHeader_CapturesReferrer()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest(
            headers: new Dictionary<string, string> { ["Referer"] = "https://google.com" }));

        emitter.LastEvent!.Referrer.Should().Be("https://google.com");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithViewerAddress_HashesIpAddress()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest(
            headers: new Dictionary<string, string> { ["CloudFront-Viewer-Address"] = "203.0.113.1:12345" }));

        emitter.LastEvent!.IpHash.Should().NotBeNullOrEmpty();
        emitter.LastEvent.IpHash.Should().NotBe("203.0.113.1");
        emitter.LastEvent.IpHash!.Length.Should().Be(64); // SHA-256 hex
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithUserAgent_HashesUserAgent()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest(
            headers: new Dictionary<string, string> { ["User-Agent"] = "Mozilla/5.0" }));

        emitter.LastEvent!.UserAgentHash.Should().NotBeNullOrEmpty();
        emitter.LastEvent.UserAgentHash.Should().NotBe("Mozilla/5.0");
        emitter.LastEvent.UserAgentHash!.Length.Should().Be(64);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithBotScoreHeader_CapturesBotScore()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest(
            headers: new Dictionary<string, string> { ["X-Bot-Score"] = "42" }));

        emitter.LastEvent!.BotScore.Should().Be(42);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithNoHeaders_LeavesAllAnalyticsFieldsNull()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest());

        emitter.LastEvent!.Country.Should().BeNull();
        emitter.LastEvent.DeviceType.Should().BeNull();
        emitter.LastEvent.Referrer.Should().BeNull();
        emitter.LastEvent.IpHash.Should().BeNull();
        emitter.LastEvent.UserAgentHash.Should().BeNull();
        emitter.LastEvent.BotScore.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithUtmParametersOnRecord_PopulatesUtmFieldsOnEvent()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(utmParameters: new Dictionary<string, string>
        {
            ["utm_source"] = "newsletter",
            ["utm_medium"] = "email",
            ["utm_campaign"] = "summer24"
        }));
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest());

        emitter.LastEvent!.UtmSource.Should().Be("newsletter");
        emitter.LastEvent.UtmMedium.Should().Be("email");
        emitter.LastEvent.UtmCampaign.Should().Be("summer24");
    }

    // ── Emission error handling ──────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_EmitterThrows_RedirectStillSucceeds()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var service = CreateService(repo, new ThrowingClickEventEmitter());

        var result = await service.ResolveAsync(BuildRequest());

        result.Outcome.Should().Be(RedirectOutcome.Redirect);
        result.DestinationUrl.Should().Be(_destination);
    }

    // ── DynamoDB error handling (throttle, timeout, general failure) ─────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_RepositoryThrowsOnGet_PropagatesException()
    {
        var repo = new FakeRedirectRepository();
        repo.Throws(new InvalidOperationException("DynamoDB throttle"));
        var service = CreateService(repo);

        var act = async () => await service.ResolveAsync(BuildRequest());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("DynamoDB throttle");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_RepositoryThrowsOnIncrement_PropagatesException()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        repo.SetIncrementThrows(new InvalidOperationException("DynamoDB timeout"));
        var service = CreateService(repo);

        var act = async () => await service.ResolveAsync(BuildRequest());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("DynamoDB timeout");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_RepositoryGetCancelled_PropagatesOperationCancelledException()
    {
        var repo = new FakeRedirectRepository();
        repo.Throws(new OperationCanceledException());
        var service = CreateService(repo);

        var act = async () => await service.ResolveAsync(BuildRequest());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_RepositoryIncrementCancelled_PropagatesOperationCancelledException()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        repo.SetIncrementThrows(new OperationCanceledException());
        var service = CreateService(repo);

        var act = async () => await service.ResolveAsync(BuildRequest());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
