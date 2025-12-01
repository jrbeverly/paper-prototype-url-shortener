using ControlPlane.Api.Models.Requests;
using FluentValidation.TestHelper;

namespace ControlPlane.UnitTests.Validators;

public sealed class CreateDomainRequestValidatorTests
{
    private readonly CreateDomainRequestValidator _sut = new();

    // ── Valid hostnames ───────────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("go.example.com")]
    [InlineData("links.my-brand.io")]
    [InlineData("example.com")]
    [InlineData("sub.sub.example.co.uk")]
    [InlineData("a.io")]
    public void Hostname_ValidFormat_PassesValidation(string hostname)
    {
        var result = _sut.TestValidate(new CreateDomainRequest { Hostname = hostname });
        result.ShouldNotHaveValidationErrorFor(x => x.Hostname);
    }

    // ── Invalid hostnames ─────────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("")]
    [InlineData("https://example.com")]
    [InlineData("http://example.com")]
    [InlineData("example.com/path")]
    [InlineData("not-a-domain")]
    [InlineData("-starts-with-hyphen.com")]
    [InlineData("ends-with-hyphen-.com")]
    public void Hostname_InvalidFormat_FailsValidation(string hostname)
    {
        var result = _sut.TestValidate(new CreateDomainRequest { Hostname = hostname });
        result.ShouldHaveValidationErrorFor(x => x.Hostname);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Hostname_ExceedsMaxLength_FailsValidation()
    {
        // 254 chars is over the 253 limit
        var hostname = string.Join(".", Enumerable.Repeat("abcdefgh", 30)) + ".com";
        var result = _sut.TestValidate(new CreateDomainRequest { Hostname = hostname });
        result.ShouldHaveValidationErrorFor(x => x.Hostname);
    }
}
