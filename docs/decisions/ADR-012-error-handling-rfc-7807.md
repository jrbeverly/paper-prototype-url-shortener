# ADR-012: Error Handling and RFC 7807 Adoption

**Status:** Accepted
**Date:** 2026-05-30
**Deciders:** Solo founder

---

## Purpose

Define the platform's error handling strategy: how errors are modeled in code, mapped to HTTP responses, and communicated to API consumers — with a specific decision to adopt RFC 7807 Problem Details.

## Context

The platform has three error domains:

1. **API boundary errors** — Validation failures (400), authentication failures (401), authorization failures (403), not-found errors (404), rate limiting (429). These must be returned as structured, machine-readable HTTP responses.
2. **Domain/business rule errors** — "Link slug already exists", "Domain not verified", "Plan limit exceeded". These are expected errors that should produce specific HTTP status codes and messages.
3. **Unexpected errors** — Unhandled exceptions, infrastructure failures, bugs. These must produce 500 responses without leaking internal state.

Error handling approaches evaluated:

| Approach | How it works | Pros | Cons |
|---|---|---|---|
| **Problem Details (RFC 7807)** | Standard JSON error format with `type`, `title`, `status`, `detail` | Industry standard, machine-readable, extensible | Requires consistent adoption across all endpoints |
| **Custom JSON envelope** | `{ "error": { "code": "...", "message": "..." } }` | Simple, flexible | Non-standard, each API invents its own format |
| **Plain text** | `"Not Found"` | Simplest | Not machine-readable, no structure |

## Approach

Use **RFC 7807 Problem Details** as the standard error response format for all API errors. Combine it with a **DomainException hierarchy** (for expected errors) and **middleware-based mapping** (for automatic conversion).

### Error Architecture

```
API Layer (endpoints)
  ├── DomainException thrown by policy services and domain logic
  │     ↓
  │   DomainExceptionMiddleware (catches all DomainException types)
  │     ↓
  │   Converts to ProblemDetails JSON response
  │
  ├── Validation failures (DataAnnotations / FluentValidation)
  │     ↓
  │   ASP.NET validation middleware
  │     ↓
  │   Converts to ProblemDetails with "errors" extension
  │
  └── Unhandled exceptions
        ↓
      Exception middleware (last resort)
        ↓
      Logs error, returns generic 500 ProblemDetails (no internal detail)
```

### Problem Details Format

Every error response follows this structure:

```json
{
  "type": "https://api.short.io/errors/not-found",
  "title": "Not Found",
  "status": 404,
  "detail": "Link with slug 'summer-sale' does not exist on domain 'go.customer.com'.",
  "instance": "/api/v1/links/go.customer.com/summer-sale",
  "traceId": "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"
}
```

| Field | Required | Description |
|---|---|---|
| `type` | Yes | URI identifying the error type (may point to documentation) |
| `title` | Yes | Human-readable summary (same for all occurrences of this type) |
| `status` | Yes | HTTP status code |
| `detail` | Yes | Human-readable explanation specific to this occurrence |
| `instance` | No | URI of the request that caused the error |
| `traceId` | No | W3C trace context ID for correlating with logs |

**Validation errors** include an `errors` extension with field-level details:

```json
{
  "type": "https://api.short.io/errors/validation-error",
  "title": "Validation Error",
  "status": 400,
  "detail": "One or more fields failed validation.",
  "errors": {
    "DestinationUrl": ["Destination URL must be a valid HTTPS URL."],
    "Slug": ["Slug must be between 1 and 100 characters.", "Slug contains invalid characters."]
  }
}
```

### DomainException Hierarchy

Expected errors use a typed exception hierarchy. Each exception carries its HTTP status code and title, enabling automatic mapping to Problem Details.

