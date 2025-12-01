namespace ControlPlane.Api.Services;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Hard per-IP limit: maximum link creation requests per minute, regardless of tenant plan.</summary>
    public int IpLimitPerMinute { get; init; } = 60;

    /// <summary>IP addresses exempt from rate limiting (e.g. internal monitoring, load balancers).</summary>
    public IReadOnlyList<string> ExemptIps { get; init; } = [];
}
