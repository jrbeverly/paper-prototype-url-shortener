using System.Security.Claims;
using Common.ErrorHandling;
using ControlPlane.Api.Extensions;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;

namespace ControlPlane.Api.Endpoints.Billing;

public class BillingEndpoints : IEndpointGroup
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/tenants/{tenantId:guid}/billing")
            .WithTags("Billing");

        group.MapPost("/upgrade", UpgradePlan)
            .WithName("UpgradePlan")
            .WithOpenApi()
            .WithDescription("Upgrade to a higher plan immediately with prorated billing.")
            .RequireAuthorization("billing:write")
            .WithValidation<UpgradePlanRequest>()
            .Produces<PlanChangeResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/downgrade", DowngradePlan)
            .WithName("DowngradePlan")
            .WithOpenApi()
            .WithDescription("Schedule a downgrade to a lower plan at the end of the current billing period. New plan limits are enforced immediately.")
            .RequireAuthorization("billing:write")
            .WithValidation<DowngradePlanRequest>()
            .Produces<PlanChangeResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/cancel", CancelSubscription)
            .WithName("CancelSubscription")
            .WithOpenApi()
            .WithDescription("Cancel the subscription at the end of the current billing period. Access is retained until the period ends, then the tenant reverts to Free. No data is lost.")
            .RequireAuthorization("billing:write")
            .Produces<CancelSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> UpgradePlan(
        Guid tenantId,
        UpgradePlanRequest request,
        ITenantRepository repository,
        IBillingService billingService,
        CancellationToken ct)
    {
        var tenant = await repository.GetByIdAsync(tenantId);
        if (tenant is null)
            throw new NotFoundException("Tenant", tenantId.ToString());

        if (tenant.DeletedAt is not null)
            throw new ConflictException("Cannot change plan for a deleted tenant.");

        var result = await billingService.UpgradePlanAsync(tenant, request.Plan.Trim(), ct);

        return Results.Ok(MapToResponse(result));
    }

    private static async Task<IResult> DowngradePlan(
        Guid tenantId,
        DowngradePlanRequest request,
        ITenantRepository repository,
        IBillingService billingService,
        CancellationToken ct)
    {
        var tenant = await repository.GetByIdAsync(tenantId);
        if (tenant is null)
            throw new NotFoundException("Tenant", tenantId.ToString());

        if (tenant.DeletedAt is not null)
            throw new ConflictException("Cannot change plan for a deleted tenant.");

        var result = await billingService.DowngradePlanAsync(tenant, request.Plan.Trim(), ct);

        return Results.Ok(MapToResponse(result));
    }

    private static async Task<IResult> CancelSubscription(
        Guid tenantId,
        ITenantRepository repository,
        IBillingService billingService,
        CancellationToken ct)
    {
        var tenant = await repository.GetByIdAsync(tenantId);
        if (tenant is null)
            throw new NotFoundException("Tenant", tenantId.ToString());

        if (tenant.DeletedAt is not null)
            throw new ConflictException("Cannot cancel subscription for a deleted tenant.");

        var result = await billingService.CancelSubscriptionAsync(tenant, ct);

        return Results.Ok(new CancelSubscriptionResponse
        {
            TenantId = result.Tenant.Id,
            Status = result.Tenant.Status,
            PeriodEnd = result.PeriodEnd
        });
    }

    private static PlanChangeResponse MapToResponse(PlanChangeResult result) => new()
    {
        TenantId = result.Tenant.Id,
        CurrentPlan = result.Tenant.Plan,
        Status = result.Tenant.Status,
        ScheduledPlan = result.Tenant.ScheduledPlan,
        ScheduledChangeAt = result.Tenant.ScheduledPlanChangeAt,
        Warnings = result.Warnings.Select(w => new PlanChangeWarning
        {
            Code = w.Code,
            Message = w.Message
        }).ToList()
    };
}
