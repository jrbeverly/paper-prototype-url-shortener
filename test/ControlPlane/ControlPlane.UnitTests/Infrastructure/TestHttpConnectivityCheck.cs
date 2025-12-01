using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Infrastructure;

public sealed class TestHttpConnectivityCheck : IHttpConnectivityCheck
{
    private HttpConnectivityResult _result = DefaultPass();

    public void SetResult(HttpConnectivityResult result) => _result = result;

    public Task<HttpConnectivityResult> CheckAsync(string hostname, CancellationToken ct = default)
        => Task.FromResult(_result);

    public static HttpConnectivityResult DefaultPass() => new()
    {
        HttpsReachable = true,
        SslValid = true,
        StatusCode = 200,
        ResponseMs = 42
    };

    public static HttpConnectivityResult Unreachable(string? error = null) => new()
    {
        HttpsReachable = false,
        SslValid = false,
        Error = error ?? "Connection refused"
    };

    public static HttpConnectivityResult InvalidSsl(string? error = null) => new()
    {
        HttpsReachable = true,
        SslValid = false,
        Error = error ?? "The SSL certificate is invalid."
    };
}
