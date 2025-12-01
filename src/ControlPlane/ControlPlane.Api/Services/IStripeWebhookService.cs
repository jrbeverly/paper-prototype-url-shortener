using Stripe;

namespace ControlPlane.Api.Services;

/// <summary>Dispatches verified Stripe events and updates tenant billing state.</summary>
public interface IStripeWebhookService
{
    /// <summary>
    /// Handles a verified Stripe event. Returns false if the event was already processed (idempotent duplicate).
    /// </summary>
    Task<bool> HandleAsync(Event stripeEvent, CancellationToken ct = default);
}
