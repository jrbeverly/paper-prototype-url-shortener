using RedirectService.Api.Services;

namespace RedirectService.Tests.Infrastructure;

/// <summary>
/// Capturing click event emitter for unit tests.
/// Records the last emitted event and a count of emissions.
/// </summary>
internal sealed class SpyClickEventEmitter : IClickEventEmitter
{
    /// <summary>The most recently emitted event, or <see langword="null"/> if none.</summary>
    public ClickEvent? LastEvent { get; private set; }

    /// <summary>Total number of times <see cref="EmitAsync"/> was called.</summary>
    public int EmitCount { get; private set; }

    public Task EmitAsync(ClickEvent clickEvent, CancellationToken ct = default)
    {
        LastEvent = clickEvent;
        EmitCount++;
        return Task.CompletedTask;
    }
}
