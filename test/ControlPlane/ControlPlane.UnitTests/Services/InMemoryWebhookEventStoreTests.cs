using System.Collections.Concurrent;
using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Services;

/// <summary>
/// Unit tests for InMemoryWebhookEventStore: idempotency and concurrency.
/// </summary>
public sealed class InMemoryWebhookEventStoreTests
{
    private readonly InMemoryWebhookEventStore _store = new();

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TryMarkProcessed_FirstCall_ReturnsTrue()
    {
        var result = await _store.TryMarkProcessedAsync("evt_001");

        result.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TryMarkProcessed_SecondCallWithSameId_ReturnsFalse()
    {
        await _store.TryMarkProcessedAsync("evt_001");

        var result = await _store.TryMarkProcessedAsync("evt_001");

        result.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TryMarkProcessed_DifferentIds_AllReturnTrue()
    {
        var r1 = await _store.TryMarkProcessedAsync("evt_001");
        var r2 = await _store.TryMarkProcessedAsync("evt_002");
        var r3 = await _store.TryMarkProcessedAsync("evt_003");

        r1.Should().BeTrue();
        r2.Should().BeTrue();
        r3.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TryMarkProcessed_MixedNewAndDuplicate_CorrectResults()
    {
        await _store.TryMarkProcessedAsync("evt_001");
        await _store.TryMarkProcessedAsync("evt_002");

        var dup1 = await _store.TryMarkProcessedAsync("evt_001");
        var new1 = await _store.TryMarkProcessedAsync("evt_003");
        var dup2 = await _store.TryMarkProcessedAsync("evt_002");

        dup1.Should().BeFalse();
        new1.Should().BeTrue();
        dup2.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task TryMarkProcessed_Concurrent_SameEventId_OnlyOneSucceeds()
    {
        const string eventId = "evt_concurrent";
        var results = new ConcurrentBag<bool>();

        var tasks = Enumerable.Range(0, 50).Select(_ =>
            Task.Run(async () => results.Add(await _store.TryMarkProcessedAsync(eventId))));

        await Task.WhenAll(tasks);

        results.Count(r => r).Should().Be(1);
        results.Count(r => !r).Should().Be(49);
    }
}
