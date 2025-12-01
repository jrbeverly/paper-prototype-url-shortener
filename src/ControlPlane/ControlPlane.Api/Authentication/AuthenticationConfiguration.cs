namespace ControlPlane.Api.Authentication;

public sealed record JwtOptions
{
    public const string SectionName = "Authentication:Jwt";

    public string? Authority { get; init; }
    public string? IssuerSigningKey { get; init; }
    public string ValidIssuer { get; init; } = "short.io";
    public string ValidAudience { get; init; } = "short-io-api";
    public int TokenLifetimeMinutes { get; init; } = 60;
}

public sealed record ApiKeyOptions
{
    public const string SectionName = "Authentication:ApiKey";

    public string HeaderName { get; init; } = "X-API-Key";
    public bool Enabled { get; init; } = true;
}
