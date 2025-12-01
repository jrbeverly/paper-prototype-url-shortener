namespace RedirectService.Tests.Services;

public sealed class HotLinkCacheTests
{
    private static RedirectRecord BuildRecord(string hostname = "go.example.com", string slug = "test")
        => new()
        {
            TenantId = "tenant-1",
            DomainId = "domain-1",
            Hostname = hostname,
            Slug = slug,
            DestinationUrl = "https://example.com/landing",
            RedirectType = 302,
            Status = "active"
        };

    // ── Set / Get roundtrip ──────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void Get_AfterSet_ReturnsRecord()
    {
        var cache = new HotLinkCache();
        var record = BuildRecord();
        var key = HotLinkCache.BuildKey(record.Hostname, record.Slug);

        cache.Set(key, record);
        var result = cache.Get(key);

        result.Should().NotBeNull();
        result!.DestinationUrl.Should().Be("https://example.com/landing");
        result.Hostname.Should().Be("go.example.com");
        result.Slug.Should().Be("test");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Get_MissingKey_ReturnsNull()
    {
        var cache = new HotLinkCache();

        var result = cache.Get("nonexistent#key");

        result.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Get_DifferentKey_ReturnsNull()
    {
        var cache = new HotLinkCache();
        cache.Set("go.example.com#slug1", BuildRecord(slug: "slug1"));

        var result = cache.Get("go.example.com#slug2");

        result.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Set_OverwritesExistingKey()
    {
        var cache = new HotLinkCache();
        var key = "go.example.com#slug";
        var record1 = new RedirectRecord { TenantId = "t1", DomainId = "d1", Hostname = "go.example.com", Slug = "slug", DestinationUrl = "https://example.com/v1", RedirectType = 302, Status = "active" };
        var record2 = new RedirectRecord { TenantId = "t1", DomainId = "d1", Hostname = "go.example.com", Slug = "slug", DestinationUrl = "https://example.com/v2", RedirectType = 302, Status = "active" };

        cache.Set(key, record1);
        cache.Set(key, record2);
        var result = cache.Get(key);

        result!.DestinationUrl.Should().Be("https://example.com/v2");
    }

    // ── TTL expiration ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void Get_ExpiredEntry_ReturnsNull()
    {
        // Use a very short TTL that has already passed
        var cache = new HotLinkCache(ttl: TimeSpan.FromMilliseconds(1));
        var key = "go.example.com#slug";

        cache.Set(key, BuildRecord());

        // The entry should be considered expired after the TTL
        Thread.Sleep(10);

        var result = cache.Get(key);
        result.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Get_NonExpiredEntry_ReturnsRecord()
    {
        var cache = new HotLinkCache(ttl: TimeSpan.FromHours(1));
        var key = "go.example.com#slug";

        cache.Set(key, BuildRecord());
        var result = cache.Get(key);

        result.Should().NotBeNull();
    }

    // ── BuildKey ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void BuildKey_CombinesHostnameAndSlug()
    {
        var key = HotLinkCache.BuildKey("go.example.com", "summer-sale");
        key.Should().Be("go.example.com#summer-sale");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BuildKey_DistinguishesDifferentHosts()
    {
        var key1 = HotLinkCache.BuildKey("go.example.com", "slug");
        var key2 = HotLinkCache.BuildKey("go.other.com", "slug");

        key1.Should().NotBe(key2);
    }

    // ── Count ────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void Count_TracksEntries()
    {
        var cache = new HotLinkCache();

        cache.Set("key1", BuildRecord(slug: "a"));
        cache.Set("key2", BuildRecord(slug: "b"));

        cache.Count.Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Count_OverwriteDoesNotIncrease()
    {
        var cache = new HotLinkCache();
        var key = "go.example.com#slug";

        cache.Set(key, BuildRecord());
        cache.Set(key, BuildRecord());
        cache.Set(key, BuildRecord());

        cache.Count.Should().Be(1);
    }

    // ── Max entries ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void Set_AtCapacity_DoesNotAdd()
    {
        var cache = new HotLinkCache(maxEntries: 2);

        cache.Set("key1", BuildRecord(slug: "a"));
        cache.Set("key2", BuildRecord(slug: "b"));
        cache.Set("key3", BuildRecord(slug: "c"));

        cache.Count.Should().Be(2);
        cache.Get("key1").Should().NotBeNull();
        cache.Get("key2").Should().NotBeNull();
        cache.Get("key3").Should().BeNull();
    }
}
