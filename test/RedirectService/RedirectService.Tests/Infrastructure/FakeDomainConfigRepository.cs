namespace RedirectService.Tests.Infrastructure;

internal sealed class FakeDomainConfigRepository : IDomainConfigRepository
{
    private DomainConfig? _config;
    private Exception? _exception;

    public int GetCallCount { get; private set; }

    public void Returns(DomainConfig? config) => _config = config;
    public void Throws(Exception exception) => _exception = exception;

    public Task<DomainConfig?> GetAsync(string hostname, CancellationToken ct = default)
    {
        GetCallCount++;
        if (_exception is not null) throw _exception;
        return Task.FromResult(_config);
    }
}
