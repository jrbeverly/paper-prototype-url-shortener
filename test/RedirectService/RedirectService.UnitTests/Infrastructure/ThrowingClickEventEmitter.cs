namespace RedirectService.UnitTests.Infrastructure;

/// <summary>
/// Click event emitter that always throws; verifies emission failures do not
/// propagate to the redirect response.
/// </summary>
internal sealed class ThrowingClickEventEmitter : IClickEventEmitter
{
    public Task EmitAsync(ClickEvent clickEvent, CancellationToken ct = default)
        => throw new InvalidOperationException("Simulated emission failure.");
}
