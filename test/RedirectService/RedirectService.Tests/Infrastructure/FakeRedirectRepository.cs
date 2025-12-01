namespace RedirectService.Tests.Infrastructure;

/// <summary>
/// Controllable in-memory redirect repository for unit tests.
/// Records the last hostname and slug passed to <see cref="GetAsync"/> for assertion.
/// </summary>
internal sealed class FakeRedirectRepository : IRedirectRepository
{
    private RedirectRecord? _record;
    private bool _incrementResult = true;
    private Exception? _exception;
    private Exception? _incrementException;
    private TimeSpan _getDelay = TimeSpan.Zero;

    /// <summary>The hostname passed to the last <see cref="GetAsync"/> call.</summary>
    public string? LastHostname { get; private set; }

    /// <summary>The slug passed to the last <see cref="GetAsync"/> call.</summary>
    public string? LastSlug { get; private set; }

    /// <summary>The hostname passed to the last <see cref="TryIncrementClickAsync"/> call.</summary>
    public string? LastIncrementHostname { get; private set; }

    /// <summary>The slug passed to the last <see cref="TryIncrementClickAsync"/> call.</summary>
    public string? LastIncrementSlug { get; private set; }

    /// <summary>Number of times <see cref="TryIncrementClickAsync"/> was called.</summary>
    public int IncrementCallCount { get; private set; }

    /// <summary>Configures the repository to return the given record on the next call.</summary>
    public void Returns(RedirectRecord? record) => _record = record;

    /// <summary>Configures whether <see cref="TryIncrementClickAsync"/> succeeds or fails.</summary>
    public void SetIncrementResult(bool result) => _incrementResult = result;

    /// <summary>Configures the repository to throw the given exception on the next <see cref="GetAsync"/> call.</summary>
    public void Throws(Exception exception) => _exception = exception;

    /// <summary>Configures the repository to throw the given exception on the next <see cref="TryIncrementClickAsync"/> call.</summary>
    public void SetIncrementThrows(Exception exception) => _incrementException = exception;

    /// <summary>Configures a delay before <see cref="GetAsync"/> returns (to test timeouts).</summary>
    public void SetGetDelay(TimeSpan delay) => _getDelay = delay;

    public async Task<RedirectRecord?> GetAsync(string hostname, string slug, CancellationToken ct = default)
    {
        LastHostname = hostname;
        LastSlug = slug;

        if (_getDelay > TimeSpan.Zero)
            await Task.Delay(_getDelay, ct);

        if (_exception is not null)
            throw _exception;

        return _record;
    }

    public Task<bool> TryIncrementClickAsync(string hostname, string slug, CancellationToken ct = default)
    {
        LastIncrementHostname = hostname;
        LastIncrementSlug = slug;
        IncrementCallCount++;

        if (_incrementException is not null)
            throw _incrementException;

        return Task.FromResult(_incrementResult);
    }
}
