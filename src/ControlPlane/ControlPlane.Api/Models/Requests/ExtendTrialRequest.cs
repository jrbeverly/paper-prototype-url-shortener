using FluentValidation;

namespace ControlPlane.Api.Models.Requests;

public record ExtendTrialRequest
{
    /// <summary>Number of additional days to add to the trial. Must be between 1 and 90.</summary>
    public required int AdditionalDays { get; init; }
}

public class ExtendTrialRequestValidator : AbstractValidator<ExtendTrialRequest>
{
    public ExtendTrialRequestValidator()
    {
        RuleFor(x => x.AdditionalDays)
            .GreaterThan(0).WithMessage("AdditionalDays must be at least 1")
            .LessThanOrEqualTo(90).WithMessage("AdditionalDays cannot exceed 90");
    }
}
