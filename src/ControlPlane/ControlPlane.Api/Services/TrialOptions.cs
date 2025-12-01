namespace ControlPlane.Api.Services;

public sealed class TrialOptions
{
    public const string SectionName = "Trial";

    /// <summary>How many days a new trial lasts.</summary>
    public int DurationDays { get; set; } = 14;

    /// <summary>The plan ID whose features are granted during the trial (must be trial-eligible).</summary>
    public string TrialPlanId { get; set; } = "pro";

    /// <summary>Days-remaining thresholds at which reminder notifications are sent (e.g. [7, 3, 1]).</summary>
    public int[] NotificationThresholdDays { get; set; } = [7, 3, 1];
}
