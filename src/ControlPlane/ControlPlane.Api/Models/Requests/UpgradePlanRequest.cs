using FluentValidation;

namespace ControlPlane.Api.Models.Requests;

public sealed record UpgradePlanRequest
{
    public required string Plan { get; init; }
}

public class UpgradePlanRequestValidator : AbstractValidator<UpgradePlanRequest>
{
    public UpgradePlanRequestValidator()
    {
        RuleFor(x => x.Plan)
            .NotEmpty().WithMessage("Plan ID is required.");
    }
}
