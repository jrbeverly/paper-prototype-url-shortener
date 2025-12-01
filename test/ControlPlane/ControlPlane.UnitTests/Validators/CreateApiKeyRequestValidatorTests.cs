using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using FluentValidation.TestHelper;

namespace ControlPlane.UnitTests.Validators;

public sealed class CreateApiKeyRequestValidatorTests
{
    private readonly CreateApiKeyRequestValidator _sut = new();

    private static CreateApiKeyRequest Valid() => new()
    {
        Name = "My API Key",
        Permissions = [Roles.Viewer]
    };

    // ── Name ──────────────────────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("a")]
    [InlineData("My Key")]
    [InlineData("Production key — backend service")]
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

    // ── Permissions ────────────────────────────────────────────────────────────

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(Roles.Viewer)]
    [InlineData(Roles.Member)]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Owner)]
    public void Permissions_AllValidRoles_PassesValidation(string role)
    {
        var result = _sut.TestValidate(Valid() with { Permissions = [role] });
        result.ShouldNotHaveValidationErrorFor(x => x.Permissions);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Permissions_MultipleValidRoles_PassesValidation()
    {
        var result = _sut.TestValidate(Valid() with { Permissions = [Roles.Viewer, Roles.Member] });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Permissions_EmptyList_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { Permissions = [] });
        result.ShouldHaveValidationErrorFor(x => x.Permissions);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Permissions_UnknownRole_FailsValidation()
    {
        var result = _sut.TestValidate(Valid() with { Permissions = ["superuser"] });
        result.ShouldHaveValidationErrorFor(x => x.Permissions);
    }
}
