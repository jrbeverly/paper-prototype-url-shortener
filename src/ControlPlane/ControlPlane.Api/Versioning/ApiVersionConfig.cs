using System.Collections.ObjectModel;

namespace ControlPlane.Api.Versioning;

/// <summary>
/// Configuration for API versioning, bound from <c>ApiVersioning</c> config section.
/// </summary>
public sealed record ApiVersionConfig
{
    public const string SectionName = "ApiVersioning";

    public int CurrentMajorVersion { get; init; } = 1;
    public string UrlPathPrefix { get; init; } = "/api/v{version:apiVersion}";

    /// <summary>
    /// Versions that are deprecated but still served.
    /// Key is the major version number.
    /// </summary>
    public ReadOnlyDictionary<int, DeprecatedVersionInfo> DeprecatedVersions { get; init; } = new(new Dictionary<int, DeprecatedVersionInfo>());
}

public sealed record DeprecatedVersionInfo
{
    /// <summary>
    /// RFC 1123 date when this version will be removed.
    /// </summary>
    public required string SunsetDate { get; init; }

    /// <summary>
    /// Human-readable deprecation message returned in the Deprecation header.
    /// </summary>
    public string Message { get; init; } = "This API version is deprecated. Please migrate to the latest version.";
}
