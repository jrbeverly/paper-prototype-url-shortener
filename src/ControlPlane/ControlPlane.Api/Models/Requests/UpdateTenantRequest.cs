using FluentValidation;

namespace ControlPlane.Api.Models.Requests;

public record UpdateTenantRequest
{
    /// <summary>New display name for the workspace (2–100 characters).</summary>
    public string? Name { get; init; }

    /// <summary>Absolute HTTPS URL for the workspace logo. Null leaves unchanged.</summary>
    public string? LogoUrl { get; init; }

    /// <summary>Default HTTP redirect type for new links: "301", "302", "307", or "308". Null leaves unchanged.</summary>
    public string? DefaultRedirectType { get; init; }

    /// <summary>Whether to receive workspace activity notifications. Null leaves unchanged.</summary>
    public bool? NotificationsEnabled { get; init; }

    /// <summary>Email address for workspace notifications. Null leaves unchanged.</summary>
    public string? NotificationEmail { get; init; }
}

public class UpdateTenantRequestValidator : AbstractValidator<UpdateTenantRequest>
{
    private static readonly HashSet<string> _validRedirectTypes = ["301", "302", "307", "308"];

    public UpdateTenantRequestValidator()
    {
        RuleFor(x => x.Name)
            .MinimumLength(2).WithMessage("Name must be at least 2 characters")
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters")
            .When(x => x.Name is not null);

        RuleFor(x => x.LogoUrl)
            .Must(BeAValidHttpsUrl)
            .WithMessage("LogoUrl must be a valid absolute HTTPS URL")
            .When(x => x.LogoUrl is not null);

        RuleFor(x => x.DefaultRedirectType)
            .Must(rt => _validRedirectTypes.Contains(rt!))
            .WithMessage($"DefaultRedirectType must be one of: {string.Join(", ", _validRedirectTypes)}")
            .When(x => x.DefaultRedirectType is not null);

        RuleFor(x => x.NotificationEmail)
            .EmailAddress().WithMessage("NotificationEmail must be a valid email address")
            .MaximumLength(254).WithMessage("NotificationEmail cannot exceed 254 characters")
            .When(x => x.NotificationEmail is not null);
    }

    private static bool BeAValidHttpsUrl(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    }
}
