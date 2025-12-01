using ControlPlane.Api.Models.Requests;
using FluentValidation.TestHelper;

namespace ControlPlane.UnitTests.Validators;

public sealed class CreateTenantRequestValidatorTests
{
    private readonly CreateTenantRequestValidator _sut = new();

    private static CreateTenantRequest Valid() => new()
    {
        Name = "Acme Corp",
        Email = "admin@acme.com"
    };

    // ── Name ──────────────────────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("Ab")]
    [InlineData("Acme Corporation")]
    public void Name_Valid_PassesValidation(string name)
    {
        var result = _sut.TestValidate(Valid() with { Name = name });
        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Name_Empty_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { Name = "" });
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Name_TooShort_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { Name = "A" });
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Name_ExceedsMaxLength_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { Name = new string('x', 101) });
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Name_AtMaxLength_PassesValidation()
    {
        var result = _sut.TestValidate(Valid() with { Name = new string('x', 100) });
        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    // ── Email ─────────────────────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("user@example.com")]
    [InlineData("user+tag@sub.domain.org")]
    public void Email_Valid_PassesValidation(string email)
    {
        var result = _sut.TestValidate(Valid() with { Email = email });
        result.ShouldNotHaveValidationErrorFor(x => x.Email);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("@missing-local.com")]
    [InlineData("missing-at-sign.com")]
    public void Email_Invalid_FailsValidation(string email)
    {
        var result = _sut.TestValidate(Valid() with { Email = email });
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    // ── Plan ──────────────────────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("free")]
    [InlineData("starter")]
    [InlineData("pro")]
    [InlineData("team")]
    [InlineData("business")]
    [InlineData("enterprise")]
    public void Plan_ValidIds_PassesValidation(string plan)
    {
        var result = _sut.TestValidate(Valid() with { Plan = plan });
        result.ShouldNotHaveValidationErrorFor(x => x.Plan);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Plan_Null_PassesValidation()
    {
        var result = _sut.TestValidate(Valid() with { Plan = null });
        result.ShouldNotHaveValidationErrorFor(x => x.Plan);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Plan_UnknownId_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { Plan = "ultra" });
        result.ShouldHaveValidationErrorFor(x => x.Plan);
    }
}
