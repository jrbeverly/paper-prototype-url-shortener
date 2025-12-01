using FluentValidation;

namespace ControlPlane.Api.Models.Requests;

public record CreateLinkRequest
{
    /// <summary>The domain to create the link on.</summary>
    public required Guid DomainId { get; init; }

    /// <summary>The destination URL to redirect to. Must be a valid absolute URL (http or https).</summary>
    public required string DestinationUrl { get; init; }

    /// <summary>Custom slug for the short link. Auto-generated if omitted.</summary>
    public string? Slug { get; init; }

    /// <summary>HTTP redirect type: 301 (permanent), 302 (temporary), 307 (temporary, preserve method), or 308 (permanent, preserve method). Defaults to 302.</summary>
    public string RedirectType { get; init; } = "302";

    /// <summary>When the link should expire. The link stops redirecting after this date.</summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>Maximum number of clicks before the link expires. Null means unlimited.</summary>
    public int? MaxClicks { get; init; }
}

public class CreateLinkRequestValidator : AbstractValidator<CreateLinkRequest>
{
    private static readonly HashSet<string> _allowedRedirectTypes = ["301", "302", "307", "308"];

    public CreateLinkRequestValidator()
    {
        RuleFor(x => x.DestinationUrl)
            .NotEmpty().WithMessage("Destination URL is required")
            .Must(BeAValidAbsoluteUrl).WithMessage("Destination URL must be a valid absolute URL (e.g., https://example.com/page).");

        RuleFor(x => x.Slug)
            .Matches(@"^[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?$")
            .WithMessage("Slug must contain only alphanumeric characters and hyphens, and cannot start or end with a hyphen.")
            .MaximumLength(100).WithMessage("Slug cannot exceed 100 characters.")
            .When(x => x.Slug is not null);

        RuleFor(x => x.RedirectType)
            .Must(rt => _allowedRedirectTypes.Contains(rt))
            .WithMessage($"Redirect type must be one of: {string.Join(", ", _allowedRedirectTypes)}.");

        RuleFor(x => x.ExpiresAt)
            .Must(d => d > DateTime.UtcNow)
            .WithMessage("Expires at must be a future date.")
            .When(x => x.ExpiresAt.HasValue);

        RuleFor(x => x.MaxClicks)
            .GreaterThan(0)
            .WithMessage("Max clicks must be greater than 0.")
            .When(x => x.MaxClicks.HasValue);
    }

    private static bool BeAValidAbsoluteUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
