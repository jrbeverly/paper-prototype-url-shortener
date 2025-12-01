namespace ControlPlane.Api.Services;

public sealed record DomainOptions
{
    public const string SectionName = "DomainOptions";

    public int MaxDomains { get; init; } = 5;
    public string CnameTarget { get; init; } = "domains.short.io";
}
