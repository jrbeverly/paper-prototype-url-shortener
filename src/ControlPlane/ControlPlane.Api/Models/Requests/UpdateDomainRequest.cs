using FluentValidation;

namespace ControlPlane.Api.Models.Requests;

public sealed record UpdateDomainRequest
{
    /// <summary>The default URL to redirect to when a visitor hits the domain root.</summary>
    public string? DefaultRedirectUrl { get; init; }

    /// <summary>Custom branding for error pages shown to visitors.</summary>
    public string? ErrorPageBranding { get; init; }

    /// <summary>Behavior when no matching link is found: "404" (show error page), "redirect" (redirect to default URL), or "passthrough" (forward the request).</summary>
    public string? NotFoundBehavior { get; init; }
}

public class UpdateDomainRequestValidator : AbstractValidator<UpdateDomainRequest>
{
    private static readonly HashSet<string> _validBehaviors = ["404", "redirect", "passthrough"];

    public UpdateDomainRequestValidator()
    {
        RuleFor(x => x.DefaultRedirectUrl)
            .Must(url => string.IsNullOrWhiteSpace(url) || Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("DefaultRedirectUrl must be a valid absolute URL or empty.");

        RuleFor(x => x.NotFoundBehavior)
            .Must(behavior => string.IsNullOrWhiteSpace(behavior) || _validBehaviors.Contains(behavior!))
            .WithMessage($"NotFoundBehavior must be one of: {string.Join(", ", _validBehaviors)}.");
    }
}
