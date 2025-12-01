using ControlPlane.Api.Services;

namespace ControlPlane.Tests.Infrastructure;

/// <summary>
/// In-process fake for <see cref="IStripePlanService"/>.
/// Returns deterministic fake Stripe IDs without calling the Stripe API.
/// </summary>
public sealed class TestStripePlanService : IStripePlanService
{
    private readonly IReadOnlyDictionary<string, StripePlanPricing> _pricing;

    public TestStripePlanService()
    {
        _pricing = PlanCatalog.All
            .Where(p => !p.IsCustomPricing && p.MonthlyPriceCents > 0)
            .ToDictionary(
                p => p.Id,
                p => new StripePlanPricing
                {
                    PlanId = p.Id,
                    ProductId = $"prod_test_{p.Id}",
                    PriceId = $"price_test_{p.Id}"
                },
                StringComparer.OrdinalIgnoreCase);
    }

    public Task<IReadOnlyDictionary<string, StripePlanPricing>> GetPricingAsync(CancellationToken ct = default) =>
        Task.FromResult(_pricing);

    public Task<IReadOnlyDictionary<string, StripePlanPricing>> SyncAsync(CancellationToken ct = default) =>
        Task.FromResult(_pricing);
}
