using Amazon.DynamoDBv2;

namespace RedirectService.Tests.Repositories;

public sealed class DynamoDbDomainConfigRepositoryTests
{
    private const string _tableName = "test-redirects";
    private const string _hostname = "go.example.com";

    private static DynamoDbDomainConfigRepository CreateRepository(StubDynamoDB stub)
        => new(stub, _tableName);

    private static Dictionary<string, AttributeValue> BuildItem(
        string? fallbackUrl = null,
        string? brandColor = null,
        string? logoUrl = null,
        string? customMessage = null,
        string? supportUrl = null,
        string? customCss = null)
    {
        // PK is always included so that IsItemSet returns true (SDK checks Count > 0).
        var item = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new AttributeValue { S = $"HOST#{_hostname}" }
        };
        if (fallbackUrl is not null)
            item["FallbackUrl"] = new AttributeValue { S = fallbackUrl };
        if (brandColor is not null)
            item["BrandColor"] = new AttributeValue { S = brandColor };
        if (logoUrl is not null)
            item["LogoUrl"] = new AttributeValue { S = logoUrl };
        if (customMessage is not null)
            item["CustomMessage"] = new AttributeValue { S = customMessage };
        if (supportUrl is not null)
            item["SupportUrl"] = new AttributeValue { S = supportUrl };
        if (customCss is not null)
            item["CustomCss"] = new AttributeValue { S = customCss };
        return item;
    }

    // ── Not found ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_ItemNotFound_ReturnsNull()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse());
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname);

        result.Should().BeNull();
    }

    // ── Field mapping ─────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_ItemFound_ReturnsDomainConfig()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse { Item = BuildItem(fallbackUrl: "https://example.com") });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname);

        result.Should().NotBeNull();
        result!.Hostname.Should().Be(_hostname);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_MapsAllFields()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse
        {
            Item = BuildItem(
                fallbackUrl: "https://example.com/fallback",
                brandColor: "#3498db",
                logoUrl: "https://cdn.example.com/logo.png")
        });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname);

        result!.FallbackUrl.Should().Be("https://example.com/fallback");
        result.BrandColor.Should().Be("#3498db");
        result.LogoUrl.Should().Be("https://cdn.example.com/logo.png");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_MissingOptionalFields_ReturnsConfigWithNulls()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse { Item = BuildItem() });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname);

        result!.FallbackUrl.Should().BeNull();
        result.BrandColor.Should().BeNull();
        result.LogoUrl.Should().BeNull();
        result.CustomMessage.Should().BeNull();
        result.SupportUrl.Should().BeNull();
        result.CustomCss.Should().BeNull();
    }

    // ── Field validation ──────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_ValidHexColorSixDigit_ReturnsBrandColor()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse { Item = BuildItem(brandColor: "#a1b2c3") });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname);

        result!.BrandColor.Should().Be("#a1b2c3");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_ValidHexColorThreeDigit_ReturnsBrandColor()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse { Item = BuildItem(brandColor: "#F0A") });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname);

        result!.BrandColor.Should().Be("#F0A");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_InvalidHexColorBadChars_ReturnsNullBrandColor()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse { Item = BuildItem(brandColor: "#ZZZZZZ") });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname);

        result!.BrandColor.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_InvalidHexColorNoHash_ReturnsNullBrandColor()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse { Item = BuildItem(brandColor: "3498db") });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname);

        result!.BrandColor.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_InvalidFallbackUrlNonHttpScheme_ReturnsNullFallbackUrl()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse { Item = BuildItem(fallbackUrl: "ftp://example.com") });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname);

        result!.FallbackUrl.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_InvalidLogoUrlJavascriptScheme_ReturnsNullLogoUrl()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse { Item = BuildItem(logoUrl: "javascript:alert(1)") });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname);

        result!.LogoUrl.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_HttpFallbackUrl_IsAccepted()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse { Item = BuildItem(fallbackUrl: "http://example.com") });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname);

        result!.FallbackUrl.Should().Be("http://example.com");
    }

    // ── DynamoDB key pattern ──────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_UsesCorrectTableName()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse());
        var repo = CreateRepository(stub);

        await repo.GetAsync(_hostname);

        stub.LastRequest!.TableName.Should().Be(_tableName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_UsesCorrectPartitionKey()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse());
        var repo = CreateRepository(stub);

        await repo.GetAsync(_hostname);

        stub.LastRequest!.Key["PK"].S.Should().Be($"HOST#{_hostname}");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_UsesCorrectSortKey()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse());
        var repo = CreateRepository(stub);

        await repo.GetAsync(_hostname);

        stub.LastRequest!.Key["SK"].S.Should().Be("DOMAIN_CONFIG");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_UsesProjectionExpression()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse());
        var repo = CreateRepository(stub);

        await repo.GetAsync(_hostname);

        stub.LastRequest!.ProjectionExpression.Should().NotBeNullOrEmpty();
    }

    // ── CustomMessage field ───────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_MapsCustomMessage()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse { Item = BuildItem(customMessage: "Contact support for help.") });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname);

        result!.CustomMessage.Should().Be("Contact support for help.");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_CustomMessageExceedingMaxLength_IsTruncatedTo500()
    {
        var stub = new StubDynamoDB();
        var longMessage = new string('a', 600);
        stub.Returns(new GetItemResponse { Item = BuildItem(customMessage: longMessage) });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname);

        result!.CustomMessage.Should().HaveLength(500);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_WhitespaceOnlyCustomMessage_ReturnsNull()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse { Item = BuildItem(customMessage: "   ") });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname);

        result!.CustomMessage.Should().BeNull();
    }

    // ── SupportUrl field ──────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_MapsSupportUrl()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse { Item = BuildItem(supportUrl: "https://support.example.com") });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname);

        result!.SupportUrl.Should().Be("https://support.example.com");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_InvalidSupportUrlNonHttpScheme_ReturnsNull()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse { Item = BuildItem(supportUrl: "ftp://support.example.com") });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname);

        result!.SupportUrl.Should().BeNull();
    }

    // ── CustomCss field ───────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_MapsCustomCss()
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse { Item = BuildItem(customCss: "body{font-family:Georgia,serif}") });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname);

        result!.CustomCss.Should().Be("body{font-family:Georgia,serif}");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_CustomCssExceedingMaxLength_ReturnsNull()
    {
        var stub = new StubDynamoDB();
        var oversizedCss = new string('a', 2001);
        stub.Returns(new GetItemResponse { Item = BuildItem(customCss: oversizedCss) });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname);

        result!.CustomCss.Should().BeNull();
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("</style><script>alert(1)</script>")]
    [InlineData("</STYLE>bad")]
    [InlineData("</Style>bad")]
    public async Task GetAsync_CustomCssWithStyleCloseTag_ReturnsNull(string css)
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse { Item = BuildItem(customCss: css) });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname);

        result!.CustomCss.Should().BeNull();
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<SCRIPT src='evil.js'>")]
    public async Task GetAsync_CustomCssWithScriptTag_ReturnsNull(string css)
    {
        var stub = new StubDynamoDB();
        stub.Returns(new GetItemResponse { Item = BuildItem(customCss: css) });
        var repo = CreateRepository(stub);

        var result = await repo.GetAsync(_hostname);

        result!.CustomCss.Should().BeNull();
    }

    // ── Exception propagation ─────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetAsync_DynamoDbThrows_PropagatesException()
    {
        var stub = new StubDynamoDB();
        stub.Throws(new InvalidOperationException("DynamoDB unavailable"));
        var repo = CreateRepository(stub);

        var act = async () => await repo.GetAsync(_hostname);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
