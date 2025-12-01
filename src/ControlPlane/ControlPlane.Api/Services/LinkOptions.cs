namespace ControlPlane.Api.Services;

public sealed record LinkOptions
{
    public const string SectionName = "LinkOptions";

    public int MaxLinks { get; init; } = 50;
    public string DefaultRedirectType { get; init; } = "302";
    public string BaseUrl { get; init; } = "https://short.io";
}
