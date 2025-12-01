using FluentValidation;

namespace ControlPlane.Api.Models.Requests;

public record UpdateLinkRequest
{
    /// <summary>New destination URL. Must be a valid absolute URL if provided.</summary>
    public string? DestinationUrl { get; init; }

    /// <summary>New HTTP redirect type: 301, 302, 307, or 308.</summary>
    public string? RedirectType { get; init; }

    /// <summary>New expiration date. The link stops redirecting after this date.</summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>Set to true to remove the expiration date.</summary>
    public bool ClearExpiresAt { get; init; }

    /// <summary>Maximum number of clicks before the link expires. Must be > 0 if set.</summary>
    public int? MaxClicks { get; init; }

    /// <summary>Set to true to remove the click limit.</summary>
    public bool ClearMaxClicks { get; init; }

    /// <summary>Key-value metadata for link routing rules.</summary>
    public Dictionary<string, string>? Rules { get; init; }

    /// <summary>Set to true to remove all rules.</summary>
    public bool ClearRules { get; init; }

    /// <summary>Link status: "active" or "paused".</summary>
    public string? Status { get; init; }
}

public class UpdateLinkRequestValidator : AbstractValidator<UpdateLinkRequest>
{
    private static readonly HashSet<string> _allowedRedirectTypes = ["301", "302", "307", "308"];
    private static readonly HashSet<string> _allowedStatuses = ["active", "paused"];

    public UpdateLinkRequestValidator()
    {
        RuleFor(x => x.DestinationUrl)
            .Must(BeAValidAbsoluteUrl)
            .WithMessage("Destination URL must be a valid absolute URL (e.g., https://example.com/page).")
            .When(x => x.DestinationUrl is not null);

        RuleFor(x => x.RedirectType)
            .Must(rt => _allowedRedirectTypes.Contains(rt!))
            .WithMessage($"Redirect type must be one of: {string.Join(", ", _allowedRedirectTypes)}.")
            .When(x => x.RedirectType is not null);

        RuleFor(x => x.ExpiresAt)
            .Must(d => d > DateTime.UtcNow)
            .WithMessage("Expires at must be a future date.")
            .When(x => x.ExpiresAt.HasValue);

        RuleFor(x => x.MaxClicks)
            .GreaterThan(0)
            .WithMessage("Max clicks must be greater than 0.")
            .When(x => x.MaxClicks.HasValue);

        RuleFor(x => x.Status)
            .Must(s => _allowedStatuses.Contains(s!))
            .WithMessage($"Status must be one of: {string.Join(", ", _allowedStatuses)}.")
            .When(x => x.Status is not null);
    }

    private static bool BeAValidAbsoluteUrl(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
