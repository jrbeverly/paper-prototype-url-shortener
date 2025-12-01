using System.Security.Claims;
using Common.ErrorHandling;
using ControlPlane.Api.Authentication;
using ControlPlane.Api.Authorization;
using ControlPlane.Api.Extensions;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;

namespace ControlPlane.Api.Endpoints.Tenants;

/// <summary>Tenant workspace creation and management endpoints.</summary>
public class TenantEndpoints : IEndpointGroup
{
    private static readonly TimeSpan _coolingOffPeriod = TimeSpan.FromDays(30);

    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/tenants")
            .WithTags("Tenants");

        group.MapGet("/", ListTenants)
            .WithName("ListTenants")
            .WithOpenApi()
            .WithDescription("List all tenant workspaces. Paginated. Supports optional filtering by status. Admin use only — requires owner or admin role.")
            .RequireAuthorization("admin")
            .Produces<TenantListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/{tenantId:guid}", GetTenant)
            .WithName("GetTenant")
            .WithOpenApi()
            .WithDescription("Get the full details of a tenant workspace including plan limits, settings, and trial status.")
            .RequireAuthorization("tenant:read")
            .Produces<TenantDetailResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPatch("/{tenantId:guid}", UpdateTenant)
            .WithName("UpdateTenant")
            .WithOpenApi()
            .WithDescription("Partially update tenant workspace settings. Only provided fields are changed. Plan and status are not updatable through this endpoint.")
            .RequireAuthorization("tenant:write")
            .WithValidation<UpdateTenantRequest>()
            .Produces<TenantDetailResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/", CreateTenant)
            .WithName("CreateTenant")
            .WithOpenApi()
            .WithDescription("Create a new tenant workspace. New tenants automatically start a 14-day Pro trial with full Pro features. After the trial the tenant is downgraded to Free unless a paid subscription is added.")
            .WithValidation<CreateTenantRequest>()
            .Produces<CreateTenantResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/{tenantId:guid}/suspend", SuspendTenant)
            .WithName("SuspendTenant")
            .WithOpenApi()
            .WithDescription("Suspend a tenant workspace. All links and domains become inactive. Suspended links redirect to a suspension notice page rather than their destinations.")
            .RequireAuthorization("tenant:write")
            .WithValidation<SuspendTenantRequest>()
            .Produces<TenantStateResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/{tenantId:guid}/reactivate", ReactivateTenant)
            .WithName("ReactivateTenant")
            .WithOpenApi()
            .WithDescription("Reactivate a suspended tenant workspace. All links and domains resume normal operation. Requires Owner or Admin role.")
            .RequireAuthorization("tenant:write")
            .Produces<TenantStateResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapDelete("/{tenantId:guid}", DeleteTenant)
            .WithName("DeleteTenant")
            .WithOpenApi()
            .WithDescription("Soft-delete a tenant workspace. Data is retained for a 30-day cooling-off period before permanent purge. All links and domains are immediately deactivated.")
            .RequireAuthorization("tenant:write")
            .Produces<TenantStateResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
    }

    /// <summary>Lists all tenant workspaces with optional status filter and pagination.</summary>
    private static async Task<IResult> ListTenants(
        ITenantRepository repository,
        string? status = null,
        int page = 1,
        int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        var (items, totalCount) = await repository.ListAsync(status, page, pageSize);

        var response = new TenantListResponse
        {
            Items = items.Select(e => new TenantListItemResponse
            {
                Id = e.Id,
                Name = e.Name,
                Email = e.Email,
                Plan = e.Plan,
                Status = e.Status,
                CreatedAt = e.CreatedAt,
                UpdatedAt = e.UpdatedAt
            }).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return Results.Ok(response);
    }

    /// <summary>Gets full details of a tenant workspace including plan limits, settings, and trial status.</summary>
    private static async Task<IResult> GetTenant(
        Guid tenantId,
        ITenantRepository repository,
        ITrialService trialService)
    {
        var entity = await repository.GetByIdAsync(tenantId);
        if (entity is null)
            throw new NotFoundException("Tenant", tenantId.ToString());

        var activePlan = PlanCatalog.Get(entity.Plan);
        var trial = trialService.GetStatus(entity);

        return Results.Ok(MapToDetailResponse(entity, activePlan, trial));
    }

    /// <summary>Partially updates tenant workspace settings. Plan and status are not updatable here.</summary>
    private static async Task<IResult> UpdateTenant(
        Guid tenantId,
        UpdateTenantRequest request,
        ITenantRepository repository,
        ITrialService trialService,
        IAuditLogService auditLog,
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        var entity = await repository.GetByIdAsync(tenantId);
        if (entity is null)
            throw new NotFoundException("Tenant", tenantId.ToString());

        if (entity.DeletedAt is not null)
            throw new ConflictException("Cannot update a deleted tenant.");

        var now = DateTime.UtcNow;
        var updated = entity with
        {
            Name = request.Name?.Trim() ?? entity.Name,
            LogoUrl = request.LogoUrl ?? entity.LogoUrl,
            DefaultRedirectType = request.DefaultRedirectType ?? entity.DefaultRedirectType,
            NotificationsEnabled = request.NotificationsEnabled ?? entity.NotificationsEnabled,
            NotificationEmail = request.NotificationEmail?.Trim().ToLowerInvariant() ?? entity.NotificationEmail,
            UpdatedAt = now
        };

        await repository.UpdateAsync(updated);

        await auditLog.LogAsync(AuditLogEntryFactory.Create(
            tenantId,
            action: "tenant.updated",
            resourceType: "tenant",
            resourceId: tenantId.ToString(),
            actor: user,
            httpContext: httpContext,
            oldValue: AuditLogEntryFactory.Snapshot(new
            {
                name = entity.Name,
                logoUrl = entity.LogoUrl,
                defaultRedirectType = entity.DefaultRedirectType,
                notificationsEnabled = entity.NotificationsEnabled,
                notificationEmail = entity.NotificationEmail
            }),
            newValue: AuditLogEntryFactory.Snapshot(new
            {
                name = updated.Name,
                logoUrl = updated.LogoUrl,
                defaultRedirectType = updated.DefaultRedirectType,
                notificationsEnabled = updated.NotificationsEnabled,
                notificationEmail = updated.NotificationEmail
            })));

        var activePlan = PlanCatalog.Get(updated.Plan);
        var trial = trialService.GetStatus(updated);

        return Results.Ok(MapToDetailResponse(updated, activePlan, trial));
    }

    private static TenantDetailResponse MapToDetailResponse(TenantEntity entity, PlanDefinition plan, TrialStatusResponse trial)
    {
        return new TenantDetailResponse
        {
            Id = entity.Id,
            Name = entity.Name,
            Email = entity.Email,
            Plan = entity.Plan,
            Status = entity.Status,
            Limits = new TenantLimits
            {
                MaxDomains = entity.MaxDomains,
                MaxLinksPerDomain = entity.MaxLinksPerDomain,
                MaxTrackedClicksPerMonth = plan.MaxTrackedClicksPerMonth,
                AnalyticsRetentionDays = plan.AnalyticsRetentionDays
            },
            Settings = new TenantSettings
            {
                LogoUrl = entity.LogoUrl,
                DefaultRedirectType = entity.DefaultRedirectType,
                NotificationsEnabled = entity.NotificationsEnabled,
                NotificationEmail = entity.NotificationEmail
            },
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            Trial = trial.HasUsedTrial ? trial : null
        };
    }

    /// <summary>Creates a new tenant workspace and starts a Pro free trial.</summary>
    private static async Task<IResult> CreateTenant(
        CreateTenantRequest request,
        ITenantRepository repository,
        IStripeCustomerService stripeService,
        ITrialService trialService,
        IAuditLogService auditLog,
        HttpContext httpContext)
    {
        // Build a provisional entity and apply trial fields before persisting.
        var provisional = new TenantEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            Plan = "free",
            Status = "active",
            MaxDomains = PlanCatalog.Get("free").MaxDomains,
            MaxLinksPerDomain = PlanCatalog.Get("free").MaxLinksPerDomain,
            CreatedAt = DateTime.UtcNow
        };

        var withTrial = trialService.StartTrial(provisional);

        // Stripe customer creation is attempted synchronously but failures are swallowed —
        // billing sync failures must not block tenant signup.
        var stripeCustomerId = await stripeService.CreateCustomerAsync(withTrial);

        var entity = withTrial with { StripeCustomerId = stripeCustomerId };
        await repository.CreateAsync(entity);

        await auditLog.LogAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            TenantId = entity.Id,
            Action = "tenant.created",
            ActorId = "system",
            ActorType = "system",
            ResourceType = "tenant",
            ResourceId = entity.Id.ToString(),
            NewValue = AuditLogEntryFactory.Snapshot(new
            {
                name = entity.Name,
                email = entity.Email,
                plan = entity.Plan,
                status = entity.Status
            }),
            IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = httpContext.Request.Headers.UserAgent.ToString(),
            Timestamp = entity.CreatedAt
        });

        var activePlan = PlanCatalog.Get(entity.Plan);
        var trialStatus = trialService.GetStatus(entity);

        var response = new CreateTenantResponse
        {
            Id = entity.Id,
            Name = entity.Name,
            Email = entity.Email,
            Plan = entity.Plan,
            Status = entity.Status,
            Limits = new TenantLimits
            {
                MaxDomains = entity.MaxDomains,
                MaxLinksPerDomain = entity.MaxLinksPerDomain,
                MaxTrackedClicksPerMonth = activePlan.MaxTrackedClicksPerMonth,
                AnalyticsRetentionDays = activePlan.AnalyticsRetentionDays
            },
            CreatedAt = entity.CreatedAt,
            Trial = trialStatus
        };

        return Results.Created($"/tenants/{entity.Id}", response);
    }

    /// <summary>Suspends a tenant, deactivating all its links and domains.</summary>
    private static async Task<IResult> SuspendTenant(
        Guid tenantId,
        SuspendTenantRequest request,
        ITenantRepository repository,
        IAuditLogService auditLog,
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        var entity = await repository.GetByIdAsync(tenantId);
        if (entity is null)
            throw new NotFoundException("Tenant", tenantId.ToString());

        if (entity.DeletedAt is not null)
            throw new ConflictException("Cannot suspend a deleted tenant.");

        if (entity.Status == "suspended")
            throw new ConflictException("Tenant is already suspended.");

        var now = DateTime.UtcNow;
        var updated = entity with
        {
            Status = "suspended",
            SuspendedAt = now,
            SuspendedReason = request.Reason?.Trim(),
            UpdatedAt = now
        };

        await repository.UpdateAsync(updated);

        await auditLog.LogAsync(AuditLogEntryFactory.Create(
            tenantId,
            action: "tenant.suspended",
            resourceType: "tenant",
            resourceId: tenantId.ToString(),
            actor: user,
            httpContext: httpContext,
            oldValue: AuditLogEntryFactory.Snapshot(new { status = entity.Status }),
            newValue: AuditLogEntryFactory.Snapshot(new { status = updated.Status, suspendedAt = now }),
            details: request.Reason?.Trim()));

        return Results.Ok(new TenantStateResponse
        {
            Id = updated.Id,
            Status = updated.Status,
            SuspendedAt = updated.SuspendedAt,
            SuspendedReason = updated.SuspendedReason,
            UpdatedAt = now
        });
    }

    /// <summary>Reactivates a suspended tenant, restoring all its links and domains.</summary>
    private static async Task<IResult> ReactivateTenant(
        Guid tenantId,
        ITenantRepository repository,
        IAuditLogService auditLog,
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        var entity = await repository.GetByIdAsync(tenantId);
        if (entity is null)
            throw new NotFoundException("Tenant", tenantId.ToString());

        if (entity.DeletedAt is not null)
            throw new ConflictException("Cannot reactivate a deleted tenant.");

        if (entity.Status != "suspended")
            throw new ConflictException($"Tenant is not suspended (current status: {entity.Status}).");

        var now = DateTime.UtcNow;
        var updated = entity with
        {
            Status = entity.TrialEndsAt.HasValue && entity.TrialEndsAt.Value > now ? "trialing" : "active",
            SuspendedAt = null,
            SuspendedReason = null,
            UpdatedAt = now
        };

        await repository.UpdateAsync(updated);

        await auditLog.LogAsync(AuditLogEntryFactory.Create(
            tenantId,
            action: "tenant.reactivated",
            resourceType: "tenant",
            resourceId: tenantId.ToString(),
            actor: user,
            httpContext: httpContext,
            oldValue: AuditLogEntryFactory.Snapshot(new { status = entity.Status }),
            newValue: AuditLogEntryFactory.Snapshot(new { status = updated.Status })));

        return Results.Ok(new TenantStateResponse
        {
            Id = updated.Id,
            Status = updated.Status,
            UpdatedAt = now
        });
    }

    /// <summary>Soft-deletes a tenant. Data is retained for a 30-day cooling-off period before permanent purge.</summary>
    private static async Task<IResult> DeleteTenant(
        Guid tenantId,
        ITenantRepository repository,
        IAuditLogService auditLog,
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        var entity = await repository.GetByIdAsync(tenantId);
        if (entity is null)
            throw new NotFoundException("Tenant", tenantId.ToString());

        if (entity.DeletedAt is not null)
            throw new ConflictException("Tenant is already deleted.");

        var now = DateTime.UtcNow;
        var purgesAt = now.Add(_coolingOffPeriod);

        var updated = entity with
        {
            Status = "deleted",
            DeletedAt = now,
            UpdatedAt = now
        };

        await repository.UpdateAsync(updated);

        await auditLog.LogAsync(AuditLogEntryFactory.Create(
            tenantId,
            action: "tenant.deleted",
            resourceType: "tenant",
            resourceId: tenantId.ToString(),
            actor: user,
            httpContext: httpContext,
            oldValue: AuditLogEntryFactory.Snapshot(new { status = entity.Status }),
            newValue: AuditLogEntryFactory.Snapshot(new { status = updated.Status, deletedAt = now }),
            details: $"Cooling-off period ends {purgesAt:O}"));

        return Results.Ok(new TenantStateResponse
        {
            Id = updated.Id,
            Status = updated.Status,
            DeletedAt = updated.DeletedAt,
            PurgesAt = purgesAt,
            UpdatedAt = now
        });
    }
}
