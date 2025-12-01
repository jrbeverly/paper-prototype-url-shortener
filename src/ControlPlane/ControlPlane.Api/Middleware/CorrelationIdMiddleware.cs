namespace ControlPlane.Api.Middleware;

/// <summary>
/// Extracts or generates a correlation ID for each request, propagates it in the response header,
/// and opens a log scope so all log entries within the request include the CorrelationId field.
/// Accepts <c>X-Correlation-ID</c> from upstream callers (CloudFront, API Gateway) or generates one.
/// Also extracts the X-Ray root trace ID from the <c>X-Amzn-Trace-Id</c> header set by API Gateway
/// when active tracing is enabled, so every log entry within the request includes a TraceId field
/// that can be used to correlate structured logs with X-Ray traces.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    public const string XRayTraceHeaderName = "X-Amzn-Trace-Id";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N");

        context.Response.Headers[HeaderName] = correlationId;

        var traceId = ParseXRayRootId(
            context.Request.Headers[XRayTraceHeaderName].FirstOrDefault());

        var scopeProperties = new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        };
        if (traceId is not null)
            scopeProperties["TraceId"] = traceId;

        using (_logger.BeginScope(scopeProperties))
        {
            await _next(context);
        }
    }

    /// <summary>
    /// Extracts the Root segment ID from an <c>X-Amzn-Trace-Id</c> header value.
    /// Header format: <c>Root=1-{hex8}-{hex24};Parent={hex16};Sampled={0|1}</c>.
    /// Returns the Root value (e.g. <c>1-5b0f9e4b-000000000000000000000001</c>) or <c>null</c>
    /// when the header is absent or contains no Root segment.
    /// </summary>
    internal static string? ParseXRayRootId(string? header)
    {
        if (string.IsNullOrEmpty(header)) return null;
        foreach (var part in header.Split(';'))
        {
            if (part.StartsWith("Root=", StringComparison.OrdinalIgnoreCase))
                return part[5..];
        }
        return null;
    }
}
