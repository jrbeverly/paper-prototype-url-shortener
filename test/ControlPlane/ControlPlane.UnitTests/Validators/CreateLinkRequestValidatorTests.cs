using ControlPlane.Api.Models.Requests;
using FluentValidation.TestHelper;

namespace ControlPlane.UnitTests.Validators;

public sealed class CreateLinkRequestValidatorTests
{
    private readonly CreateLinkRequestValidator _sut = new();

    private static CreateLinkRequest Valid() => new()
    {
        DomainId = Guid.NewGuid(),
        DestinationUrl = "https://example.com/page",
        RedirectType = "302"
    };

    // ── DestinationUrl ───────────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("https://example.com")]
    [InlineData("https://example.com/path?q=1")]
    [InlineData("http://localhost:3000")]
    public void DestinationUrl_Valid_PassesValidation(string url)
    {
        var result = _sut.TestValidate(Valid() with { DestinationUrl = url });
        result.ShouldNotHaveValidationErrorFor(x => x.DestinationUrl);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    [InlineData("//example.com")]
    public void DestinationUrl_Invalid_FailsValidation(string url)
    {
        var result = _sut.TestValidate(Valid() with { DestinationUrl = url });
        result.ShouldHaveValidationErrorFor(x => x.DestinationUrl);
    }

    // ── Slug ─────────────────────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("abc123")]
    [InlineData("my-link")]
    [InlineData("a")]
    [InlineData("ABC-def-123")]
    public void Slug_ValidFormat_PassesValidation(string slug)
    {
        var result = _sut.TestValidate(Valid() with { Slug = slug });
        result.ShouldNotHaveValidationErrorFor(x => x.Slug);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("-starts-with-hyphen")]
    [InlineData("ends-with-hyphen-")]
    [InlineData("has spaces")]
    [InlineData("has_underscore")]
    public void Slug_InvalidFormat_FailsValidation(string slug)
    {
        var result = _sut.TestValidate(Valid() with { Slug = slug });
        result.ShouldHaveValidationErrorFor(x => x.Slug);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Slug_ExceedsMaxLength_FailsValidation()
    {
        var slug = new string('a', 101);
        var result = _sut.TestValidate(Valid() with { Slug = slug });
        result.ShouldHaveValidationErrorFor(x => x.Slug);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Slug_Null_PassesValidation()
    {
        var result = _sut.TestValidate(Valid() with { Slug = null });
        result.ShouldNotHaveValidationErrorFor(x => x.Slug);
    }

    // ── RedirectType ─────────────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("301")]
    [InlineData("302")]
    [InlineData("307")]
    [InlineData("308")]
    public void RedirectType_AllowedValues_PassesValidation(string redirectType)
    {
        var result = _sut.TestValidate(Valid() with { RedirectType = redirectType });
        result.ShouldNotHaveValidationErrorFor(x => x.RedirectType);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("200")]
    [InlineData("303")]
    [InlineData("404")]
    [InlineData("")]
    [InlineData("permanent")]
    public void RedirectType_InvalidValues_FailsValidation(string redirectType)
    {
        var result = _sut.TestValidate(Valid() with { RedirectType = redirectType });
        result.ShouldHaveValidationErrorFor(x => x.RedirectType);
    }

    // ── ExpiresAt ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void ExpiresAt_FutureDate_PassesValidation()
    {
        var result = _sut.TestValidate(Valid() with { ExpiresAt = DateTime.UtcNow.AddDays(1) });
        result.ShouldNotHaveValidationErrorFor(x => x.ExpiresAt);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ExpiresAt_PastDate_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { ExpiresAt = DateTime.UtcNow.AddDays(-1) });
        result.ShouldHaveValidationErrorFor(x => x.ExpiresAt);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ExpiresAt_Null_PassesValidation()
    {
        var result = _sut.TestValidate(Valid() with { ExpiresAt = null });
        result.ShouldNotHaveValidationErrorFor(x => x.ExpiresAt);
    }

    // ── Full valid request ────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void FullValidRequest_PassesAllValidations()
    {
        var result = _sut.TestValidate(Valid());
        result.IsValid.Should().BeTrue();
    }
}
