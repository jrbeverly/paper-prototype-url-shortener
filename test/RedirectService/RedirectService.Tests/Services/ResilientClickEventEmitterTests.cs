namespace RedirectService.Tests.Services;

public sealed class ResilientClickEventEmitterTests
{
    private static ClickEvent BuildEvent()
        => new()
        {
            EventId = Guid.NewGuid().ToString(),
            SchemaVersion = "1",
            Timestamp = DateTimeOffset.UtcNow,
            TenantId = "tenant-1",
            DomainId = "domain-1",
            Domain = "go.example.com",
            Slug = "test",
            DestinationUrl = "https://example.com",
            StatusCode = 302
        };

    // ── Passthrough (healthy) ────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EmitAsync_Healthy_DelegatesToInner()
    {
        var spy = new SpyClickEventEmitter();
        var cb = new CircuitBreaker("Analytics");
        var emitter = new ResilientClickEventEmitter(spy, cb);
        var evt = BuildEvent();

        await emitter.EmitAsync(evt);

        spy.EmitCount.Should().Be(1);
        spy.LastEvent.Should().BeSameAs(evt);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EmitAsync_Healthy_KeepsCircuitClosed()
    {
        var spy = new SpyClickEventEmitter();
        var cb = new CircuitBreaker("Analytics");
        var emitter = new ResilientClickEventEmitter(spy, cb);

        for (int i = 0; i < 5; i++)
            await emitter.EmitAsync(BuildEvent());

        cb.State.Should().Be(CircuitState.Closed);
        spy.EmitCount.Should().Be(5);
    }

    // ── Circuit breaker (emitter failures) ───────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EmitAsync_ConsecutiveFailures_OpensCircuit()
    {
        var throwing = new ThrowingClickEventEmitter();
        var cb = new CircuitBreaker("Analytics", failureThreshold: 2);
        var emitter = new ResilientClickEventEmitter(throwing, cb);

        await emitter.EmitAsync(BuildEvent());
        await emitter.EmitAsync(BuildEvent());

        cb.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EmitAsync_CircuitOpen_SkipsCall()
    {
        var throwing = new ThrowingClickEventEmitter();
        var cb = new CircuitBreaker("Analytics", failureThreshold: 1);
        var emitter = new ResilientClickEventEmitter(throwing, cb);

        await emitter.EmitAsync(BuildEvent());
        cb.State.Should().Be(CircuitState.Open);

        // This should not throw — circuit is open, call is skipped
        var act = () => emitter.EmitAsync(BuildEvent());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EmitAsync_AfterCooldown_ProbesAndRecovers()
    {
        var spy = new SpyClickEventEmitter();
        var cb = new CircuitBreaker("Analytics", failureThreshold: 1, cooldownPeriod: TimeSpan.Zero);
        var throwing = new ThrowingClickEventEmitter();
        var emitter = new ResilientClickEventEmitter(throwing, cb);

        // Open the circuit
        await emitter.EmitAsync(BuildEvent());
        cb.State.Should().Be(CircuitState.Open);

        // Now swap the inner for a working one and retry — the probe should succeed
        var recovered = new ResilientClickEventEmitter(spy, cb);
        await recovered.EmitAsync(BuildEvent());

        cb.State.Should().Be(CircuitState.Closed);
        spy.EmitCount.Should().Be(1);
    }
}
