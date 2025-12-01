using Common.ErrorHandling;
using ControlPlane.Api.Extensions;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Endpoints.Trials;

/// <summary>Trial lifecycle endpoints: status, extension, and admin batch operations.</summary>
public class TrialEndpoints : IEndpointGroup
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        // ── Per-tenant trial endpoints ────────────────────────────────────
        var tenantGroup = routes.MapGroup("/tenants/{tenantId:guid}")
            .WithTags("Trials");

        tenantGroup.MapGet("/trial", GetTrialStatus)
            .WithName("GetTrialStatus")
            .WithOpenApi()
            .WithDescription("Returns the current trial status for a tenant, including days remaining and the plan being trialed.")
            .RequireAuthorization("billing:read")
            .Produces<TrialStatusResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        tenantGroup.MapPatch("/trial", ExtendTrial)
            .WithName("ExtendTrial")
            .WithOpenApi()
            .WithDescription("Extends the active trial by the specified number of days. Admin action only — the tenant must currently be on trial.")
            .WithValidation<ExtendTrialRequest>()
            .RequireAuthorization("billing:write")
            .Produces<TrialStatusResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        // ── Admin batch operations (no tenantId — called by scheduled infrastructure) ─
        var adminGroup = routes.MapGroup("/admin/trials")
            .WithTags("Trials (Admin)");

        adminGroup.MapPost("/expire", ProcessExpiredTrials)
            .WithName("ProcessExpiredTrials")
            .WithOpenApi()
            .WithDescription("Downgrades all tenants whose trial period has elapsed to the Free plan. Intended to be called daily by a scheduled Lambda function.")
            .Produces<ProcessTrialsResponse>(StatusCodes.Status200OK);

        adminGroup.MapPost("/notify", SendTrialNotifications)
            .WithName("SendTrialNotifications")
            .WithOpenApi()
            .WithDescription("Sends reminder notifications to tenants whose trial is expiring at configured thresholds (e.g. 7, 3, 1 days). Intended to be called daily by a scheduled Lambda function.")
            .Produces<ProcessTrialsResponse>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> GetTrialStatus(
        Guid tenantId,
        ITenantRepository repository,
        ITrialService trialService)
    {
        var tenant = await repository.GetByIdAsync(tenantId);
        if (tenant is null)
            throw new NotFoundException($"Tenant {tenantId} not found.");

        return Results.Ok(trialService.GetStatus(tenant));
    }

    private static async Task<IResult> ExtendTrial(
        Guid tenantId,
        ExtendTrialRequest request,
        ITenantRepository repository,
        ITrialService trialService)
    {
        var tenant = await repository.GetByIdAsync(tenantId);
        if (tenant is null)
            throw new NotFoundException($"Tenant {tenantId} not found.");

        if (tenant.Status != "trialing")
            throw new BadRequestException("Tenant is not currently on trial.");

        var updated = await trialService.ExtendAsync(tenant, request.AdditionalDays);
        return Results.Ok(trialService.GetStatus(updated));
    }

    private static async Task<IResult> ProcessExpiredTrials(
        ITenantRepository repository,
        ITrialService trialService,
        ITrialNotificationService notificationService,
        ILogger<TrialEndpoints> logger)
    {
        var now = DateTime.UtcNow;
        // All trialing tenants whose TrialEndsAt has passed.
        var expired = await repository.GetExpiringTrialsAsync(DateTime.MinValue, now);

        var count = 0;
        foreach (var tenant in expired)
        {
            await trialService.ExpireAsync(tenant);
            await notificationService.NotifyExpiredAsync(tenant);
            count++;
        }

        logger.LogInformation("ProcessExpiredTrials: downgraded {Count} tenant(s) to Free plan", count);
        return Results.Ok(new ProcessTrialsResponse { Processed = count });
    }

    private static async Task<IResult> SendTrialNotifications(
        ITenantRepository repository,
        ITrialNotificationService notificationService,
        IOptions<TrialOptions> options,
        ILogger<TrialEndpoints> logger)
    {
        var now = DateTime.UtcNow;
        var count = 0;

        foreach (var days in options.Value.NotificationThresholdDays)
        {
            // Find trials expiring in a window centred on exactly `days` days from now.
            var windowFrom = now.AddDays(days - 1);
            var windowTo = now.AddDays(days);
            var expiring = await repository.GetExpiringTrialsAsync(windowFrom, windowTo);

            foreach (var tenant in expiring)
            {
                await notificationService.NotifyExpiringAsync(tenant, days);
                count++;
            }
        }

        logger.LogInformation("SendTrialNotifications: sent {Count} notification(s)", count);
        return Results.Ok(new ProcessTrialsResponse { Processed = count });
    }
}
