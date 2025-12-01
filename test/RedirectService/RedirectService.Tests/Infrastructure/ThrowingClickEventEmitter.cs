using RedirectService.Api.Services;

namespace RedirectService.Tests.Infrastructure;

/// <summary>
/// Click event emitter that always throws; used to verify that emission failures
/// do not propagate to the redirect response.
/// </summary>
internal sealed class ThrowingClickEventEmitter : IClickEventEmitter
{
    public Task EmitAsync(ClickEvent clickEvent, CancellationToken ct = default)
        => throw new InvalidOperationException("Simulated emission failure.");
}
