namespace RedirectService.Api.Services;

/// <summary>
/// No-op click event emitter used until a real analytics backend is wired up.
/// </summary>
public sealed class NullClickEventEmitter : IClickEventEmitter
{
    public Task EmitAsync(ClickEvent clickEvent, CancellationToken ct = default) => Task.CompletedTask;
}
