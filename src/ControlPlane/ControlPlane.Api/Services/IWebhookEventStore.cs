namespace ControlPlane.Api.Services;

/// <summary>Tracks which Stripe webhook event IDs have been processed to ensure idempotent delivery.</summary>
public interface IWebhookEventStore
{
    /// <summary>
    /// Marks an event ID as processed. Returns true if newly recorded, or false if already seen.
    /// </summary>
    Task<bool> TryMarkProcessedAsync(string eventId, CancellationToken ct = default);
}