```csharp
// Base class
public abstract class DomainException : Exception
{
    public int StatusCode { get; }
    public string Title { get; }
    public string ErrorType { get; }

    protected DomainException(int statusCode, string title, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Title = title;
        ErrorType = $"https://api.short.io/errors/{title.ToLowerInvariant().Replace(" ", "-")}";
    }
}

// Concrete exceptions
public class NotFoundException : DomainException
{
    public NotFoundException(string entityType, string id)
        : base(404, "Not Found", $"{entityType} with ID '{id}' was not found.") { }
}

public class ConflictException : DomainException
{
    public ConflictException(string message)
        : base(409, "Conflict", message) { }
}

public class ForbiddenException : DomainException
{
    public ForbiddenException(string message)
        : base(403, "Forbidden", message) { }
}

public class BadRequestException : DomainException
{
    public BadRequestException(string message)
        : base(400, "Bad Request", message) { }
}

public class UnauthorizedException : DomainException
{
    public UnauthorizedException(string message)
        : base(401, "Unauthorized", message) { }
}

public class TooManyRequestsException : DomainException
{
    public TooManyRequestsException(int retryAfterSeconds)
        : base(429, "Too Many Requests", $"Rate limit exceeded. Retry after {retryAfterSeconds} seconds.") { }
}
```

### Global Exception Middleware

A single middleware catches all exceptions and converts them to Problem Details:

```csharp
public class DomainExceptionMiddleware
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
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex, "Domain exception: {StatusCode} {Title}", ex.StatusCode, ex.Title);
            context.Response.StatusCode = ex.StatusCode;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Type = ex.ErrorType,
                Title = ex.Title,
                Status = ex.StatusCode,
                Detail = ex.Message,
                Instance = context.Request.Path
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Type = "https://api.short.io/errors/internal-error",
                Title = "Internal Server Error",
                Status = 500,
                Detail = "An unexpected error occurred. Please try again later.",
                Instance = context.Request.Path
            });
        }
    }
}
```

### Result Pattern for Domain Logic

Within the domain layer (below the API), expected errors use the **Result pattern** rather than exceptions for control flow. This keeps the domain pure and testable without exception-driven control flow.

```csharp
public record Result<T>
{
    public T? Value { get; }
    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    public bool IsFailure => !IsSuccess;

    private Result(T? value, Error? error) { Value = value; Error = error; }
    public static Result<T> Success(T value) => new(value, null);
    public static Result<T> Failure(Error error) => new(default, error);
}

public record Error(string Code, string Message);
```

Domain services return `Result<T>`. Application services (endpoint handlers or command handlers) map `Result<T>.Failure` to `DomainException` for the middleware to handle:

```csharp
// Domain layer — uses Result
public static Result<Link> CreateLink(string slug, string destinationUrl, ...)
{
    if (string.IsNullOrWhiteSpace(slug))
        return Result<Link>.Failure(new Error("E300", "Slug cannot be empty."));
    // ...
    return Result<Link>.Success(new Link { ... });
}

// API layer — maps Result to DomainException
var result = Link.Create(request.Slug, request.DestinationUrl, ...);
if (result.IsFailure)
    throw new BadRequestException(result.Error.Message);
```

For the full Result pattern, see [.claude/csharp/error-modeling.md](../../.claude/csharp/error-modeling.md).

## Constraints

- **Content type:** Problem Details responses must use `Content-Type: application/problem+json` to distinguish them from successful responses (`application/json`). This enables clients to detect error responses by content type.
- **No stack traces in production:** The `detail` field for 500 errors must be a generic message, never the exception's `.Message` or `.StackTrace`. Internal details are logged to CloudWatch, not exposed to clients.
- **Consistent `type` URIs:** Error type URIs must follow a consistent pattern (`https://api.short.io/errors/{kebab-case-title}`) so that clients can programmatically switch on error types.
- **Validation errors must reference fields:** Validation failure responses must include the `errors` extension mapping field names to arrays of error messages. This enables clients to highlight specific form fields.

## Decisions

