using ControlPlane.Api.Extensions;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;

namespace ControlPlane.Api.Endpoints.Plans;

/// <summary>Public plan catalog endpoint — no authentication required.</summary>
public class PlanEndpoints : IEndpointGroup
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/plans")
            .WithTags("Plans");

        group.MapGet("/", ListPlans)
            .WithName("ListPlans")
            .WithOpenApi()
            .WithDescription("Returns all available subscription plans with limits, features, and pricing. No authentication required — intended for public pricing pages and plan selection flows.")
            .Produces<PlanListResponse>(StatusCodes.Status200OK);
    }

    /// <summary>Returns the full plan catalog, enriched with Stripe Price IDs when available.</summary>
    /// <param name="planService">The Stripe plan sync service.</param>
    private static async Task<IResult> ListPlans(IStripePlanService planService)
    {
        // Stripe pricing is fetched lazily; failures return null IDs without blocking the response.
        var pricing = await planService.GetPricingAsync();

        var plans = PlanCatalog.All.Select(plan =>
        {
            pricing.TryGetValue(plan.Id, out var stripePricing);
            return new PlanResponse
            {
                Id = plan.Id,
                Name = plan.Name,
                MonthlyPriceCents = plan.IsCustomPricing ? null : plan.MonthlyPriceCents,
                IsCustomPricing = plan.IsCustomPricing,
                Limits = new PlanLimitsResponse
                {
                    MaxDomains = plan.MaxDomains,
                    MaxLinksPerDomain = plan.MaxLinksPerDomain,
                    MaxTrackedClicksPerMonth = plan.MaxTrackedClicksPerMonth,
                    AnalyticsRetentionDays = plan.AnalyticsRetentionDays
                },
                Features = plan.Features.Order().ToList(),
                TrialAvailable = plan.TrialAvailable,
                StripePriceId = stripePricing?.PriceId
            };
        }).ToList();

        return Results.Ok(new PlanListResponse { Plans = plans });
    }
}
