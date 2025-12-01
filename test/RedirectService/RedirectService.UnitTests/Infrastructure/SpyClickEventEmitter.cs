namespace RedirectService.UnitTests.Infrastructure;

/// <summary>
/// Click event emitter that captures every emission for assertion in unit tests.
/// </summary>
internal sealed class SpyClickEventEmitter : IClickEventEmitter
{
    public ClickEvent? LastEvent { get; private set; }
    public int EmitCount { get; private set; }

    public Task EmitAsync(ClickEvent clickEvent, CancellationToken ct = default)
    {
        LastEvent = clickEvent;
        EmitCount++;
        return Task.CompletedTask;
    }
}

