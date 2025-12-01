namespace RedirectService.Tests.Services;

public sealed class RedirectServiceTests
{
    private const string _hostname = "go.example.com";
    private const string _slug = "summer-sale";
    private const string _destination = "https://example.com/landing";

    private static Api.Services.RedirectService CreateService(
        FakeRedirectRepository? repo = null,
        SpyClickEventEmitter? emitter = null)
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
            TenantId = "tenant-123",
            DomainId = "domain-456",
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

    private static RedirectRequest BuildRequest(string? hostname = null, string? slug = null)
        => new() { Hostname = hostname ?? _hostname, Slug = slug ?? _slug };

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_ActiveLink_ReturnsRedirect()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.Outcome.Should().Be(RedirectOutcome.Redirect);
        result.DestinationUrl.Should().Be(_destination);
        result.StatusCode.Should().Be(302);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_ActiveLink_HonorsRedirectType()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(redirectType: 301));
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.StatusCode.Should().Be(301);
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

    // ── Not found ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_NotFound_ReturnsNotFound()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(null);
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.Outcome.Should().Be(RedirectOutcome.NotFound);
        result.StatusCode.Should().Be(404);
        result.DestinationUrl.Should().BeNull();
    }

    // ── Expiration ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_ExpiredLink_ReturnsExpired()
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

    // ── UTM parameters ───────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithUtmParameters_AppendsToUrl()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(utmParameters: new Dictionary<string, string>
        {
            ["utm_source"] = "newsletter",
            ["utm_medium"] = "email"
        }));
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.Outcome.Should().Be(RedirectOutcome.Redirect);
        result.DestinationUrl.Should().Contain("utm_source=newsletter");
        result.DestinationUrl.Should().Contain("utm_medium=email");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithUtmParameters_AppendsAmpersandWhenQueryStringExists()
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
    public async Task ResolveAsync_WithoutUtmParameters_ReturnsUnmodifiedUrl()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.DestinationUrl.Should().Be(_destination);
    }

    // ── Click events ─────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_SuccessfulLookup_EmitsClickEvent()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest());

        emitter.EmitCount.Should().Be(1);
        emitter.LastEvent!.TenantId.Should().Be("tenant-123");
        emitter.LastEvent.DomainId.Should().Be("domain-456");
        emitter.LastEvent.Domain.Should().Be(_hostname);
        emitter.LastEvent.Slug.Should().Be(_slug);
        emitter.LastEvent.DestinationUrl.Should().Be(_destination);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_NotFound_DoesNotEmitClickEvent()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(null);
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest());

        emitter.EmitCount.Should().Be(0);
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

    // ── Click-limit expiration ─────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_ClickLimitExceeded_ReturnsExpired()
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
    public async Task ResolveAsync_ClickLimitExceeded_DoesNotCallIncrement()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(maxClicks: 100, currentClicks: 100));
        var service = CreateService(repo);

        await service.ResolveAsync(BuildRequest());

        repo.IncrementCallCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_ClickLimitExceeded_DoesNotEmitClickEvent()
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
    public async Task ResolveAsync_UnderClickLimit_IncrementsAndRedirects()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(maxClicks: 100, currentClicks: 50));
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.Outcome.Should().Be(RedirectOutcome.Redirect);
        repo.IncrementCallCount.Should().Be(1);
        repo.LastIncrementHostname.Should().Be(_hostname);
        repo.LastIncrementSlug.Should().Be(_slug);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_UnderClickLimit_EmitsClickEvent()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(maxClicks: 100, currentClicks: 50));
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest());

        emitter.EmitCount.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_IncrementFails_ReturnsExpired()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(maxClicks: 100, currentClicks: 99));
        repo.SetIncrementResult(false); // simulate race: limit reached between read and increment
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        var result = await service.ResolveAsync(BuildRequest());

        result.Outcome.Should().Be(RedirectOutcome.Expired);
        result.StatusCode.Should().Be(410);
        result.ExpiredReason.Should().Be("clicks");
        emitter.EmitCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_NoClickLimit_IncrementsAndRedirects()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var service = CreateService(repo);

        var result = await service.ResolveAsync(BuildRequest());

        result.Outcome.Should().Be(RedirectOutcome.Redirect);
        repo.IncrementCallCount.Should().Be(1);
    }

    // ── Schema envelope ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_SuccessfulLookup_SetsSchemaVersion1()
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
    public async Task ResolveAsync_SuccessfulLookup_SetsUniqueEventId()
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
    public async Task ResolveAsync_ActiveLink_SetsStatusCodeFromRecord()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(redirectType: 301));
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest());

        emitter.LastEvent!.StatusCode.Should().Be(301);
    }

    // ── UTM extraction ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithUtmParametersOnRecord_PopulatesUtmFields()
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

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithoutUtmParametersOnRecord_LeavesUtmFieldsNull()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest());

        emitter.LastEvent!.UtmSource.Should().BeNull();
        emitter.LastEvent.UtmMedium.Should().BeNull();
        emitter.LastEvent.UtmCampaign.Should().BeNull();
    }

    // ── Analytics fields ─────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithCountryHeader_SetsCountryOnEvent()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(new RedirectRequest
        {
            Hostname = _hostname,
            Slug = _slug,
            Headers = new Dictionary<string, string> { ["CloudFront-Viewer-Country"] = "CA" }
        });

        emitter.LastEvent!.Country.Should().Be("CA");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithMobileViewerHeader_SetsDeviceTypeToMobile()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(new RedirectRequest
        {
            Hostname = _hostname,
            Slug = _slug,
            Headers = new Dictionary<string, string> { ["CloudFront-Is-Mobile-Viewer"] = "true" }
        });

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

        await service.ResolveAsync(new RedirectRequest
        {
            Hostname = _hostname,
            Slug = _slug,
            Headers = new Dictionary<string, string> { ["CloudFront-Is-Tablet-Viewer"] = "true" }
        });

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

        await service.ResolveAsync(new RedirectRequest
        {
            Hostname = _hostname,
            Slug = _slug,
            Headers = new Dictionary<string, string> { ["CloudFront-Is-Desktop-Viewer"] = "true" }
        });

        emitter.LastEvent!.DeviceType.Should().Be("desktop");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithReferrerHeader_SetsReferrer()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(new RedirectRequest
        {
            Hostname = _hostname,
            Slug = _slug,
            Headers = new Dictionary<string, string> { ["Referer"] = "https://google.com" }
        });

        emitter.LastEvent!.Referrer.Should().Be("https://google.com");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithViewerAddress_SetsHashedIp()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(new RedirectRequest
        {
            Hostname = _hostname,
            Slug = _slug,
            Headers = new Dictionary<string, string> { ["CloudFront-Viewer-Address"] = "203.0.113.1:12345" }
        });

        emitter.LastEvent!.IpHash.Should().NotBeNullOrEmpty();
        emitter.LastEvent.IpHash.Should().NotBe("203.0.113.1");
        emitter.LastEvent.IpHash.Should().HaveLength(64); // full SHA-256 hex
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithUserAgent_SetsHashedUserAgent()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(new RedirectRequest
        {
            Hostname = _hostname,
            Slug = _slug,
            Headers = new Dictionary<string, string> { ["User-Agent"] = "Mozilla/5.0" }
        });

        emitter.LastEvent!.UserAgentHash.Should().NotBeNullOrEmpty();
        emitter.LastEvent.UserAgentHash.Should().NotBe("Mozilla/5.0");
        emitter.LastEvent.UserAgentHash.Should().HaveLength(64);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithBotScore_SetsBotScore()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(new RedirectRequest
        {
            Hostname = _hostname,
            Slug = _slug,
            Headers = new Dictionary<string, string> { ["X-Bot-Score"] = "42" }
        });

        emitter.LastEvent!.BotScore.Should().Be(42);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_WithNoHeaders_LeavesAnalyticsFieldsNull()
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

    // ── Emission error handling ───────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_EmitterThrows_RedirectStillSucceeds()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord());
        var service = new Api.Services.RedirectService(repo, new ThrowingClickEventEmitter());

        var result = await service.ResolveAsync(BuildRequest());

        result.Outcome.Should().Be(RedirectOutcome.Redirect);
        result.DestinationUrl.Should().Be(_destination);
    }

    // ── Normalization ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("GO.EXAMPLE.COM", "go.example.com")]
    [InlineData("Go.Example.Com", "go.example.com")]
    [InlineData("go.example.com.", "go.example.com")]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_NormalizesHostname(string rawHostname, string expectedHostname)
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(null);
        var service = CreateService(repo);

        await service.ResolveAsync(new RedirectRequest { Hostname = rawHostname, Slug = _slug });

        repo.LastHostname.Should().Be(expectedHostname);
    }

    [Theory]
    [InlineData("summer-sale", "summer-sale")]
    [InlineData("/summer-sale", "summer-sale")]
    [InlineData("SUMMER-SALE", "summer-sale")]
    [InlineData("/SUMMER-SALE", "summer-sale")]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_NormalizesSlug(string rawSlug, string expectedSlug)
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(null);
        var service = CreateService(repo);

        await service.ResolveAsync(new RedirectRequest { Hostname = _hostname, Slug = rawSlug });

        repo.LastSlug.Should().Be(expectedSlug);
    }

    // ── Suspended links ──────────────────────────────────────────────────────

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

    // ── Quarantined links ────────────────────────────────────────────────────

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

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ResolveAsync_QuarantinedLink_DoesNotEmitClickEvent()
    {
        var repo = new FakeRedirectRepository();
        repo.Returns(BuildRecord(status: "quarantined"));
        var emitter = new SpyClickEventEmitter();
        var service = CreateService(repo, emitter);

        await service.ResolveAsync(BuildRequest());

        emitter.EmitCount.Should().Be(0);
    }
}
