namespace ControlPlane.Api.Services;

/// <summary>Synchronises plan definitions with Stripe Products and Prices.</summary>
public interface IStripePlanService
{
    /// <summary>
    /// Returns a mapping of plan ID → Stripe pricing info.
    /// On first call, performs a lazy sync with Stripe; results are cached for the lifetime of the service.
    /// Returns empty pricing info for plans that failed to sync (Stripe unavailable).
    /// </summary>
    Task<IReadOnlyDictionary<string, StripePlanPricing>> GetPricingAsync(CancellationToken ct = default);

    /// <summary>
    /// Forces a full re-sync with Stripe: creates Products and Prices that do not yet exist.
    /// Safe to call multiple times (idempotent — checks before creating).
    /// </summary>
    Task<IReadOnlyDictionary<string, StripePlanPricing>> SyncAsync(CancellationToken ct = default);
}

/// <summary>Stripe Product and Price identifiers for a single plan.</summary>
public sealed record StripePlanPricing
{
    public required string PlanId { get; init; }
    public string? ProductId { get; init; }
    public string? PriceId { get; init; }
}
