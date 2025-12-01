using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Common.ErrorHandling;

public sealed class DomainExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DomainExceptionMiddleware> _logger;

    public DomainExceptionMiddleware(RequestDelegate next, ILogger<DomainExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);

            if (ShouldWriteProblemDetails(context))
            {
                await WriteProblemDetails(context, null);
            }
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex, "Domain exception: {StatusCode} {Title}", ex.StatusCode, ex.Title);
            await WriteProblemDetails(context, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
            await WriteProblemDetails(context, null);
        }
    }

    private static bool ShouldWriteProblemDetails(HttpContext context)
    {
        return context.Response.StatusCode >= 400
            && !context.Response.HasStarted;
    }

    private static async Task WriteProblemDetails(HttpContext context, DomainException? ex)
    {
        var statusCode = ex?.StatusCode ?? context.Response.StatusCode;
        var title = ex?.Title ?? GetTitleForStatusCode(statusCode);
        var type = ex?.ErrorType ?? $"https://api.short.io/errors/{title.ToLowerInvariant().Replace(" ", "-")}";
        var detail = ex?.Message ?? GetDetailForStatusCode(statusCode);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Type = type,
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = context.Request.Path
        };

        AddTraceId(problem);
        if (ex is not null)
        {
            AddCustomProperties(problem, ex);
        }

        await context.Response.WriteAsJsonAsync(problem, _jsonSerializerOptions);
    }

    private static string GetTitleForStatusCode(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        402 => "Payment Required",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        406 => "Not Acceptable",
        408 => "Request Timeout",
        409 => "Conflict",
        410 => "Gone",
        415 => "Unsupported Media Type",
        422 => "Unprocessable Entity",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        502 => "Bad Gateway",
        503 => "Service Unavailable",
        _ => "Internal Server Error"
    };

    private static string GetDetailForStatusCode(int statusCode) => statusCode switch
    {
        400 => "The request was invalid or cannot be processed.",
        401 => "Authentication is required to access this resource.",
        402 => "Your plan limit has been reached. Upgrade your plan to continue.",
        403 => "You do not have permission to access this resource.",
        404 => "The requested resource was not found.",
        405 => "The HTTP method is not supported for this resource.",
        406 => "The requested media type is not acceptable.",
        408 => "The request timed out.",
        409 => "The request conflicts with the current state of the resource.",
        410 => "The requested resource is no longer available.",
        415 => "The request body media type is not supported.",
        422 => "The request was well-formed but contains semantic errors.",
        429 => "Too many requests. Please try again later.",
        500 => "An unexpected error occurred. Please try again later.",
        502 => "The server received an invalid response from an upstream service.",
        503 => "The service is temporarily unavailable. Please try again later.",
        _ => "An unexpected error occurred. Please try again later."
    };

    private static void AddTraceId(ProblemDetails problem)
    {
        var traceId = Activity.Current?.Id;
        if (!string.IsNullOrEmpty(traceId))
        {
            problem.Extensions["traceId"] = traceId;
        }
    }

    private static void AddCustomProperties(ProblemDetails problem, Exception ex)
    {
        if (ex is TooManyRequestsException rateLimit)
        {
            problem.Extensions["retryAfter"] = rateLimit.RetryAfterSeconds;
        }

        if (ex is DomainException domainEx && domainEx.UpgradeUrl is not null)
        {
            problem.Extensions["upgradeUrl"] = domainEx.UpgradeUrl;
        }
    }

    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}
