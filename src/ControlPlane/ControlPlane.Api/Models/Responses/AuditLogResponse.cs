namespace ControlPlane.Api.Models.Responses;

public sealed record AuditLogEntryResponse
{
    public required Guid Id { get; init; }
    public required string Action { get; init; }
    public required string ActorId { get; init; }
    public required string ActorType { get; init; }
    public required string ResourceType { get; init; }
    public required string ResourceId { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public string? Details { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public required DateTime Timestamp { get; init; }
}

public sealed record AuditLogListResponse
{
    public required List<AuditLogEntryResponse> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
}
