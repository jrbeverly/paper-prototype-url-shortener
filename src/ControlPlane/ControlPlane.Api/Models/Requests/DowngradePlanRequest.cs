using FluentValidation;

namespace ControlPlane.Api.Models.Requests;

public sealed record DowngradePlanRequest
{
    public required string Plan { get; init; }
}

public class DowngradePlanRequestValidator : AbstractValidator<DowngradePlanRequest>
{
    public DowngradePlanRequestValidator()
    {
        RuleFor(x => x.Plan)
            .NotEmpty().WithMessage("Plan ID is required.");
    }
}
