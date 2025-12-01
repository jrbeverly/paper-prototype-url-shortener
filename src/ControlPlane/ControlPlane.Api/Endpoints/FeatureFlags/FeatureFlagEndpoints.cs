using System.Security.Claims;
using Common.ErrorHandling;
using ControlPlane.Api.Authorization;
using ControlPlane.Api.Extensions;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;

namespace ControlPlane.Api.Endpoints.FeatureFlags;

/// <summary>
/// Admin CRUD for feature flags (global, not tenant-scoped) and per-tenant flag evaluation.
/// Admin endpoints at /feature-flags require flag:write / flag:read but no tenantId route param.
/// Tenant evaluation at /tenants/{tenantId}/feature-flags is scoped to a specific workspace.
/// </summary>
public class FeatureFlagEndpoints : IEndpointGroup
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        // ── Admin CRUD — no tenantId in route ────────────────────────────────────
        var admin = routes.MapGroup("/feature-flags")
            .WithTags("FeatureFlags");

        admin.MapGet("/", ListFlags)
            .WithName("ListFeatureFlags")
            .WithOpenApi()
            .WithDescription("List all feature flags with their current configuration and audit trail. Admin-only.")
            .RequireAuthorization(Permissions.FlagRead)
            .Produces<FeatureFlagListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        admin.MapPost("/", CreateFlag)
            .WithName("CreateFeatureFlag")
            .WithOpenApi()
            .WithDescription("Create a new feature flag. Key must follow the feature.area.name dot-notation convention. Duplicate keys are rejected.")
            .RequireAuthorization(Permissions.FlagWrite)
            .WithValidation<CreateFeatureFlagRequest>()
            .Produces<FeatureFlagResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);

        admin.MapGet("/{key}", GetFlag)
            .WithName("GetFeatureFlag")
            .WithOpenApi()
            .WithDescription("Get a single feature flag by key, including its full audit trail.")
            .RequireAuthorization(Permissions.FlagRead)
            .Produces<FeatureFlagResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        admin.MapPatch("/{key}", UpdateFlag)
            .WithName("UpdateFeatureFlag")
            .WithOpenApi()
            .WithDescription("Partially update a feature flag. Set Enabled=false for an emergency kill switch — the cache is invalidated immediately.")
            .RequireAuthorization(Permissions.FlagWrite)
            .WithValidation<UpdateFeatureFlagRequest>()
            .Produces<FeatureFlagResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        admin.MapDelete("/{key}", DeleteFlag)
            .WithName("DeleteFeatureFlag")
            .WithOpenApi()
            .WithDescription("Permanently delete a feature flag. Use when a flag has been fully rolled out or is no longer needed.")
            .RequireAuthorization(Permissions.FlagWrite)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        // ── Tenant evaluation — tenantId in route ────────────────────────────────
        var tenant = routes.MapGroup("/tenants/{tenantId:guid}/feature-flags")
            .WithTags("FeatureFlags");

        tenant.MapGet("/", EvaluateAll)
            .WithName("EvaluateTenantFeatureFlags")
            .WithOpenApi()
            .WithDescription("Evaluate all feature flags for the authenticated tenant. Returns the enabled/disabled state and evaluation reason for each flag.")
            .RequireAuthorization(Permissions.FlagRead)
            .Produces<FeatureFlagEvaluationsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        tenant.MapGet("/{key}", EvaluateOne)
            .WithName("EvaluateTenantFeatureFlag")
            .WithOpenApi()
            .WithDescription("Evaluate a single feature flag for the authenticated tenant.")
            .RequireAuthorization(Permissions.FlagRead)
            .Produces<FeatureFlagEvaluationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);
    }

    // ── Admin handlers ────────────────────────────────────────────────────────

    private static async Task<IResult> ListFlags(IFeatureFlagRepository repository, CancellationToken ct)
    {
        var flags = await repository.ListAsync(ct);
        return Results.Ok(new FeatureFlagListResponse
        {
            Flags = flags.Select(ToResponse).ToList(),
            TotalCount = flags.Count
        });
    }

    private static async Task<IResult> CreateFlag(
        CreateFeatureFlagRequest request,
        IFeatureFlagRepository repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var existing = await repository.GetByKeyAsync(request.Key, ct);
        if (existing is not null)
            throw new ConflictException($"A feature flag with key '{request.Key}' already exists.");

        var actor = ActorId(user);
        var now = DateTime.UtcNow;

        var entity = new FeatureFlagEntity
        {
            Key = request.Key,
            Name = request.Name,
            Description = request.Description,
            FlagType = request.FlagType,
            Enabled = request.Enabled,
            RolloutPercentage = request.RolloutPercentage,
            EnabledTenantIds = request.EnabledTenantIds?.AsReadOnly(),
            EnabledPlanIds = request.EnabledPlanIds?.AsReadOnly(),
            CreatedBy = actor,
            CreatedAt = now,
            AuditTrail =
            [
                new FlagAuditEntry
                {
                    Action = "created",
                    PerformedBy = actor,
                    Details = $"Flag created with type '{request.FlagType}', enabled={request.Enabled}.",
                    Timestamp = now
                }
            ]
        };

        var created = await repository.CreateAsync(entity, ct);
        return Results.Created($"/feature-flags/{created.Key}", ToResponse(created));
    }

    private static async Task<IResult> GetFlag(
        string key,
        IFeatureFlagRepository repository,
        CancellationToken ct)
    {
        var flag = await repository.GetByKeyAsync(key, ct);
        if (flag is null)
            throw new NotFoundException("FeatureFlag", key);

        return Results.Ok(ToResponse(flag));
    }

    private static async Task<IResult> UpdateFlag(
        string key,
        UpdateFeatureFlagRequest request,
        IFeatureFlagRepository repository,
        IFeatureFlagService service,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var actor = ActorId(user);
        var now = DateTime.UtcNow;

        var action = request.Enabled switch
        {
            false => "disabled",
            true => "enabled",
            null => "updated"
        };

        var details = BuildUpdateDetails(request);

        var auditEntry = new FlagAuditEntry
        {
            Action = action,
            PerformedBy = actor,
            Details = details,
            Timestamp = now
        };

        var update = new FeatureFlagUpdate
        {
            Name = request.Name,
            Description = request.Description,
            Enabled = request.Enabled,
            FlagType = request.FlagType,
            RolloutPercentage = request.RolloutPercentage,
            EnabledTenantIds = request.EnabledTenantIds?.AsReadOnly(),
            EnabledPlanIds = request.EnabledPlanIds?.AsReadOnly()
        };

        var updated = await repository.UpdateAsync(key, update, auditEntry, ct);
        if (updated is null)
            throw new NotFoundException("FeatureFlag", key);

        // Invalidate the cache immediately so kill switches and other changes take effect
        // without waiting for the TTL to expire.
        service.InvalidateCache(key);

        return Results.Ok(ToResponse(updated));
    }

    private static async Task<IResult> DeleteFlag(
        string key,
        IFeatureFlagRepository repository,
        IFeatureFlagService service,
        CancellationToken ct)
    {
        var removed = await repository.DeleteAsync(key, ct);
        if (!removed)
            throw new NotFoundException("FeatureFlag", key);

        service.InvalidateCache(key);
        return Results.NoContent();
    }

    // ── Tenant evaluation handlers ────────────────────────────────────────────

    private static async Task<IResult> EvaluateAll(
        Guid tenantId,
        IFeatureFlagService service,
        ITenantRepository tenantRepository,
        CancellationToken ct)
    {
        var tenant = await tenantRepository.GetByIdAsync(tenantId);
        if (tenant is null)
            throw new NotFoundException("Tenant", tenantId.ToString());

        var evaluations = await service.EvaluateAllAsync(tenantId, tenant.Plan, ct);

        return Results.Ok(new FeatureFlagEvaluationsResponse
        {
            Flags = evaluations.Select(e => new FeatureFlagEvaluationResponse
            {
                Key = e.Key,
                Enabled = e.Enabled,
                Reason = e.Reason
            }).ToList(),
            TotalCount = evaluations.Count
        });
    }

    private static async Task<IResult> EvaluateOne(
        Guid tenantId,
        string key,
        IFeatureFlagService service,
        ITenantRepository tenantRepository,
        CancellationToken ct)
    {
        var tenant = await tenantRepository.GetByIdAsync(tenantId);
        if (tenant is null)
            throw new NotFoundException("Tenant", tenantId.ToString());

        var enabled = await service.IsEnabledAsync(key, tenantId, tenant.Plan, ct);

        return Results.Ok(new FeatureFlagEvaluationResponse
        {
            Key = key,
            Enabled = enabled,
            Reason = enabled ? "enabled" : "disabled"
        });
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private static FeatureFlagResponse ToResponse(FeatureFlagEntity f) => new()
    {
        Key = f.Key,
        Name = f.Name,
        Description = f.Description,
        FlagType = f.FlagType,
        Enabled = f.Enabled,
        RolloutPercentage = f.RolloutPercentage,
        EnabledTenantIds = f.EnabledTenantIds,
        EnabledPlanIds = f.EnabledPlanIds,
        CreatedBy = f.CreatedBy,
        UpdatedBy = f.UpdatedBy,
        CreatedAt = f.CreatedAt,
        UpdatedAt = f.UpdatedAt,
        AuditTrail = f.AuditTrail.Select(a => new FlagAuditEntryResponse
        {
            Action = a.Action,
            PerformedBy = a.PerformedBy,
            Details = a.Details,
            Timestamp = a.Timestamp
        }).ToList()
    };

    private static string ActorId(ClaimsPrincipal user) =>
        user.FindFirst("sub")?.Value
        ?? user.FindFirst("key_id")?.Value
        ?? "unknown";

    private static string? BuildUpdateDetails(UpdateFeatureFlagRequest req)
    {
        var parts = new List<string>();
        if (req.Enabled.HasValue)
            parts.Add($"enabled={req.Enabled.Value}");
        if (req.FlagType is not null)
            parts.Add($"flagType={req.FlagType}");
        if (req.RolloutPercentage.HasValue)
            parts.Add($"rolloutPercentage={req.RolloutPercentage}");
        if (req.Name is not null)
            parts.Add("name updated");
        if (req.Description is not null)
            parts.Add("description updated");
        if (req.EnabledTenantIds is not null)
            parts.Add($"enabledTenantIds updated ({req.EnabledTenantIds.Count} entries)");
        if (req.EnabledPlanIds is not null)
            parts.Add($"enabledPlanIds updated ({req.EnabledPlanIds.Count} entries)");
        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }
}
