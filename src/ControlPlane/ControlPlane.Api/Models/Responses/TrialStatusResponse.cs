namespace ControlPlane.Api.Models.Responses;

public sealed record TrialStatusResponse
{
    /// <summary>True when the tenant is currently in the trial period (Status is "trialing" and trial has not yet expired).</summary>
    public required bool IsOnTrial { get; init; }

    /// <summary>True once a trial has ever been started for this tenant, even if it has since expired or converted.</summary>
    public required bool HasUsedTrial { get; init; }

    /// <summary>UTC timestamp when the trial period ends (or ended). Null for tenants provisioned before trials were introduced.</summary>
    public DateTime? TrialEndsAt { get; init; }

    /// <summary>Whole days remaining in the trial. Null when not on trial.</summary>
    public int? DaysRemaining { get; init; }

    /// <summary>The plan ID whose features are available during the trial (e.g. "pro"). Null when not on trial.</summary>
    public string? TrialPlan { get; init; }
}
