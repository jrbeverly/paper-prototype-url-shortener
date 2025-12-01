using ControlPlane.Api.Extensions;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;

namespace ControlPlane.Api.Endpoints.Usage;

/// <summary>Per-tenant usage metering and plan consumption endpoints.</summary>
public class UsageEndpoints : IEndpointGroup
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/tenants/{tenantId:guid}/usage")
            .WithTags("Usage");

        group.MapGet("/", GetCurrentUsage)
            .WithName("GetCurrentUsage")
            .WithOpenApi()
            .WithDescription("Get the current billing period usage for a tenant: tracked clicks, active domains, active links, plan limits, overage, and current alert level.")
            .RequireAuthorization("tenant:read")
            .Produces<UsageResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
    }

    /// <summary>Returns current billing period usage vs plan limits for the tenant.</summary>
    private static async Task<IResult> GetCurrentUsage(
        Guid tenantId,
        IUsageService usageService)
    {
        var summary = await usageService.GetUsageAsync(tenantId);
        return Results.Ok(MapToResponse(summary));
    }

    private static UsageResponse MapToResponse(TenantUsageSummary summary) =>
        new()
        {
            TenantId = summary.TenantId,
            Plan = summary.Plan,
            Period = new UsagePeriodResponse
            {
                Start = summary.PeriodStart,
                End = summary.PeriodEnd
            },
            Current = new UsageMetricsResponse
            {
                TrackedClicks = summary.TrackedClicks,
                ActiveDomains = summary.ActiveDomains,
                ActiveLinks = summary.ActiveLinks
            },
            Limits = new UsageLimitsResponse
            {
                MaxTrackedClicksPerMonth = summary.MaxTrackedClicksPerMonth == int.MaxValue
                    ? null
                    : summary.MaxTrackedClicksPerMonth,
                MaxDomains = summary.MaxDomains,
                MaxLinksPerDomain = summary.MaxLinksPerDomain
            },
            Overage = new UsageOverageResponse
            {
                Clicks = summary.ClicksOverage
            },
            AlertLevel = FormatAlertLevel(summary.AlertLevel)
        };

    private static string FormatAlertLevel(UsageAlertLevel level) => level switch
    {
        UsageAlertLevel.Warning80 => "warning80",
        UsageAlertLevel.Warning90 => "warning90",
        UsageAlertLevel.LimitReached => "limitReached",
        _ => "none"
    };
}
