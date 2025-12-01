using FluentValidation;

namespace ControlPlane.Api.Models.Requests;

public record SuspendTenantRequest
{
    /// <summary>Optional reason for the suspension (max 500 characters).</summary>
    public string? Reason { get; init; }
}

public class SuspendTenantRequestValidator : AbstractValidator<SuspendTenantRequest>
{
    public SuspendTenantRequestValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(500).WithMessage("Reason cannot exceed 500 characters")
            .When(x => x.Reason is not null);
    }
}
