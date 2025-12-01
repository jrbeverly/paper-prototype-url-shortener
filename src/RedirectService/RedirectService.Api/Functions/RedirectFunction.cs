using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Kinesis;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.XRay.Recorder.Handlers.AwsSdk;
using RedirectService.Api.Models;
using RedirectService.Api.Repositories;
using RedirectService.Api.Services;
// Alias required: the class name "RedirectService" conflicts with the root namespace "RedirectService".
using RedirectServiceImpl = global::RedirectService.Api.Services.RedirectService;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace RedirectService.Api.Functions;

/// <summary>
/// Lambda handler for the redirect hot path.
/// Resolves <c>hostname#slug</c> to a destination URL via DynamoDB and returns the appropriate HTTP redirect.
/// </summary>
public sealed class RedirectFunction : IDisposable
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly IRedirectService _service;
    private readonly IDomainConfigRepository? _domainConfigRepo;
    private readonly DomainConfigCache _domainConfigCache;
    private readonly ResilientRedirectRepository? _resilientRepo;
    private readonly CircuitBreaker? _analyticsCircuitBreaker;
    private readonly IRedirectRateLimiter _rateLimiter;

    private static readonly string _version =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

    static RedirectFunction()
    {
        // Instrument all AWS SDK calls (DynamoDB) with X-Ray subsegments.
        // The Lambda runtime creates the root segment when active tracing is enabled;
        // we only need to register the SDK handler so DynamoDB calls appear as children.
        AWSSDKHandler.RegisterXRayForAllServices();
    }

    /// <summary>Default constructor used by the Lambda managed runtime.</summary>
    public RedirectFunction()
    {
        var dynamoDb = new AmazonDynamoDBClient();
        var tableName = Environment.GetEnvironmentVariable("REDIRECTS_TABLE_NAME")
            ?? throw new InvalidOperationException("REDIRECTS_TABLE_NAME environment variable is required");
        _dynamoDb = dynamoDb;
        var innerRepository = new DynamoDbRedirectRepository(dynamoDb, tableName);

        // Resilience: circuit breaker, in-memory cache, and per-call timeouts for DynamoDB.
        // After 3 consecutive failures the circuit opens; while open, lookups fall back to
        // the hot-link cache and increments are skipped. The circuit auto-recovers after 30s.
        var dynamoCircuitBreaker = new CircuitBreaker("DynamoDB",
            failureThreshold: 3,
            cooldownPeriod: TimeSpan.FromSeconds(30));
        var hotLinkCache = new HotLinkCache(
            ttl: TimeSpan.FromMinutes(5),
            maxEntries: 1000);
        var repository = new ResilientRedirectRepository(
            innerRepository,
            dynamoCircuitBreaker,
            hotLinkCache,
            timeout: TimeSpan.FromMilliseconds(500));
        _resilientRepo = repository;

        var streamName = Environment.GetEnvironmentVariable("CLICK_EVENTS_STREAM_NAME");
        IClickEventEmitter innerEmitter = string.IsNullOrEmpty(streamName)
            ? new NullClickEventEmitter()
            : new KinesisClickEventEmitter(new AmazonKinesisClient(), streamName);

        // Resilience: circuit breaker so analytics failures don't block redirects.
        // After 3 consecutive emission failures the circuit opens and emission is
        // silently skipped for 60s before probing for recovery.
        var analyticsCircuitBreaker = new CircuitBreaker("Analytics",
            failureThreshold: 3,
            cooldownPeriod: TimeSpan.FromSeconds(60));
        _analyticsCircuitBreaker = analyticsCircuitBreaker;
        var emitter = new ResilientClickEventEmitter(innerEmitter, analyticsCircuitBreaker);

        _service = new RedirectServiceImpl(repository, emitter);
        _domainConfigRepo = new DynamoDbDomainConfigRepository(dynamoDb, tableName);
        // Branding config changes rarely — cache for 15 minutes to avoid a DynamoDB call
        // on every error response. Bounded to 500 entries (one per distinct custom domain).
        _domainConfigCache = new DomainConfigCache(ttl: TimeSpan.FromMinutes(15), maxEntries: 500);
        _rateLimiter = new InMemoryRedirectRateLimiter(RedirectRateLimitOptions.FromEnvironment());
    }

    /// <summary>Constructor for tests; accepts injected dependencies.</summary>
    public RedirectFunction(IAmazonDynamoDB dynamoDb, IRedirectService service,
        IDomainConfigRepository? domainConfigRepo = null,
        IRedirectRateLimiter? rateLimiter = null)
    {
        _dynamoDb = dynamoDb;
        _service = service;
        _domainConfigRepo = domainConfigRepo;
        _domainConfigCache = new DomainConfigCache(ttl: TimeSpan.FromMinutes(15), maxEntries: 500);
        _rateLimiter = rateLimiter ?? new InMemoryRedirectRateLimiter(new RedirectRateLimitOptions());
    }

    /// <summary>
    /// Lambda handler method.
    /// Invoked by the Lambda managed runtime for each HTTP request routed through API Gateway HTTP API (payload format 2.0).
    /// </summary>
    /// <param name="request">API Gateway HTTP API v2 proxy request containing hostname and path.</param>
    /// <param name="context">Lambda execution context providing logging and metadata.</param>
    /// <returns>API Gateway proxy response with the redirect location header and status code.</returns>
    public async Task<APIGatewayHttpApiV2ProxyResponse> HandleAsync(
        APIGatewayHttpApiV2ProxyRequest request,
        ILambdaContext context)
    {
        // Extract or generate a correlation ID; propagate it through the response so callers
        // can correlate their request with entries in CloudWatch Logs.
        var correlationId = request.Headers.TryGetValue("x-correlation-id", out var cid) && !string.IsNullOrEmpty(cid)
            ? cid
            : Guid.NewGuid().ToString("N");

        // Extract the X-Ray root trace ID forwarded by API Gateway. When active tracing is
        // enabled the header is always present; outside Lambda it may be absent.
        var traceId = ParseXRayRootId(
            request.Headers.TryGetValue("x-amzn-trace-id", out var tid) ? tid : null);

        var path = request.RequestContext.Http.Path;

        if (path == "/health")
            return await HandleHealthAsync(request, context, correlationId, traceId);

        var host = request.Headers.TryGetValue("host", out var h) ? h : "";
        var slug = path.TrimStart('/');

        // Rate limiting: check per-IP and per-domain before touching DynamoDB.
        // Exempt search crawlers and known integrations to avoid blocking legitimate traffic.
        var clientIp = ExtractClientIp(request.Headers);
        var userAgent = request.Headers.TryGetValue("user-agent", out var ua) ? ua : "";

        if (!_rateLimiter.IsExemptIp(clientIp) && !_rateLimiter.IsExemptUserAgent(userAgent))
        {
            var ipResult = _rateLimiter.CheckIp(clientIp);
            if (!ipResult.IsAllowed)
            {
                context.Logger.LogWarning(
                    "Rate limited by IP: ip={Ip} host={Host} slug={Slug} limit={Limit} retryAfter={RetryAfter} correlationId={CorrelationId}",
                    clientIp, host, slug, ipResult.Limit, ipResult.RetryAfterSeconds, correlationId);
                return BuildRateLimitResponse(ipResult, correlationId);
            }

            var domainResult = _rateLimiter.CheckDomain(host);
            if (!domainResult.IsAllowed)
            {
                context.Logger.LogWarning(
                    "Rate limited by domain: host={Host} slug={Slug} limit={Limit} retryAfter={RetryAfter} correlationId={CorrelationId}",
                    host, slug, domainResult.Limit, domainResult.RetryAfterSeconds, correlationId);
                return BuildRateLimitResponse(domainResult, correlationId);
            }
        }

        // API Gateway provides IDictionary<string,string>; RedirectRequest requires IReadOnlyDictionary.
        // Dictionary<T,T> satisfies both interfaces at runtime, but IDictionary does not coerce implicitly.
        var redirectRequest = new RedirectRequest
        {
            Hostname = host,
            Slug = slug,
            QueryParameters = new Dictionary<string, string>(request.QueryStringParameters ?? new Dictionary<string, string>()),
            Headers = new Dictionary<string, string>(request.Headers)
        };

        // Each DynamoDB call is individually capped at 500ms by ResilientRedirectRepository.
        // This overall timeout (1500ms) is a safety net so a stuck resolve cannot hold the
        // Lambda invocation open past API Gateway's 30s max integration timeout.
        using var resolveCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
        var result = await _service.ResolveAsync(redirectRequest, resolveCts.Token);

        context.Logger.LogInformation(
            "Redirect resolved: host={Host} slug={Slug} outcome={Outcome} statusCode={StatusCode} correlationId={CorrelationId}",
            host, slug, result.Outcome, result.StatusCode, correlationId);

        return result.Outcome switch
        {
            RedirectOutcome.Redirect => BuildRedirectResponse(result, correlationId),
            _ => await HandleErrorAsync(result, host, correlationId, context)
        };
    }

    // ── Health check ─────────────────────────────────────────────────────────

    private async Task<APIGatewayHttpApiV2ProxyResponse> HandleHealthAsync(
        APIGatewayHttpApiV2ProxyRequest request,
        ILambdaContext context,
        string correlationId,
        string? traceId)
    {
        var dynamoStatus = await CheckDynamoDbAsync(context);

        // Include circuit breaker and cache state for observability into the resilience layer.
        var dynamoCircuitState = _resilientRepo?.CircuitBreaker.State.ToString().ToLowerInvariant();
        var cacheEntryCount = _resilientRepo?.Cache.Count ?? 0;
        var analyticsCircuitState = _analyticsCircuitBreaker?.State.ToString().ToLowerInvariant();

        var overallStatus = dynamoStatus.Status switch
        {
            "unhealthy" => "unhealthy",
            "degraded" => "degraded",
            _ => "healthy"
        };

        EmitMetric(context, dynamoStatus.Name, dynamoStatus.Status, dynamoStatus.LatencyMs);

        var body = JsonSerializer.Serialize(new
        {
            status = overallStatus,
            version = _version,
            dependencies = new[] { dynamoStatus },
            circuitBreakers = new
            {
                dynamodb = dynamoCircuitState,
                analytics = analyticsCircuitState
            },
            cache = new
            {
                hotLinkEntries = cacheEntryCount,
                domainConfigEntries = _domainConfigCache.Count
            }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        context.Logger.LogInformation(
            "Health check: status={OverallStatus} dynamo={DynamoStatus} dynamoCircuit={DynamoCircuit} analyticsCircuit={AnalyticsCircuit} cacheEntries={CacheEntries}",
            overallStatus, dynamoStatus.Status, dynamoCircuitState, analyticsCircuitState, cacheEntryCount);

        var statusCode = overallStatus == "unhealthy" ? 503 : 200;

        var headers = BuildBaseHeaders(correlationId);
        headers["Content-Type"] = "application/json";

        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = statusCode,
            Body = body,
            Headers = headers
        };
    }

    private readonly ConcurrentState _state = new();

    private const int _dynamoDbTimeoutMs = 500;
    private const int _unhealthyThreshold = 3;

    private async Task<HealthDependency> CheckDynamoDbAsync(ILambdaContext context)
    {
        using var cts = new CancellationTokenSource(_dynamoDbTimeoutMs);
        var sw = Stopwatch.StartNew();
        try
        {
            await _dynamoDb.ListTablesAsync(new ListTablesRequest { Limit = 1 }, cts.Token);
            sw.Stop();
            _state.ConsecutiveFailures = 0;
            return new HealthDependency("dynamodb", "healthy", sw.ElapsedMilliseconds, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            context.Logger.LogWarning("DynamoDB health check failed: {Message}", ex.Message);
            var count = ++_state.ConsecutiveFailures;
            var status = count >= _unhealthyThreshold ? "unhealthy" : "degraded";
            return new HealthDependency("dynamodb", status, sw.ElapsedMilliseconds, ex.Message);
        }
    }

    // Emits a CloudWatch Embedded Metric Format (EMF) line. Lambda stdout is
    // ingested by CloudWatch Logs, which auto-extracts EMF metrics.
    private static void EmitMetric(ILambdaContext context, string dependency, string status, long latencyMs)
    {
        double value = status switch
        {
            "healthy" => 1.0,
            "degraded" => 0.5,
            _ => 0.0
        };

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var emf = $"{{\"_aws\":{{\"Timestamp\":{timestamp},\"CloudWatchMetrics\":[{{\"Namespace\":\"ShortIo/RedirectService\",\"Dimensions\":[[\"Dependency\"]],\"Metrics\":[{{\"Name\":\"DependencyHealth\",\"Unit\":\"None\"}}]}}]}},\"Dependency\":\"{dependency}\",\"DependencyHealth\":{value},\"LatencyMs\":{latencyMs}}}";
        Console.WriteLine(emf);
    }

    /// <summary>
    /// Extracts the Root segment ID from an <c>X-Amzn-Trace-Id</c> header value.
    /// Header format: <c>Root=1-{hex8}-{hex24};Parent={hex16};Sampled={0|1}</c>.
    /// Returns <c>null</c> when the header is absent or contains no Root segment.
    /// </summary>
    private static string? ParseXRayRootId(string? header)
    {
        if (string.IsNullOrEmpty(header)) return null;
        foreach (var part in header.Split(';'))
        {
            if (part.StartsWith("Root=", StringComparison.OrdinalIgnoreCase))
                return part[5..];
        }
        return null;
    }

    // ── Response builders ─────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the real client IP from CloudFront or fallback headers.
    /// CloudFront-Viewer-Address carries <c>ip:port</c> when the distribution is in front.
    /// X-Forwarded-For is used as fallback for direct API Gateway access.
    /// </summary>
    private static string ExtractClientIp(IDictionary<string, string> headers)
    {
        if (headers.TryGetValue("cloudfront-viewer-address", out var cfAddr)
            && !string.IsNullOrEmpty(cfAddr))
        {
            var colonIdx = cfAddr.LastIndexOf(':');
            return colonIdx > 0 ? cfAddr[..colonIdx] : cfAddr;
        }

        if (headers.TryGetValue("x-forwarded-for", out var xff) && !string.IsNullOrEmpty(xff))
        {
            var commaIdx = xff.IndexOf(',');
            return commaIdx > 0 ? xff[..commaIdx].Trim() : xff.Trim();
        }

        return "unknown";
    }

    private static APIGatewayHttpApiV2ProxyResponse BuildRateLimitResponse(
        RedirectRateLimitResult result, string correlationId)
    {
        var headers = BuildBaseHeaders(correlationId);
        headers["Retry-After"] = $"{result.RetryAfterSeconds}";
        headers["X-RateLimit-Limit"] = $"{result.Limit}";
        headers["X-RateLimit-Remaining"] = "0";
        headers["X-RateLimit-Reset"] = $"{result.ResetUnixSeconds}";
        headers["Content-Type"] = "text/plain; charset=utf-8";

        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = 429,
            Body = "Too Many Requests",
            Headers = headers
        };
    }

    private static APIGatewayHttpApiV2ProxyResponse BuildRedirectResponse(
        RedirectResult result, string correlationId)
    {
        var headers = BuildBaseHeaders(correlationId);
        headers["Location"] = result.DestinationUrl!;
        headers["Cache-Control"] = CacheControlFor(result.StatusCode);

        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = result.StatusCode,
            Headers = headers
        };
    }

    // ── Error page handler ────────────────────────────────────────────────────

    private const int _domainConfigTimeoutMs = 500;

    private async Task<APIGatewayHttpApiV2ProxyResponse> HandleErrorAsync(
        RedirectResult result, string host, string correlationId, ILambdaContext context)
    {
        // Check in-process cache first. TryGet returns true even when config is null (negative
        // cache hit), so no DynamoDB call is made for hosts known to have no configuration.
        if (!_domainConfigCache.TryGet(host, out var config) && _domainConfigRepo is not null)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(_domainConfigTimeoutMs));
                config = await _domainConfigRepo.GetAsync(host, cts.Token);
                // Cache the result (including null) so subsequent error responses for the same
                // host skip DynamoDB for the next 15 minutes.
                _domainConfigCache.Set(host, config);
            }
            catch (Exception ex)
            {
                context.Logger.LogWarning(
                    "Domain config lookup failed for host={Host}: {Message}", host, ex.Message);
                // A transient failure is not cached — the next error response will retry.
            }
        }

        // When the tenant has configured a fallback URL, redirect there with an error code
        // query parameter so their site can display a customized message.
        if (config?.FallbackUrl is string fallbackUrl)
        {
            var sep = fallbackUrl.Contains('?') ? '&' : '?';
            var location = $"{fallbackUrl}{sep}error={result.StatusCode}";
            var redirectHeaders = BuildBaseHeaders(correlationId);
            redirectHeaders["Location"] = location;
            redirectHeaders["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            return new APIGatewayHttpApiV2ProxyResponse { StatusCode = 302, Headers = redirectHeaders };
        }

        var html = result.Outcome switch
        {
            RedirectOutcome.NotFound => ErrorPageRenderer.RenderNotFound(config, host),
            RedirectOutcome.Expired => ErrorPageRenderer.RenderExpired(config, host, result.ExpiredReason),
            RedirectOutcome.Suspended => ErrorPageRenderer.RenderSuspended(config, host),
            RedirectOutcome.Quarantined => ErrorPageRenderer.RenderQuarantined(config, host),
            _ => ErrorPageRenderer.RenderNotFound(config, host)
        };

        var errorHeaders = BuildBaseHeaders(correlationId);
        errorHeaders["Content-Type"] = "text/html; charset=utf-8";
        errorHeaders["Cache-Control"] = "no-store";
        errorHeaders["Content-Security-Policy"] =
            "default-src 'none'; style-src 'unsafe-inline'; img-src https: http:; base-uri 'none';";
        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = result.StatusCode,
            Body = html,
            Headers = errorHeaders
        };
    }

    private static Dictionary<string, string> BuildBaseHeaders(string correlationId)
        => new()
        {
            ["X-Correlation-ID"] = correlationId,
            ["X-Content-Type-Options"] = "nosniff",
            ["X-Frame-Options"] = "DENY",
            ["Referrer-Policy"] = "strict-origin-when-cross-origin",
            ["Access-Control-Allow-Origin"] = "*"
        };

    private static string CacheControlFor(int statusCode) => statusCode switch
    {
        301 or 308 => "public, max-age=3600",
        _ => "no-store, no-cache, must-revalidate, max-age=0"
    };

    public void Dispose() => _rateLimiter.Dispose();

    // Tracks inter-invocation state within a warm Lambda container.
    private sealed class ConcurrentState
    {
        public int ConsecutiveFailures;
    }

    private sealed record HealthDependency(string Name, string Status, long LatencyMs, string? Error);
}
