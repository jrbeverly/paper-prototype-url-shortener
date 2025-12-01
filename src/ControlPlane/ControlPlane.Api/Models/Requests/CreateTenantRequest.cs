using FluentValidation;
using ControlPlane.Api.Services;

namespace ControlPlane.Api.Models.Requests;

public record CreateTenantRequest
{
    /// <summary>The display name for the tenant workspace (2–100 characters).</summary>
    public required string Name { get; init; }

    /// <summary>The billing contact email address.</summary>
    public required string Email { get; init; }

    /// <summary>The subscription plan: free, starter, pro, or enterprise. Defaults to free.</summary>
    public string? Plan { get; init; }
}

public class CreateTenantRequestValidator : AbstractValidator<CreateTenantRequest>
{
    private static readonly IReadOnlySet<string> _validPlans = PlanCatalog.ValidIds;

    public CreateTenantRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MinimumLength(2).WithMessage("Name must be at least 2 characters")
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Email must be a valid email address")
            .MaximumLength(254).WithMessage("Email cannot exceed 254 characters");

        RuleFor(x => x.Plan)
            .Must(p => p is null || _validPlans.Contains(p))
            .WithMessage("Plan must be one of: free, starter, pro, team, business, enterprise");
    }
}
