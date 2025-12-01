namespace ControlPlane.Api.Services;

/// <summary>
/// Orchestrates the domain activation pipeline after DNS verification succeeds.
/// Transitions the domain through certificate provisioning and distribution setup,
/// arriving at <c>active</c> when all prerequisites are met, or a clear failure state
/// with a recovery path when a step fails.
/// </summary>
public interface IDomainActivationService
{
    /// <summary>
    /// Runs the full activation sequence for a DNS-verified domain:
    /// <c>certificate_provisioning</c> → ACM certificate request → CloudFront distribution
    /// tenant creation → <c>active</c>. Sends the customer notification on success.
    /// On certificate failure the domain is transitioned to <c>certificate_failed</c>
    /// and the result carries the reason. Distribution failure is non-fatal: the domain
    /// still becomes <c>active</c> but without a distribution tenant.
    /// </summary>
    /// <param name="domain">Domain entity that has just passed DNS verification.</param>
    /// <param name="actorId">System actor identifier for audit log entries (e.g., "dns-polling", "manual-verify").</param>
    /// <param name="ct">Cancellation token.</param>
    Task<DomainActivationResult> ActivateAsync(
        DomainEntity domain,
        string actorId,
        CancellationToken ct = default);
}

public sealed record DomainActivationResult
{
    public required bool Succeeded { get; init; }

    /// <summary>The domain entity in its final state after the activation attempt.</summary>
    public required DomainEntity Domain { get; init; }

    /// <summary>Human-readable reason when <see cref="Succeeded"/> is <c>false</c>.</summary>
    public string? FailureReason { get; init; }

    public static DomainActivationResult Success(DomainEntity domain) =>
        new() { Succeeded = true, Domain = domain };

    public static DomainActivationResult Failure(DomainEntity domain, string reason) =>
        new() { Succeeded = false, Domain = domain, FailureReason = reason };
}
