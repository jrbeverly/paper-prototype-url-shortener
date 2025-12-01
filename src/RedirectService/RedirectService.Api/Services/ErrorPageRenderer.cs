using System.Net;
using System.Text;
using RedirectService.Api.Models;

namespace RedirectService.Api.Services;

/// <summary>
/// Generates branded, self-contained HTML error pages for non-redirect outcomes.
/// Pages include no external resources so they load in every network condition.
/// All caller-supplied strings are HTML-encoded before insertion.
/// Brand colors are validated at the repository layer; the renderer applies a safe
/// default when the config carries no color.
/// Custom CSS is validated at the repository layer (max 2 000 chars, no injection
/// sequences) and injected without re-encoding into a separate &lt;style&gt; block.
/// HTML output is minified (leading whitespace stripped, blank lines removed) to
/// reduce response size.
/// </summary>
public static class ErrorPageRenderer
{
    private const string _defaultColor = "#4f46e5";

    /// <summary>Renders a "Link Not Found" page.</summary>
    public static string RenderNotFound(DomainConfig? config, string hostname)
        => Render(
            config, hostname,
            title: "Link Not Found",
            message: "The link you followed doesn't exist or may have been removed.",
            showAbuseLink: false);

    /// <summary>Renders a "Link Expired" page with a reason-specific message.</summary>
    public static string RenderExpired(DomainConfig? config, string hostname, string? reason)
    {
        var message = reason == "clicks"
            ? "This link has reached its maximum number of uses and is no longer active."
            : "This link has expired and is no longer active.";
        return Render(config, hostname, title: "Link Expired", message: message, showAbuseLink: false);
    }

    /// <summary>Renders a "Link Unavailable" page for suspended links, with an abuse report link.</summary>
    public static string RenderSuspended(DomainConfig? config, string hostname)
        => Render(
            config, hostname,
            title: "Link Unavailable",
            message: "This link has been suspended and is no longer available.",
            showAbuseLink: true);

    /// <summary>Renders a "Link Under Review" page for quarantined links, with an abuse report link.</summary>
    public static string RenderQuarantined(DomainConfig? config, string hostname)
        => Render(
            config, hostname,
            title: "Link Under Review",
            message: "This link is temporarily unavailable while it is being reviewed for policy compliance.",
            showAbuseLink: true);

    private static string Render(
        DomainConfig? config, string hostname,
        string title, string message, bool showAbuseLink)
    {
        var color = config?.BrandColor ?? _defaultColor;
        var safeTitle = Encode(title);
        var safeMessage = Encode(message);
        var logoHtml = BuildLogoHtml(config?.LogoUrl);
        var abuseHtml = showAbuseLink
            ? $"""<p class="abuse"><a href="mailto:abuse@{Encode(hostname)}">Report abuse</a></p>"""
            : "";
        var customMsgHtml = BuildCustomMessageHtml(config?.CustomMessage);
        var supportHtml = BuildSupportHtml(config?.SupportUrl, color);
        var customCssHtml = BuildCustomCssHtml(config?.CustomCss);

        // $$"""...""" uses double-dollar prefix so CSS braces are literal
        // and only {{expr}} is treated as a C# interpolation.
        var html = $$"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                  <meta charset="UTF-8">
                  <meta name="viewport" content="width=device-width,initial-scale=1">
                  <title>{{safeTitle}}</title>
                  <style>
                    *,*::before,*::after{box-sizing:border-box;margin:0;padding:0}
                    body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#f4f6f8;display:flex;align-items:center;justify-content:center;min-height:100vh;padding:16px}
                    .card{background:#fff;border-radius:12px;box-shadow:0 4px 20px rgba(0,0,0,.08);padding:48px 40px;text-align:center;max-width:440px;width:100%}
                    .bar{width:48px;height:4px;background:{{color}};border-radius:2px;margin:0 auto 28px}
                    .logo{max-height:48px;max-width:180px;margin:0 auto 24px;display:block}
                    h1{font-size:20px;font-weight:700;color:#111;margin-bottom:10px}
                    p{font-size:15px;color:#555;line-height:1.6}
                    .custom-msg{margin-top:12px;font-size:14px;color:#444}
                    .support{margin-top:20px;font-size:13px}
                    .support a{color:{{color}};text-decoration:none}
                    .support a:hover{text-decoration:underline}
                    .abuse{margin-top:24px;font-size:13px;color:#888}
                    .abuse a{color:{{color}};text-decoration:none}
                    .abuse a:hover{text-decoration:underline}
                  </style>
                  {{customCssHtml}}
                </head>
                <body>
                  <div class="card">
                    {{logoHtml}}
                    <div class="bar"></div>
                    <h1>{{safeTitle}}</h1>
                    <p>{{safeMessage}}</p>
                    {{customMsgHtml}}
                    {{abuseHtml}}
                    {{supportHtml}}
                  </div>
                </body>
                </html>
                """;

        return Minify(html);
    }

    private static string BuildLogoHtml(string? logoUrl)
    {
        if (string.IsNullOrEmpty(logoUrl)) return "";
        // Only emit <img> tags for http/https URLs — prevents javascript:/data: injection.
        if (!logoUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !logoUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return "";
        return $"""<img src="{Encode(logoUrl)}" alt="Logo" class="logo">""";
    }

    private static string BuildCustomMessageHtml(string? customMessage)
    {
        if (string.IsNullOrEmpty(customMessage)) return "";
        return $"""<p class="custom-msg">{Encode(customMessage)}</p>""";
    }

    private static string BuildSupportHtml(string? supportUrl, string color)
    {
        if (string.IsNullOrEmpty(supportUrl)) return "";
        // URL is already validated as http/https by the repository.
        return $"""<p class="support"><a href="{Encode(supportUrl)}">Get help</a></p>""";
    }

    // Custom CSS is injected into its own <style> block after the base styles so
    // tenant rules can override defaults. The CSS has been validated at the repository
    // layer (no </style or <script sequences, max 2 000 chars); no additional encoding
    // is applied here since CSS content must not be HTML-encoded.
    private static string BuildCustomCssHtml(string? customCss)
    {
        if (string.IsNullOrEmpty(customCss)) return "";
        return $"<style>{customCss}</style>";
    }

    // Strips leading whitespace from each line and removes blank lines.
    // This reduces response size by ~25–30% without altering content or semantics.
    private static string Minify(string html)
        => string.Join('\n',
            html.Split('\n')
                .Select(line => line.TrimStart())
                .Where(line => line.Length > 0));

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
