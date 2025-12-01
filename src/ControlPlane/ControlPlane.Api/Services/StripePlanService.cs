using Microsoft.Extensions.Options;
using Stripe;

namespace ControlPlane.Api.Services;

/// <summary>
/// Ensures each paid plan has a corresponding Stripe Product and monthly recurring Price.
/// Syncs lazily on first access; results are held in memory for the service lifetime.
/// </summary>
public class StripePlanService : IStripePlanService
{
    // Metadata key used to correlate Stripe Products back to plan IDs.
    private const string _planIdMetadataKey = "plan_id";

    private readonly StripeClient _client;
    private readonly ILogger<StripePlanService> _logger;

    // Lazy sync: written once and never cleared (plans are static).
    private IReadOnlyDictionary<string, StripePlanPricing>? _cache;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public StripePlanService(IOptions<StripeOptions> options, ILogger<StripePlanService> logger)
    {
        _client = new StripeClient(options.Value.SecretKey);
        _logger = logger;
    }

    internal StripePlanService(StripeClient client, ILogger<StripePlanService> logger)
    {
        _client = client;
        _logger = logger;
    }

    internal virtual ProductService CreateProductService() => new(_client);
    internal virtual PriceService CreatePriceService() => new(_client);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, StripePlanPricing>> GetPricingAsync(CancellationToken ct = default)
    {
        if (_cache is not null) return _cache;
        return await SyncAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, StripePlanPricing>> SyncAsync(CancellationToken ct = default)
    {
        await _syncLock.WaitAsync(ct);
        try
        {
            var result = new Dictionary<string, StripePlanPricing>(StringComparer.OrdinalIgnoreCase);

            foreach (var plan in PlanCatalog.All)
            {
                // Free and custom-priced (Enterprise) plans have no Stripe Price.
                if (plan.MonthlyPriceCents <= 0 || plan.IsCustomPricing)
                {
                    result[plan.Id] = new StripePlanPricing { PlanId = plan.Id };
                    continue;
                }

                try
                {
                    var productId = await EnsureProductAsync(plan, ct);
                    var priceId = await EnsurePriceAsync(plan, productId, ct);
                    result[plan.Id] = new StripePlanPricing
                    {
                        PlanId = plan.Id,
                        ProductId = productId,
                        PriceId = priceId
                    };
                }
                catch (StripeException ex)
                {
                    _logger.LogError(ex,
                        "Stripe plan sync failed for plan {PlanId}: {StripeError}",
                        plan.Id, ex.StripeError?.Message ?? ex.Message);
                    result[plan.Id] = new StripePlanPricing { PlanId = plan.Id };
                }
            }

            _cache = result;
            _logger.LogInformation("Stripe plan sync complete. Synced {Count} plans.", result.Count);
            return result;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<string> EnsureProductAsync(PlanDefinition plan, CancellationToken ct)
    {
        // Search for an existing Product tagged with this plan ID.
        var listOptions = new ProductListOptions
        {
            Active = true,
            Limit = 10
        };
        var productService = CreateProductService();
        var existing = await productService.ListAsync(listOptions, cancellationToken: ct);
        var match = existing.Data.FirstOrDefault(p =>
            p.Metadata.TryGetValue(_planIdMetadataKey, out var id) &&
            string.Equals(id, plan.Id, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
            return match.Id;

        var createOptions = new ProductCreateOptions
        {
            Name = $"Short.io {plan.Name}",
            Metadata = new Dictionary<string, string> { [_planIdMetadataKey] = plan.Id }
        };
        var product = await productService.CreateAsync(createOptions, cancellationToken: ct);
        _logger.LogInformation("Created Stripe Product {ProductId} for plan {PlanId}", product.Id, plan.Id);
        return product.Id;
    }

    private async Task<string?> EnsurePriceAsync(PlanDefinition plan, string productId, CancellationToken ct)
    {
        // Look for an active monthly recurring Price for this product.
        var priceService = CreatePriceService();
        var listOptions = new PriceListOptions
        {
            Active = true,
            Product = productId,
            Limit = 10
        };
        var existing = await priceService.ListAsync(listOptions, cancellationToken: ct);
        var match = existing.Data.FirstOrDefault(p =>
            p.Recurring?.Interval == "month");

        if (match is not null)
            return match.Id;

        var createOptions = new PriceCreateOptions
        {
            Product = productId,
            UnitAmount = plan.MonthlyPriceCents,
            Currency = "usd",
            Recurring = new PriceRecurringOptions { Interval = "month" },
            Metadata = new Dictionary<string, string> { [_planIdMetadataKey] = plan.Id }
        };
        var price = await priceService.CreateAsync(createOptions, cancellationToken: ct);
        _logger.LogInformation(
            "Created Stripe Price {PriceId} ({Amount} USD/mo) for plan {PlanId}",
            price.Id, plan.MonthlyPriceCents, plan.Id);
        return price.Id;
    }
}
