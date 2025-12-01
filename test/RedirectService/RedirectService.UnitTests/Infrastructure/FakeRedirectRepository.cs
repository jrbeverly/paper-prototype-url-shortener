namespace RedirectService.UnitTests.Infrastructure;

/// <summary>
/// Controllable in-memory redirect repository for unit tests.
/// Records call arguments for assertion; configurable to return, throw, or delay.
/// </summary>
internal sealed class FakeRedirectRepository : IRedirectRepository
{
    private RedirectRecord? _record;
    private bool _incrementResult = true;
    private Exception? _getException;
    private Exception? _incrementException;

    public string? LastHostname { get; private set; }
    public string? LastSlug { get; private set; }
    public string? LastIncrementHostname { get; private set; }
    public string? LastIncrementSlug { get; private set; }
    public int IncrementCallCount { get; private set; }

    public void Returns(RedirectRecord? record) => _record = record;
    public void SetIncrementResult(bool result) => _incrementResult = result;
    public void Throws(Exception exception) => _getException = exception;
    public void SetIncrementThrows(Exception exception) => _incrementException = exception;

    public Task<RedirectRecord?> GetAsync(string hostname, string slug, CancellationToken ct = default)
    {
        LastHostname = hostname;
        LastSlug = slug;

        if (_getException is not null)
            throw _getException;

        return Task.FromResult(_record);
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

