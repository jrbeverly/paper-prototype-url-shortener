namespace RedirectService.Api.Services;

/// <summary>
/// Decorator for <see cref="IClickEventEmitter"/> that adds a circuit breaker.
/// When the circuit is open (analytics pipeline unreachable), emission is silently skipped
/// so redirects are not delayed by failing analytics calls.
/// </summary>
public sealed class ResilientClickEventEmitter : IClickEventEmitter
{
    private readonly IClickEventEmitter _inner;
    private readonly CircuitBreaker _circuitBreaker;

    public CircuitBreaker CircuitBreaker => _circuitBreaker;

    public ResilientClickEventEmitter(IClickEventEmitter inner, CircuitBreaker circuitBreaker)
    {
        _inner = inner;
        _circuitBreaker = circuitBreaker;
    }

    public async Task EmitAsync(ClickEvent clickEvent, CancellationToken ct = default)
    {
        var captured = clickEvent;
        await _circuitBreaker.ExecuteVoidAsync(() => _inner.EmitAsync(captured, ct));
    }
}
