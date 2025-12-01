namespace RedirectService.Api.Models;

/// <summary>Outcome of a redirect resolution attempt.</summary>
public enum RedirectOutcome
{
    /// <summary>A redirect was resolved; <see cref="RedirectResult.DestinationUrl"/> is populated.</summary>
    Redirect,
    /// <summary>No active link exists for the requested host+slug pair.</summary>
    NotFound,
    /// <summary>The link exists but its expiry timestamp has passed.</summary>
    Expired,
    /// <summary>The link is suspended (tenant suspension or administrative action).</summary>
    Suspended,
    /// <summary>The link is quarantined pending abuse review.</summary>
    Quarantined
}

/// <summary>
/// Result returned by <see cref="Services.IRedirectService.ResolveAsync"/>.
/// </summary>
public sealed record RedirectResult
{
    /// <summary>How the lookup concluded.</summary>
    public required RedirectOutcome Outcome { get; init; }

    /// <summary>
    /// Destination URL to redirect to.
    /// Non-null only when <see cref="Outcome"/> is <see cref="RedirectOutcome.Redirect"/>.
    /// </summary>
    public string? DestinationUrl { get; init; }

    /// <summary>
    /// HTTP status code to return.
    /// 301/302/307/308 for redirects, 404 for not found, 410 for expired, 403 for quarantined, 451 for suspended.
    /// </summary>
    public int StatusCode { get; init; }

    /// <summary>Reason for expiration: <c>"time"</c> for past <c>ExpiresAt</c>, <c>"clicks"</c> for click-limit reached.</summary>
    public string? ExpiredReason { get; init; }

    /// <summary>Creates a not-found result (HTTP 404).</summary>
    public static RedirectResult NotFound() =>
        new() { Outcome = RedirectOutcome.NotFound, StatusCode = 404 };

    /// <summary>Creates an expired result (HTTP 410 Gone) with the specified reason.</summary>
    public static RedirectResult Expired(string reason) =>
        new() { Outcome = RedirectOutcome.Expired, StatusCode = 410, ExpiredReason = reason };

    /// <summary>Creates a successful redirect result with the specified destination and status code.</summary>
    public static RedirectResult Redirect(string destinationUrl, int statusCode) =>
        new() { Outcome = RedirectOutcome.Redirect, DestinationUrl = destinationUrl, StatusCode = statusCode };

    /// <summary>Creates a suspended result (HTTP 451 Unavailable For Legal Reasons).</summary>
    public static RedirectResult Suspended() =>
        new() { Outcome = RedirectOutcome.Suspended, StatusCode = 451 };

    /// <summary>Creates a quarantined result (HTTP 403 Forbidden).</summary>
    public static RedirectResult Quarantined() =>
        new() { Outcome = RedirectOutcome.Quarantined, StatusCode = 403 };
}
