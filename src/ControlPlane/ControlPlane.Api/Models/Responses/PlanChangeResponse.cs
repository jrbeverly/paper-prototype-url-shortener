namespace ControlPlane.Api.Models.Responses;

public sealed record PlanChangeResponse
{
    public required Guid TenantId { get; init; }
    public required string CurrentPlan { get; init; }
    public required string Status { get; init; }

    /// <summary>The plan that will take effect at <see cref="ScheduledChangeAt"/>. Null if the change was immediate.</summary>
    public string? ScheduledPlan { get; init; }

    /// <summary>When the scheduled plan change takes effect. Null if the change was immediate.</summary>
    public DateTime? ScheduledChangeAt { get; init; }

    /// <summary>Usage warnings triggered by the plan change. Empty if no warnings.</summary>
    public required IReadOnlyList<PlanChangeWarning> Warnings { get; init; }
}

public sealed record PlanChangeWarning
{
    /// <summary>Machine-readable code: "domain_limit_exceeded", "link_limit_exceeded", "click_limit_exceeded".</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable description of the warning.</summary>
    public required string Message { get; init; }
}

public sealed record CancelSubscriptionResponse
{
    public required Guid TenantId { get; init; }
    public required string Status { get; init; }

    /// <summary>When the current subscription period ends and the tenant reverts to Free.</summary>
    public required DateTime PeriodEnd { get; init; }

    /// <summary>Plan the tenant will be on after the subscription ends.</summary>
    public const string PostCancelPlan = "free";
}
