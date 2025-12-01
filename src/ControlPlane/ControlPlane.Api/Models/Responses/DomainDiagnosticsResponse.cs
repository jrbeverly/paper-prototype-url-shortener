namespace ControlPlane.Api.Models.Responses;

/// <summary>
/// Complete diagnostic report for a custom domain, suitable for sharing with support.
/// Combines structured pass/fail check results with human-readable issue descriptions
/// and remediation guidance.
/// </summary>
public sealed record DomainDiagnosticsResponse
{
    /// <summary>The domain that was diagnosed.</summary>
    public required Guid DomainId { get; init; }

    /// <summary>The hostname that was diagnosed.</summary>
    public required string Hostname { get; init; }

    /// <summary>
    /// Overall diagnosis outcome: "pass" (all checks pass), "fail" (at least one check failed),
    /// or "partial" (no failures but at least one warning).
    /// </summary>
    public required string OverallStatus { get; init; }

    /// <summary>Individual check results in the order they were run.</summary>
    public required IReadOnlyList<DiagnosticCheckResponse> Checks { get; init; }

    /// <summary>
    /// Detected configuration problems. Empty when OverallStatus is "pass".
    /// Each issue includes remediation guidance.
    /// </summary>
    public required IReadOnlyList<DiagnosticIssueResponse> DetectedIssues { get; init; }

    /// <summary>Human-readable summary suitable for support tickets or self-service troubleshooting.</summary>
    public required string Summary { get; init; }

    /// <summary>When the diagnostic run was performed (UTC).</summary>
    public required DateTime RunAt { get; init; }
}

/// <summary>Result of a single named diagnostic check.</summary>
public sealed record DiagnosticCheckResponse
{
    /// <summary>Stable machine-readable identifier (e.g., "dns_cname_record").</summary>
    public required string Name { get; init; }

    /// <summary>Human-readable display label (e.g., "DNS CNAME Record").</summary>
    public required string Label { get; init; }

    /// <summary>Check outcome: "pass", "fail", "warning", or "skip".</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable explanation of the check result.</summary>
    public required string Message { get; init; }

    /// <summary>Optional key/value details (e.g., expected vs actual DNS values, response times).</summary>
    public IReadOnlyDictionary<string, string?>? Details { get; init; }
}

/// <summary>A detected configuration problem with actionable remediation guidance.</summary>
public sealed record DiagnosticIssueResponse
{
    /// <summary>Stable machine-readable issue code (e.g., "wrong_cname_target").</summary>
    public required string Code { get; init; }

    /// <summary>Severity: "error" (blocks traffic) or "warning" (degrades service).</summary>
    public required string Severity { get; init; }

    /// <summary>Short human-readable title.</summary>
    public required string Title { get; init; }

    /// <summary>Detailed explanation of what was found and why it matters.</summary>
    public required string Description { get; init; }

    /// <summary>Step-by-step remediation advice, or null if no action is required.</summary>
    public string? Remediation { get; init; }
}
