namespace ControlPlane.Api.Models.Responses;

public sealed record TenantStateResponse
{
    /// <summary>The unique identifier of the tenant.</summary>
    public required Guid Id { get; init; }

    /// <summary>Current status: active, trialing, suspended, deleted, etc.</summary>
    public required string Status { get; init; }

    /// <summary>When the tenant was suspended. Non-null when Status is "suspended".</summary>
    public DateTime? SuspendedAt { get; init; }

    /// <summary>Reason provided for suspension.</summary>
    public string? SuspendedReason { get; init; }

    /// <summary>When the tenant was soft-deleted. Non-null when Status is "deleted".</summary>
    public DateTime? DeletedAt { get; init; }

    /// <summary>When data will be permanently purged (DeletedAt + 30-day cooling-off period). Non-null when Status is "deleted".</summary>
    public DateTime? PurgesAt { get; init; }

    /// <summary>When this state change was recorded.</summary>
    public required DateTime UpdatedAt { get; init; }
}
