using System.Diagnostics;
using System.Security.Authentication;

namespace ControlPlane.Api.Services;

/// <summary>
/// Tests HTTPS reachability and TLS certificate validity by issuing a real HTTP request.
/// Uses a named HttpClient ("diagnostics") with auto-redirect disabled so status codes
/// are reported as-received.
/// </summary>
public sealed class HttpConnectivityCheck : IHttpConnectivityCheck
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpConnectivityCheck> _logger;

    public HttpConnectivityCheck(
        IHttpClientFactory httpClientFactory,
        ILogger<HttpConnectivityCheck> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<HttpConnectivityResult> CheckAsync(string hostname, CancellationToken ct = default)
    {
        var url = $"https://{hostname}/";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RequestTimeout);

        var sw = Stopwatch.StartNew();
        try
        {
            using var client = _httpClientFactory.CreateClient("diagnostics");
            using var response = await client.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            sw.Stop();

            return new HttpConnectivityResult
            {
                HttpsReachable = true,
                SslValid = true,
                StatusCode = (int)response.StatusCode,
                ResponseMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException sslEx)
        {
            _logger.LogDebug(sslEx, "TLS error for {Url}", url);
            return new HttpConnectivityResult
            {
                HttpsReachable = true,
                SslValid = false,
                Error = sslEx.Message
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new HttpConnectivityResult
            {
                HttpsReachable = false,
                SslValid = false,
                Error = $"Connection timed out after {(int)RequestTimeout.TotalSeconds}s"
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "HTTPS connectivity failed for {Hostname}: {Message}", hostname, ex.Message);
            return new HttpConnectivityResult
            {
                HttpsReachable = false,
                SslValid = false,
                Error = ex.Message
            };
        }
    }
}
