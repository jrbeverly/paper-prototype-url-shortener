using ControlPlane.Api.Extensions;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;

namespace ControlPlane.Api.Endpoints.AuditLogs;

/// <summary>
/// Read-only audit log query endpoints. Audit records are immutable — no update or delete
/// routes are exposed. Retention is enforced at the storage layer via TTL.
/// </summary>
public class AuditLogEndpoints : IEndpointGroup
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/tenants/{tenantId:guid}/audit-logs")
            .WithTags("Audit Logs");

        group.MapGet("/", QueryAuditLogs)
            .WithName("QueryAuditLogs")
            .WithOpenApi()
            .WithDescription(
                "Query the immutable audit log for a tenant. Supports optional filtering by action prefix " +
                "(e.g. \"link.\" matches all link events), resource type, and UTC time range. " +
                "Results are newest-first. Requires owner or admin role.")
            .RequireAuthorization("audit:read")
            .Produces<AuditLogListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> QueryAuditLogs(
        Guid tenantId,
        IAuditLogService auditLog,
        string? action = null,
        string? resourceType = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 200) pageSize = 50;

        var result = await auditLog.QueryAsync(tenantId, action, resourceType, from, to, page, pageSize);

        return Results.Ok(new AuditLogListResponse
        {
            Items = result.Items.Select(MapToResponse).ToList(),
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        });
    }

    private static AuditLogEntryResponse MapToResponse(AuditLogEntry e) => new()
    {
        Id = e.Id,
        Action = e.Action,
        ActorId = e.ActorId,
        ActorType = e.ActorType,
        ResourceType = e.ResourceType,
        ResourceId = e.ResourceId,
        OldValue = e.OldValue,
        NewValue = e.NewValue,
        Details = e.Details,
        IpAddress = e.IpAddress,
        UserAgent = e.UserAgent,
        Timestamp = e.Timestamp
    };
}
