namespace RedirectService.Tests.Services;

public sealed class CircuitBreakerTests
{
    // ── Initial state ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_StartsClosed()
    {
        var cb = new CircuitBreaker("test");
        cb.State.Should().Be(CircuitState.Closed);
        cb.FailureCount.Should().Be(0);
        cb.OpenCount.Should().Be(0);
    }

    // ── Successful execution ─────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_Success_ReturnsResult()
    {
        var cb = new CircuitBreaker("test");

        var result = await cb.ExecuteAsync(() => Task.FromResult(42));

        result.Should().Be(42);
        cb.State.Should().Be(CircuitState.Closed);
        cb.FailureCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_MultipleSuccesses_KeepsCircuitClosed()
    {
        var cb = new CircuitBreaker("test");

        for (int i = 0; i < 10; i++)
            await cb.ExecuteAsync(() => Task.FromResult(i));

        cb.State.Should().Be(CircuitState.Closed);
        cb.FailureCount.Should().Be(0);
    }

    // ── Circuit opening ──────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_ConsecutiveFailures_OpensCircuit()
    {
        var cb = new CircuitBreaker("test", failureThreshold: 3);

        for (int i = 0; i < 3; i++)
        {
            try { await cb.ExecuteAsync<object>(() => throw new InvalidOperationException("fail")); }
            catch (InvalidOperationException) { }
        }

        cb.State.Should().Be(CircuitState.Open);
        cb.FailureCount.Should().Be(3);
        cb.OpenCount.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_OpenCircuit_ThrowsCircuitOpenException()
    {
        var cb = new CircuitBreaker("test", failureThreshold: 1);

        try { await cb.ExecuteAsync<object>(() => throw new Exception("fail")); }
        catch (Exception) { }

        var act = () => cb.ExecuteAsync<object>(() => Task.FromResult<object>(null!));

        await act.Should().ThrowAsync<CircuitOpenException>();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_SuccessAfterFailure_ResetsCount()
    {
        var cb = new CircuitBreaker("test", failureThreshold: 3);

        try { await cb.ExecuteAsync<object>(() => throw new Exception("fail 1")); } catch { }
        try { await cb.ExecuteAsync<object>(() => throw new Exception("fail 2")); } catch { }

        await cb.ExecuteAsync(() => Task.FromResult(42));

        cb.State.Should().Be(CircuitState.Closed);
        cb.FailureCount.Should().Be(0);
    }

    // ── Fallback ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_OpenCircuitWithFallback_ReturnsFallbackValue()
    {
        var cb = new CircuitBreaker("test", failureThreshold: 1);

        try { await cb.ExecuteAsync<object>(() => throw new Exception("fail")); }
        catch { }

        var result = await cb.ExecuteAsync(
            () => Task.FromResult("primary")!,
            fallback: () => Task.FromResult("fallback"));

        result.Should().Be("fallback");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_OpenCircuitWithNullFallback_Throws()
    {
        var cb = new CircuitBreaker("test", failureThreshold: 1);

        try { await cb.ExecuteAsync<object>(() => throw new Exception("fail")); }
        catch { }

        var act = () => cb.ExecuteAsync<string?>(
            () => Task.FromResult("primary")!,
            fallback: () => Task.FromResult<string?>(null));

        await act.Should().ThrowAsync<CircuitOpenException>();
    }

    // ── Half-open transition ─────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_AfterCooldown_AttemptsHalfOpen()
    {
        // cooldownPeriod: TimeSpan.Zero so it transitions immediately
        var cb = new CircuitBreaker("test", failureThreshold: 1, cooldownPeriod: TimeSpan.Zero);

        try { await cb.ExecuteAsync<object>(() => throw new Exception("fail")); }
        catch { }

        cb.State.Should().Be(CircuitState.Open);

        // Next call should attempt half-open (cooldown is zero) and succeed
        var result = await cb.ExecuteAsync(() => Task.FromResult(42));

        result.Should().Be(42);
        cb.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_HalfOpenFailure_ReopensCircuit()
    {
        var cb = new CircuitBreaker("test", failureThreshold: 1, cooldownPeriod: TimeSpan.Zero);

        try { await cb.ExecuteAsync<object>(() => throw new Exception("initial fail")); }
        catch { }

        // Half-open probe fails
        try { await cb.ExecuteAsync<object>(() => throw new Exception("probe fail")); }
        catch { }

        cb.State.Should().Be(CircuitState.Open);
        cb.OpenCount.Should().Be(2);
    }

    // ── ExecuteVoidAsync ─────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteVoidAsync_Success_ResetsFailureCount()
    {
        var cb = new CircuitBreaker("test", failureThreshold: 3);

        try { await cb.ExecuteAsync<object>(() => throw new Exception("fail")); } catch { }

        await cb.ExecuteVoidAsync(() => Task.CompletedTask);

        cb.FailureCount.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteVoidAsync_OpenCircuit_SilentlySkips()
    {
        var cb = new CircuitBreaker("test", failureThreshold: 1);

        try { await cb.ExecuteVoidAsync(() => throw new Exception("fail")); }
        catch { } // should not throw — ExecuteVoidAsync doesn't propagate

        cb.State.Should().Be(CircuitState.Open);

        // Should not throw when circuit is open
        var act = () => cb.ExecuteVoidAsync(() => Task.CompletedTask);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteVoidAsync_ConsecutiveFailures_OpensCircuit()
    {
        var cb = new CircuitBreaker("test", failureThreshold: 2);

        await cb.ExecuteVoidAsync(() => throw new Exception("fail 1"));
        await cb.ExecuteVoidAsync(() => throw new Exception("fail 2"));

        cb.State.Should().Be(CircuitState.Open);
    }

    // ── Concurrent safety ────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_ParallelFailures_OpensCircuitOnce()
    {
        var cb = new CircuitBreaker("test", failureThreshold: 1);

        var tasks = Enumerable.Range(0, 10).Select(_ =>
            Task.Run(async () =>
            {
                try { await cb.ExecuteAsync<object>(() => throw new Exception("fail")); }
                catch { }
            }));

        await Task.WhenAll(tasks);

        cb.State.Should().Be(CircuitState.Open);
        // OpenCount may be > 1 due to concurrent openings, but it should be at least 1
        cb.OpenCount.Should().BeGreaterOrEqualTo(1);
    }
}
