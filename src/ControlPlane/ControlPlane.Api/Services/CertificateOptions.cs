namespace ControlPlane.Api.Services;

public sealed record CertificateOptions
{
    public const string SectionName = "CertificateOptions";

    public int MaxPendingCertificates { get; init; } = 10;
    public int StatusCacheTtlSeconds { get; init; } = 60;
    public string Region { get; init; } = "us-east-1";
    public int MaxRequestsPerMinute { get; init; } = 6;
}
