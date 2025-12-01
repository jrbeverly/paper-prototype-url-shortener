using ControlPlane.Api.Authorization;
using FluentValidation;

namespace ControlPlane.Api.Models.Requests;

public record CreateApiKeyRequest
{
    /// <summary>A human-readable name for the API key (shown in the dashboard).</summary>
    public required string Name { get; init; }

    /// <summary>Role-based permissions assigned to the key. Defaults to "viewer".</summary>
    public List<string> Permissions { get; init; } = [Roles.Viewer];
}

public class CreateApiKeyRequestValidator : AbstractValidator<CreateApiKeyRequest>
{
    private static readonly string _validRoles = string.Join(", ", Roles.All.Order());

    public CreateApiKeyRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters");

        RuleFor(x => x.Permissions)
            .NotEmpty().WithMessage("At least one role is required")
            .ForEach(p => p.Must(perm => Roles.All.Contains(perm))
                .WithMessage($"Role must be one of: {_validRoles}"));
    }
}
