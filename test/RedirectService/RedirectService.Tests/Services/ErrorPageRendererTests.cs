namespace RedirectService.Tests.Services;

public sealed class ErrorPageRendererTests
{
    private const string _hostname = "go.example.com";

    // ── Structural correctness ────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_ReturnsValidHtmlDocument()
    {
        var html = ErrorPageRenderer.RenderNotFound(null, _hostname);
        html.Should().StartWith("<!DOCTYPE html>");
        html.Should().Contain("<html");
        html.Should().Contain("</html>");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_ContainsTitleInHeadAndBody()
    {
        var html = ErrorPageRenderer.RenderNotFound(null, _hostname);
        html.Should().Contain("<title>Link Not Found</title>");
        html.Should().Contain("<h1>Link Not Found</h1>");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderExpired_TimeReason_ContainsExpiredMessage()
    {
        var html = ErrorPageRenderer.RenderExpired(null, _hostname, "time");
        html.Should().Contain("expired");
        html.Should().Contain("no longer active");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderExpired_ClicksReason_ContainsClickLimitMessage()
    {
        var html = ErrorPageRenderer.RenderExpired(null, _hostname, "clicks");
        html.Should().Contain("maximum number of uses");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderExpired_NullReason_ContainsGenericExpiredMessage()
    {
        var html = ErrorPageRenderer.RenderExpired(null, _hostname, null);
        html.Should().Contain("expired");
    }

    // ── Abuse links ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderSuspended_ContainsAbuseMailtoLink()
    {
        var html = ErrorPageRenderer.RenderSuspended(null, _hostname);
        html.Should().Contain($"mailto:abuse@{_hostname}");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderQuarantined_ContainsAbuseMailtoLink()
    {
        var html = ErrorPageRenderer.RenderQuarantined(null, _hostname);
        html.Should().Contain($"mailto:abuse@{_hostname}");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_DoesNotContainAbuseLink()
    {
        var html = ErrorPageRenderer.RenderNotFound(null, _hostname);
        html.Should().NotContain("mailto:");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderExpired_DoesNotContainAbuseLink()
    {
        var html = ErrorPageRenderer.RenderExpired(null, _hostname, "time");
        html.Should().NotContain("mailto:");
    }

    // ── Branding ──────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_WithBrandColor_AppliesColorInStyle()
    {
        var config = new DomainConfig { Hostname = _hostname, BrandColor = "#e63946" };
        var html = ErrorPageRenderer.RenderNotFound(config, _hostname);
        html.Should().Contain("#e63946");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_WithThreeDigitBrandColor_AppliesColorInStyle()
    {
        var config = new DomainConfig { Hostname = _hostname, BrandColor = "#f0f" };
        var html = ErrorPageRenderer.RenderNotFound(config, _hostname);
        html.Should().Contain("#f0f");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_NullConfig_UsesDefaultColor()
    {
        var html = ErrorPageRenderer.RenderNotFound(null, _hostname);
        // Default color must appear somewhere in the CSS
        html.Should().Contain("#4f46e5");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_ConfigWithNullBrandColor_UsesDefaultColor()
    {
        var config = new DomainConfig { Hostname = _hostname, BrandColor = null };
        var html = ErrorPageRenderer.RenderNotFound(config, _hostname);
        html.Should().Contain("#4f46e5");
    }

    // ── Logo ──────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_WithHttpsLogoUrl_RendersImgTag()
    {
        var config = new DomainConfig { Hostname = _hostname, LogoUrl = "https://cdn.example.com/logo.png" };
        var html = ErrorPageRenderer.RenderNotFound(config, _hostname);
        html.Should().Contain("<img");
        html.Should().Contain("https://cdn.example.com/logo.png");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_WithHttpLogoUrl_RendersImgTag()
    {
        var config = new DomainConfig { Hostname = _hostname, LogoUrl = "http://cdn.example.com/logo.png" };
        var html = ErrorPageRenderer.RenderNotFound(config, _hostname);
        html.Should().Contain("<img");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_NullLogoUrl_NoImgTag()
    {
        var config = new DomainConfig { Hostname = _hostname, LogoUrl = null };
        var html = ErrorPageRenderer.RenderNotFound(config, _hostname);
        html.Should().NotContain("<img");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_JavascriptLogoUrl_OmitsImgTag()
    {
        // LogoUrl with javascript: scheme must never be rendered into <img src>.
        // (Repository validation normally prevents this, but the renderer guards too.)
        var config = new DomainConfig { Hostname = _hostname, LogoUrl = "javascript:alert(1)" };
        var html = ErrorPageRenderer.RenderNotFound(config, _hostname);
        html.Should().NotContain("<img");
        html.Should().NotContain("javascript:");
    }

    // ── XSS / injection prevention ────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderSuspended_HostnameWithXssPayload_IsHtmlEncoded()
    {
        var maliciousHost = "<script>alert(1)</script>";
        var html = ErrorPageRenderer.RenderSuspended(null, maliciousHost);
        html.Should().NotContain("<script>");
        html.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_LogoUrlWithSpecialChars_IsHtmlEncoded()
    {
        var config = new DomainConfig
        {
            Hostname = _hostname,
            LogoUrl = """https://cdn.example.com/logo.png?a=1&b=2"""
        };
        var html = ErrorPageRenderer.RenderNotFound(config, _hostname);
        // Ampersand in URL must be encoded as &amp; inside the HTML attribute
        html.Should().Contain("&amp;");
        html.Should().NotContain("""a=1&b=2""");
    }

    // ── Custom message ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_WithCustomMessage_ContainsMessage()
    {
        var config = new DomainConfig { Hostname = _hostname, CustomMessage = "Please try again later." };
        var html = ErrorPageRenderer.RenderNotFound(config, _hostname);
        html.Should().Contain("Please try again later.");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_NoCustomMessage_DoesNotContainCustomMsgElement()
    {
        var html = ErrorPageRenderer.RenderNotFound(null, _hostname);
        // The CSS class definition is always present; the element must not be.
        html.Should().NotContain("class=\"custom-msg\"");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_CustomMessageWithXssPayload_IsHtmlEncoded()
    {
        var config = new DomainConfig { Hostname = _hostname, CustomMessage = "<script>alert(1)</script>" };
        var html = ErrorPageRenderer.RenderNotFound(config, _hostname);
        html.Should().NotContain("<script>");
        html.Should().Contain("&lt;script&gt;");
    }

    // ── Support URL ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_WithSupportUrl_ContainsGetHelpLink()
    {
        var config = new DomainConfig { Hostname = _hostname, SupportUrl = "https://support.example.com" };
        var html = ErrorPageRenderer.RenderNotFound(config, _hostname);
        html.Should().Contain("https://support.example.com");
        html.Should().Contain("Get help");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_NoSupportUrl_DoesNotContainGetHelp()
    {
        var html = ErrorPageRenderer.RenderNotFound(null, _hostname);
        html.Should().NotContain("Get help");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_SupportUrlWithSpecialChars_IsHtmlEncoded()
    {
        var config = new DomainConfig { Hostname = _hostname, SupportUrl = "https://support.example.com?a=1&b=2" };
        var html = ErrorPageRenderer.RenderNotFound(config, _hostname);
        html.Should().Contain("&amp;");
        html.Should().NotContain("a=1&b=2");
    }

    // ── Custom CSS ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_WithCustomCss_InjectsStyleBlock()
    {
        var config = new DomainConfig { Hostname = _hostname, CustomCss = "body{font-family:Georgia,serif}" };
        var html = ErrorPageRenderer.RenderNotFound(config, _hostname);
        html.Should().Contain("<style>body{font-family:Georgia,serif}</style>");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_NoCustomCss_DoesNotContainExtraStyleBlock()
    {
        // Should contain exactly one <style> block (the base styles)
        var html = ErrorPageRenderer.RenderNotFound(null, _hostname);
        var styleCount = html.Split("<style>").Length - 1;
        styleCount.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_WithCustomCss_CustomCssBlockAppearsAfterBaseStyles()
    {
        var config = new DomainConfig { Hostname = _hostname, CustomCss = ".card{border:2px solid red}" };
        var html = ErrorPageRenderer.RenderNotFound(config, _hostname);
        var firstStyleEnd = html.IndexOf("</style>", StringComparison.Ordinal);
        var customStyleStart = html.IndexOf(".card{border:2px solid red}", StringComparison.Ordinal);
        customStyleStart.Should().BeGreaterThan(firstStyleEnd);
    }

    // ── HTML minification ─────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_OutputHasNoLeadingWhitespace()
    {
        var html = ErrorPageRenderer.RenderNotFound(null, _hostname);
        foreach (var line in html.Split('\n'))
        {
            if (line.Length > 0)
                line[0].Should().NotBe(' ', because: $"line should not start with a space: '{line}'");
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RenderNotFound_OutputHasNoBlankLines()
    {
        var html = ErrorPageRenderer.RenderNotFound(null, _hostname);
        html.Split('\n').Should().NotContain("");
    }
}
