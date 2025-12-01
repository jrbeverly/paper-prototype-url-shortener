namespace RedirectService.Api.Models;

/// <summary>
/// Domain-level configuration for error page branding and fallback behavior.
/// Stored in DynamoDB at <c>HOST#{hostname}</c> / <c>DOMAIN_CONFIG</c> in the same single-table
/// as link records. All values are pre-validated by the repository before this record is returned.
/// </summary>
public sealed record DomainConfig
{
    /// <summary>Custom hostname (e.g. <c>go.customer.com</c>).</summary>
    public required string Hostname { get; init; }

    /// <summary>
    /// URL to redirect visitors to when a link is not found, expired, or blocked.
    /// When set, the visitor receives a 302 redirect to <c>{FallbackUrl}?error={code}</c>
    /// instead of an HTML error page. Only http/https URLs are accepted; the repository
    /// sets this to <see langword="null"/> if the stored value uses any other scheme.
    /// </summary>
    public string? FallbackUrl { get; init; }

    /// <summary>
    /// CSS hex color for the brand accent on error pages (e.g. <c>#3498db</c>).
    /// Accepts <c>#RRGGBB</c> or <c>#RGB</c> format only. The repository sets this to
    /// <see langword="null"/> if the stored value is not a valid hex color, preventing
    /// CSS injection. <see langword="null"/> causes the renderer to use a default color.
    /// </summary>
    public string? BrandColor { get; init; }

    /// <summary>
    /// HTTPS URL of a logo image displayed on error pages.
    /// Only http/https URLs are accepted. <see langword="null"/> means no logo is shown.
    /// </summary>
    public string? LogoUrl { get; init; }

    /// <summary>
    /// Optional per-tenant message displayed below the default error message on branded error
    /// pages. HTML-encoded before insertion. Truncated to 500 characters at the repository layer.
    /// </summary>
    public string? CustomMessage { get; init; }

    /// <summary>
    /// URL of a support or help page shown as a "Get help" link on error pages.
    /// Only http/https URLs are accepted; the repository sets this to <see langword="null"/>
    /// if the stored value uses any other scheme.
    /// </summary>
    public string? SupportUrl { get; init; }

    /// <summary>
    /// Additional CSS injected into a <c>&lt;style&gt;</c> block on error pages, allowing tenants
    /// to customize fonts, spacing, or other visual properties beyond the brand color.
    /// Validated by the repository: max 2 000 characters, must not contain
    /// <c>&lt;/style</c> or <c>&lt;script</c> sequences.
    /// </summary>
    public string? CustomCss { get; init; }
}
