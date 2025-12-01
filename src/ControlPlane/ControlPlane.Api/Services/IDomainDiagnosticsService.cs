namespace ControlPlane.Api.Services;

/// <summary>Status of an individual diagnostic check.</summary>
public enum DiagnosticStatus { Pass, Fail, Warning, Skip }

/// <summary>Result of a single named diagnostic check.</summary>
public sealed record DiagnosticCheckResult
{
    /// <summary>Stable machine-readable identifier (e.g., "dns_cname_record").</summary>
    public required string Name { get; init; }

    /// <summary>Human-readable display label.</summary>
    public required string Label { get; init; }

    /// <summary>Check outcome.</summary>
    public required DiagnosticStatus Status { get; init; }

    /// <summary>Human-readable explanation of the result.</summary>
    public required string Message { get; init; }

    /// <summary>Optional key/value details (e.g., expected vs actual values).</summary>
    public IReadOnlyDictionary<string, string?>? Details { get; init; }
}

/// <summary>A detected configuration problem with remediation guidance.</summary>
public sealed record DiagnosticIssue
{
    /// <summary>Stable machine-readable issue code (e.g., "wrong_cname_target").</summary>
    public required string Code { get; init; }

    /// <summary>Severity: "error" blocks traffic; "warning" degrades it.</summary>
    public required string Severity { get; init; }

    /// <summary>Short human-readable title.</summary>
    public required string Title { get; init; }

    /// <summary>Detailed explanation of what was found and why it matters.</summary>
    public required string Description { get; init; }

    /// <summary>Step-by-step remediation, or null if no action is needed.</summary>
    public string? Remediation { get; init; }
}

/// <summary>Full diagnostic report for a single domain run.</summary>
public sealed record DiagnosticsReport
{
    public required IReadOnlyList<DiagnosticCheckResult> Checks { get; init; }
    public required IReadOnlyList<DiagnosticIssue> Issues { get; init; }
    public required DateTime RunAt { get; init; }
}

public interface IDomainDiagnosticsService
{
    /// <summary>
    /// Runs all diagnostic checks for the given domain and returns a structured report.
    /// Each check is independent; failures do not prevent subsequent checks from running.
    /// </summary>
    Task<DiagnosticsReport> RunAsync(DomainEntity domain, CancellationToken ct = default);
}

// ── HTTP connectivity ────────────────────────────────────────────────────────

public sealed record HttpConnectivityResult
{
    public required bool HttpsReachable { get; init; }
    public required bool SslValid { get; init; }
    public int? ResponseMs { get; init; }
    public int? StatusCode { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Tests HTTPS reachability and TLS certificate validity for a hostname.
/// Abstracted for testability — tests inject a configurable double.
/// </summary>
public interface IHttpConnectivityCheck
{
    Task<HttpConnectivityResult> CheckAsync(string hostname, CancellationToken ct = default);
}

// ── DNS propagation ──────────────────────────────────────────────────────────

public sealed record ResolverResult
{
    public required string ResolverName { get; init; }
    public required string ResolverAddress { get; init; }
    public required bool Resolved { get; init; }
    public string? ResolvedTarget { get; init; }
    public string? Error { get; init; }
}

public sealed record PropagationCheckResult
{
    public required bool AllResolved { get; init; }
    public required int PassedCount { get; init; }
    public required int TotalCount { get; init; }
    public required IReadOnlyList<ResolverResult> Results { get; init; }
}

/// <summary>
/// Queries a hostname's CNAME record from multiple DNS resolvers to assess global propagation.
/// Abstracted for testability — tests inject a configurable double.
/// </summary>
public interface IDnsPropagationCheck
{
    Task<PropagationCheckResult> CheckAsync(
        string hostname,
        string expectedCnameTarget,
        CancellationToken ct = default);
}
