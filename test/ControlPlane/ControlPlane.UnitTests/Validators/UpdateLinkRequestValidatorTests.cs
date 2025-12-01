using ControlPlane.Api.Models.Requests;
using FluentValidation.TestHelper;

namespace ControlPlane.UnitTests.Validators;

public sealed class UpdateLinkRequestValidatorTests
{
    private readonly UpdateLinkRequestValidator _sut = new();

    // Empty update is valid — all fields are optional.
    [Fact]
    [Trait("Category", "Unit")]
    public void EmptyRequest_PassesValidation()
    {
        var result = _sut.TestValidate(new UpdateLinkRequest());
        result.IsValid.Should().BeTrue();
    }

    // ── DestinationUrl ───────────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("https://example.com")]
    [InlineData("http://example.com/path")]
    public void DestinationUrl_ValidWhenProvided_PassesValidation(string url)
    {
        var result = _sut.TestValidate(new UpdateLinkRequest { DestinationUrl = url });
        result.ShouldNotHaveValidationErrorFor(x => x.DestinationUrl);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    [InlineData("relative/path")]
    public void DestinationUrl_InvalidWhenProvided_FailsValidation(string url)
    {
        var result = _sut.TestValidate(new UpdateLinkRequest { DestinationUrl = url });
        result.ShouldHaveValidationErrorFor(x => x.DestinationUrl);
    }

    // ── RedirectType ─────────────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("301")]
    [InlineData("302")]
    [InlineData("307")]
    [InlineData("308")]
    public void RedirectType_ValidWhenProvided_PassesValidation(string redirectType)
    {
        var result = _sut.TestValidate(new UpdateLinkRequest { RedirectType = redirectType });
        result.ShouldNotHaveValidationErrorFor(x => x.RedirectType);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("303")]
    [InlineData("permanent")]
    public void RedirectType_InvalidWhenProvided_FailsValidation(string redirectType)
    {
        var result = _sut.TestValidate(new UpdateLinkRequest { RedirectType = redirectType });
        result.ShouldHaveValidationErrorFor(x => x.RedirectType);
    }

    // ── ExpiresAt ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void ExpiresAt_FutureDate_PassesValidation()
    {
        var result = _sut.TestValidate(new UpdateLinkRequest { ExpiresAt = DateTime.UtcNow.AddDays(7) });
        result.ShouldNotHaveValidationErrorFor(x => x.ExpiresAt);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ExpiresAt_PastDate_FailsValidation()
    {
        var result = _sut.TestValidate(new UpdateLinkRequest { ExpiresAt = DateTime.UtcNow.AddDays(-1) });
        result.ShouldHaveValidationErrorFor(x => x.ExpiresAt);
    }

    // ── Status ────────────────────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("active")]
    [InlineData("paused")]
    public void Status_ValidWhenProvided_PassesValidation(string status)
    {
        var result = _sut.TestValidate(new UpdateLinkRequest { Status = status });
        result.ShouldNotHaveValidationErrorFor(x => x.Status);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("deleted")]
    [InlineData("ACTIVE")]
    [InlineData("unknown")]
    public void Status_InvalidWhenProvided_FailsValidation(string status)
    {
        var result = _sut.TestValidate(new UpdateLinkRequest { Status = status });
        result.ShouldHaveValidationErrorFor(x => x.Status);
    }
}
