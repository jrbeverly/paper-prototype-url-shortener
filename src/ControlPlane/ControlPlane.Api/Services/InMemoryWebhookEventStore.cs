using System.Collections.Concurrent;

namespace ControlPlane.Api.Services;

public sealed class InMemoryWebhookEventStore : IWebhookEventStore
{
    private readonly ConcurrentDictionary<string, byte> _processed = new();

    public Task<bool> TryMarkProcessedAsync(string eventId, CancellationToken ct = default)
        => Task.FromResult(_processed.TryAdd(eventId, 0));
}
