namespace ControlPlane.Api.Services;

/// <summary>Creates and manages Stripe customer records that correspond to tenants.</summary>
public interface IStripeCustomerService
{
    /// <summary>
    /// Creates a Stripe customer for the given tenant.
    /// Returns the Stripe customer ID on success, or <c>null</c> if the Stripe API is unavailable.
    /// Errors are logged but never propagated — tenant creation must not fail because of Stripe.
    /// </summary>
    Task<string?> CreateCustomerAsync(TenantEntity tenant, CancellationToken cancellationToken = default);
}