1. **RFC 7807 over custom JSON envelope** — RFC 7807 is an IETF standard with well-defined semantics. It is supported by ASP.NET Core's `ProblemDetails` class natively. Custom error envelopes require clients to learn a bespoke format for every API. The `application/problem+json` content type enables middleware and proxies to detect error responses without parsing the body.

2. **DomainException hierarchy over result types at the API boundary** — Exceptions thrown by policy services and domain logic are caught by middleware and converted automatically. This eliminates error-handling boilerplate in every endpoint handler. Endpoint handlers do not have try-catch blocks — they call policy services and domain logic, and the middleware handles errors. See [.claude/csharp/minimal-api.md](../../.claude/csharp/minimal-api.md#policy-services-pattern).

3. **Result pattern for the domain, exceptions for the API** — The domain layer uses the Result pattern for explicit error handling (no exceptions for control flow). The API boundary maps Results to DomainExceptions. This clean separation means domain logic is testable with pure Result assertions, and API error formatting is centralized in middleware.

4. **`type` URI references documentation** — Error type URIs like `https://api.short.io/errors/not-found` are currently placeholder URLs. In the future, they will resolve to documentation pages explaining the error type and common remediation steps. For now, they serve as unique, stable identifiers for client error handling.

5. **W3C trace context in errors** — Every error response includes a `traceId` (W3C Trace Context format). This enables correlation between client-reported errors and server-side logs. Distributed tracing headers are propagated through the Lambda → DynamoDB → EventBridge chain.

## Trade-offs

| Trade-off | Detail |
|---|---|
| **Exception overhead** | Using exceptions for expected errors (DomainException) has CPU overhead from stack unwinding. For the management API, this is negligible — error rates are low. For the redirect hot path, errors are extremely rare (lookup misses return a 404 page, not a JSON Problem Details response). |
| **Middleware magic** | Centralized error handling via middleware means developers must understand that throwing a `NotFoundException` automatically produces a 404 Problem Details response. This is "magic" in the sense that it's not visible in the endpoint handler. Mitigation: the policy service pattern makes this explicit — endpoint handlers call `bookPolicy.RequireBookExistsAsync()`, which documents that a `NotFoundException` is the failure mode. |
| **Error type URL stability** | Error type URIs like `https://api.short.io/errors/not-found` must remain stable even if the documentation site moves. If the domain changes, old error types break. Mitigation: error types are short, semantic paths that can be redirect-maintained. The domain `api.short.io` is the API's canonical domain. |
| **Result pattern boilerplate** | The Result pattern adds boilerplate (checking `IsSuccess`, propagating errors). Mitigation: the `Bind`/`Map` extension methods (railway-oriented programming) reduce this to chained calls. See [.claude/csharp/error-modeling.md](../../.claude/csharp/error-modeling.md). |

## Evolution

- If error type documentation pages are needed, stand up a simple static site at `https://api.short.io/errors/` that renders each error type's documentation from a template.
- If specific error types need custom extensions (e.g., rate limit errors include a `retryAfter` field), add extension properties to the Problem Details response through custom middleware.
- If the Result pattern boilerplate becomes burdensome, evaluate a source generator that creates the `Bind`/`Map` chains automatically — or adopt a library like `OneOf` for discriminated unions instead of the custom Result type.

## Related ADRs

- [ADR-004: C# for Backend Services](ADR-004-csharp-backend.md) — DomainException and Result pattern are implemented in C#.
- [ADR-008: API Versioning Strategy](ADR-008-api-versioning-strategy.md) — Error format is consistent across all API versions.
- [ADR-009: Authentication Scheme (JWT + API Keys)](ADR-009-authentication-jwt-api-keys.md) — Auth failures (401, 403) use Problem Details format.
- [ADR-010: DynamoDB Single-Table Design](ADR-010-dynamodb-single-table-design.md) — DynamoDB conditional check failures map to `ConflictException`.
