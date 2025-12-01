namespace RedirectService.Tests.Services;

public sealed class DomainConfigCacheTests
{
    private static DomainConfig BuildConfig(string hostname = "go.example.com")
        => new() { Hostname = hostname, BrandColor = "#3498db" };

    // ── TryGet on empty cache ─────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void TryGet_EmptyCache_ReturnsFalse()
    {
        var cache = new DomainConfigCache();

        var found = cache.TryGet("go.example.com", out var config);

        found.Should().BeFalse();
        config.Should().BeNull();
    }

    // ── Set / TryGet roundtrip ────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void TryGet_AfterSetWithConfig_ReturnsTrueWithConfig()
    {
        var cache = new DomainConfigCache();
        var original = BuildConfig();

        cache.Set("go.example.com", original);
        var found = cache.TryGet("go.example.com", out var result);

        found.Should().BeTrue();
        result.Should().BeSameAs(original);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TryGet_AfterSetNull_ReturnsTrueWithNullConfig()
    {
        // Negative caching: null means "we checked, no config exists for this host".
        var cache = new DomainConfigCache();

        cache.Set("go.example.com", null);
        var found = cache.TryGet("go.example.com", out var result);

        found.Should().BeTrue();
        result.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TryGet_DifferentHostname_ReturnsFalse()
    {
        var cache = new DomainConfigCache();
        cache.Set("go.example.com", BuildConfig("go.example.com"));

        var found = cache.TryGet("go.other.com", out _);

        found.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Set_OverwritesExistingEntry()
    {
        var cache = new DomainConfigCache();
        var v1 = BuildConfig();
        var v2 = new DomainConfig { Hostname = "go.example.com", BrandColor = "#ff0000" };

        cache.Set("go.example.com", v1);
        cache.Set("go.example.com", v2);
        cache.TryGet("go.example.com", out var result);

        result!.BrandColor.Should().Be("#ff0000");
    }

    // ── TTL expiration ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void TryGet_ExpiredEntry_ReturnsFalse()
    {
        var cache = new DomainConfigCache(ttl: TimeSpan.FromMilliseconds(1));
        cache.Set("go.example.com", BuildConfig());

        Thread.Sleep(10);

        var found = cache.TryGet("go.example.com", out _);
        found.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TryGet_NonExpiredEntry_ReturnsTrue()
    {
        var cache = new DomainConfigCache(ttl: TimeSpan.FromHours(1));
        cache.Set("go.example.com", BuildConfig());

        var found = cache.TryGet("go.example.com", out _);

        found.Should().BeTrue();
    }

    // ── Negative-cache TTL expiration ─────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void TryGet_ExpiredNullEntry_ReturnsFalse()
    {
        var cache = new DomainConfigCache(ttl: TimeSpan.FromMilliseconds(1));
        cache.Set("go.example.com", null);

        Thread.Sleep(10);

        var found = cache.TryGet("go.example.com", out _);
        found.Should().BeFalse();
    }

    // ── Count ─────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void Count_TracksEntries()
    {
        var cache = new DomainConfigCache();

        cache.Set("host-a.example.com", BuildConfig("host-a.example.com"));
        cache.Set("host-b.example.com", BuildConfig("host-b.example.com"));

        cache.Count.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Count_NullEntryIsCounted()
    {
        var cache = new DomainConfigCache();

        cache.Set("go.example.com", null);

        cache.Count.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Count_OverwriteDoesNotIncrease()
    {
        var cache = new DomainConfigCache();

        cache.Set("go.example.com", BuildConfig());
        cache.Set("go.example.com", BuildConfig());

        cache.Count.Should().Be(1);
    }

    // ── Max entries ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void Set_AtCapacity_DoesNotAddNewEntry()
    {
        var cache = new DomainConfigCache(maxEntries: 2);

        cache.Set("host-a.example.com", BuildConfig("host-a.example.com"));
        cache.Set("host-b.example.com", BuildConfig("host-b.example.com"));
        cache.Set("host-c.example.com", BuildConfig("host-c.example.com")); // rejected

        cache.Count.Should().Be(2);
        cache.TryGet("host-c.example.com", out _).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Set_AtCapacity_AllowsOverwriteOfExistingKey()
    {
        var cache = new DomainConfigCache(maxEntries: 1);
        var updated = new DomainConfig { Hostname = "go.example.com", BrandColor = "#ff0000" };

        cache.Set("go.example.com", BuildConfig());
        cache.Set("go.example.com", updated); // overwrite — should succeed even at capacity

        cache.TryGet("go.example.com", out var result);
        result!.BrandColor.Should().Be("#ff0000");
    }
}
