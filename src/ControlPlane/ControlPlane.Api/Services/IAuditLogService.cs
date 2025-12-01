namespace ControlPlane.Api.Services;

public interface IAuditLogService
{
    Task LogAsync(AuditLogEntry entry, CancellationToken ct = default);

    Task<AuditLogPage> QueryAsync(
        Guid tenantId,
        string? action = null,
        string? resourceType = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default);
}

public sealed record AuditLogEntry
{
    public required Guid Id { get; init; }
    public required Guid TenantId { get; init; }

    /// <summary>Action identifier, e.g. "link.created", "domain.verified", "tenant.suspended".</summary>
    public required string Action { get; init; }

    /// <summary>User ID or API key ID of the actor who triggered this change.</summary>
    public required string ActorId { get; init; }

    /// <summary>"user" or "api_key".</summary>
    public required string ActorType { get; init; }

    /// <summary>Resource type affected: "link", "domain", "tenant", or "api_key".</summary>
    public required string ResourceType { get; init; }

    /// <summary>Identifier of the affected resource.</summary>
    public required string ResourceId { get; init; }

    /// <summary>JSON snapshot of resource state before the change. Null for create operations.</summary>
    public string? OldValue { get; init; }

    /// <summary>JSON snapshot of resource state after the change. Null for hard-delete operations.</summary>
    public string? NewValue { get; init; }

    /// <summary>Optional free-text details (e.g. suspension reason).</summary>
    public string? Details { get; init; }

    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }

    public required DateTime Timestamp { get; init; }
}

public sealed record AuditLogPage
{
    public required IReadOnlyList<AuditLogEntry> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
}
