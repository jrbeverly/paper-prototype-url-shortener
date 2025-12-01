using ControlPlane.Api.Models.Requests;
using FluentValidation.TestHelper;

namespace ControlPlane.UnitTests.Validators;

public sealed class ExtendTrialRequestValidatorTests
{
    private readonly ExtendTrialRequestValidator _sut = new();

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(90)]
    public void AdditionalDays_ValidRange_PassesValidation(int days)
    {
        var result = _sut.TestValidate(new ExtendTrialRequest { AdditionalDays = days });
        result.ShouldNotHaveValidationErrorFor(x => x.AdditionalDays);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void AdditionalDays_Zero_FailsValidation(int days)
    {
        var result = _sut.TestValidate(new ExtendTrialRequest { AdditionalDays = days });
        result.ShouldHaveValidationErrorFor(x => x.AdditionalDays);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(91)]
    [InlineData(365)]
    public void AdditionalDays_ExceedsMaximum_FailsValidation(int days)
    {
        var result = _sut.TestValidate(new ExtendTrialRequest { AdditionalDays = days });
        result.ShouldHaveValidationErrorFor(x => x.AdditionalDays);
    }
}
