namespace RedirectService.Tests.Infrastructure;

internal sealed class StubRedirectService : IRedirectService
{
    private RedirectResult _result = RedirectResult.NotFound();

    public void Returns(RedirectResult result) => _result = result;

    public Task<RedirectResult> ResolveAsync(RedirectRequest request, CancellationToken ct = default)
        => Task.FromResult(_result);
}
