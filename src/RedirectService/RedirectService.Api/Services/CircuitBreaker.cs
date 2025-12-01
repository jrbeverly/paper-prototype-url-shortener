namespace RedirectService.Api.Services;

/// <summary>
/// Thread-safe circuit breaker with three states: Closed (normal), Open (fail fast), HalfOpen (testing recovery).
/// After N consecutive failures (configured via <c>failureThreshold</c>) the circuit opens. While open,
/// calls return immediately (throwing a <see cref="CircuitOpenException"/>) without invoking the dependency.
/// After the cooldown period the circuit transitions to half-open and allows one probe request through;
/// success closes the circuit, failure reopens it.
/// </summary>
public sealed class CircuitBreaker
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _cooldownPeriod;
    private readonly string _name;

    private int _failureCount;
    private int _state; // 0 = Closed, 1 = Open, 2 = HalfOpen
    private DateTime _openedAt;
    private int _halfOpenProbe;

    // Lock only for state transitions; the hot path uses lock-free reads.
    private readonly object _lock = new();

    private const int _stateClosed = 0;
    private const int _stateOpen = 1;
    private const int _stateHalfOpen = 2;

    public int OpenCount { get; private set; }
    public int FailureCount => Volatile.Read(ref _failureCount);

    public CircuitState State
    {
        get
        {
            var s = Volatile.Read(ref _state);
            return s switch
            {
                _stateOpen => CircuitState.Open,
                _stateHalfOpen => CircuitState.HalfOpen,
                _ => CircuitState.Closed
            };
        }
    }

    public DateTime OpenedAt => _openedAt;

    public CircuitBreaker(string name, int failureThreshold = 3, TimeSpan? cooldownPeriod = null)
    {
        _name = name;
        _failureThreshold = failureThreshold;
        _cooldownPeriod = cooldownPeriod ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Executes <paramref name="action"/>. Returns the result on success, throws on failure.
    /// When the circuit is open and <paramref name="fallback"/> is provided and returns a value,
    /// that value is returned instead of throwing.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, Func<Task<T>>? fallback = null)
    {
        if (Volatile.Read(ref _state) == _stateOpen)
        {
            if (ShouldAttemptHalfOpen() && TryEnterHalfOpen())
                return await ProbeAsync(action, fallback);

            if (fallback is not null)
            {
                var fallbackResult = await fallback();
                if (fallbackResult is not null)
                    return fallbackResult;
            }

            throw new CircuitOpenException(_name);
        }

        try
        {
            var result = await action();
            OnSuccess();
            return result;
        }
        catch (Exception)
        {
            OnFailure();
            throw;
        }
    }

    /// <summary>
    /// Executes a void <paramref name="action"/>. Exceptions are recorded but not propagated.
    /// When the circuit is open, the action is silently skipped.
    /// </summary>
    public async Task ExecuteVoidAsync(Func<Task> action)
    {
        if (Volatile.Read(ref _state) == _stateOpen)
        {
            if (ShouldAttemptHalfOpen() && TryEnterHalfOpen())
            {
                await ProbeVoidAsync(action);
                return;
            }

            return;
        }

        try
        {
            await action();
            OnSuccess();
        }
        catch (Exception)
        {
            OnFailure();
        }
    }

    private void OnSuccess()
    {
        Interlocked.Exchange(ref _failureCount, 0);
        lock (_lock)
        {
            if (Volatile.Read(ref _state) == _stateHalfOpen)
                Volatile.Write(ref _state, _stateClosed);
        }
    }

    private void OnFailure()
    {
        var count = Interlocked.Increment(ref _failureCount);
        if (count >= _failureThreshold)
        {
            lock (_lock)
            {
                if (Volatile.Read(ref _state) == _stateClosed)
                {
                    Volatile.Write(ref _state, _stateOpen);
                    _openedAt = DateTime.UtcNow;
                    OpenCount++;
                    Interlocked.Exchange(ref _halfOpenProbe, 0);
                }
            }
        }
    }

    private bool ShouldAttemptHalfOpen()
        => DateTime.UtcNow - _openedAt >= _cooldownPeriod;

    private bool TryEnterHalfOpen()
    {
        if (Interlocked.CompareExchange(ref _halfOpenProbe, 1, 0) != 0)
            return false;

        lock (_lock)
        {
            if (Volatile.Read(ref _state) == _stateOpen)
            {
                Volatile.Write(ref _state, _stateHalfOpen);
                return true;
            }
        }

        Interlocked.Exchange(ref _halfOpenProbe, 0);
        return false;
    }

    private async Task<T> ProbeAsync<T>(Func<Task<T>> action, Func<Task<T>>? fallback)
    {
        try
        {
            var result = await action();
            OnSuccess();
            return result;
        }
        catch (Exception)
        {
            ReopenCircuit();

            if (fallback is not null)
            {
                var fallbackResult = await fallback();
                if (fallbackResult is not null)
                    return fallbackResult;
            }

            throw;
        }
    }

    private async Task ProbeVoidAsync(Func<Task> action)
    {
        try
        {
            await action();
            OnSuccess();
        }
        catch (Exception)
        {
            ReopenCircuit();
        }
    }

    private void ReopenCircuit()
    {
        lock (_lock)
        {
            Volatile.Write(ref _state, _stateOpen);
            _openedAt = DateTime.UtcNow;
            OpenCount++;
        }

        Interlocked.Exchange(ref _halfOpenProbe, 0);
    }
}

/// <summary>Thrown when the circuit is open and no fallback is available.</summary>
public sealed class CircuitOpenException(string circuitName)
    : InvalidOperationException($"Circuit '{circuitName}' is open — dependency is unavailable.");

public enum CircuitState
{
    Closed = 0,
    Open = 1,
    HalfOpen = 2
}
