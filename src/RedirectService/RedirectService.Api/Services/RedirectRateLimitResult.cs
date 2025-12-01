namespace RedirectService.Api.Services;

/// <summary>Result of a single redirect rate-limit check.</summary>
public sealed record RedirectRateLimitResult
{
    public required bool IsAllowed { get; init; }

    /// <summary>The limit applied (requests per minute).</summary>
    public required int Limit { get; init; }

    /// <summary>Estimated remaining tokens after this request.</summary>
    public required int Remaining { get; init; }

    /// <summary>Unix timestamp (seconds) when the token bucket will be full again.</summary>
    public required long ResetUnixSeconds { get; init; }

    /// <summary>Seconds until one token is available. 0 when IsAllowed is true.</summary>
    public required int RetryAfterSeconds { get; init; }

    public static RedirectRateLimitResult Allow(int limit, int remaining, long resetUnixSeconds) =>
        new() { IsAllowed = true, Limit = limit, Remaining = remaining, ResetUnixSeconds = resetUnixSeconds, RetryAfterSeconds = 0 };

    public static RedirectRateLimitResult Block(int limit, long resetUnixSeconds, int retryAfterSeconds) =>
        new() { IsAllowed = false, Limit = limit, Remaining = 0, ResetUnixSeconds = resetUnixSeconds, RetryAfterSeconds = retryAfterSeconds };
}
