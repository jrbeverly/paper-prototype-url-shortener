using FluentValidation;

namespace ControlPlane.Api.Models.Requests;

public record CreateDomainRequest
{
    /// <summary>The custom domain hostname to register (e.g., go.example.com). Do not include protocol or path.</summary>
    public required string Hostname { get; init; }
}

public class CreateDomainRequestValidator : AbstractValidator<CreateDomainRequest>
{
    public CreateDomainRequestValidator()
    {
        RuleFor(x => x.Hostname)
            .NotEmpty().WithMessage("Hostname is required")
            .MaximumLength(253).WithMessage("Hostname cannot exceed 253 characters")
            .Matches(@"^[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?)*\.[a-zA-Z]{2,}$")
            .WithMessage("Hostname must be a valid domain name (e.g., go.example.com). Do not include protocol or path.");
    }
}
