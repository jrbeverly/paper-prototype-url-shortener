# Error Handling

The API uses [RFC 7807 Problem Details](https://www.rfc-editor.org/rfc/rfc7807) for all error responses. Errors are returned as JSON with the `application/problem+json` content type.

## Error response format

```json
{
  "type": "https://api.short.io/errors/not-found",
  "title": "Not Found",
  "status": 404,
  "detail": "Link 'abc123' was not found.",
  "instance": "/api/v1/tenants/.../links/abc123",
  "traceId": "00-4bf92f3577b34da6..."
}
```

| Field | Type | Description |
|-------|------|-------------|
| `type` | string | URI identifying the error type. Stable — safe to match against programmatically. |
| `title` | string | Short human-readable summary of the error type. |
| `status` | integer | HTTP status code. Same as the response status code. |
| `detail` | string | Human-readable explanation specific to this occurrence. |
| `instance` | string | URI of the resource or endpoint that produced the error. |
| `traceId` | string | Trace ID for correlating with support. Include this when reporting issues. |

Some error types include additional fields:

| Extra field | Error type | Description |
|-------------|-----------|-------------|
| `retryAfter` | `429 Too Many Requests` | Seconds to wait before retrying |
| `errors` | `400 Bad Request` (validation) | Field-level validation errors |

### Validation error example

```json
{
  "type": "https://api.short.io/errors/bad-request",
  "title": "Bad Request",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "errors": {
    "destinationUrl": ["Destination URL must be an absolute HTTP or HTTPS URL."],
    "slug": ["Slug must contain only letters, numbers, and hyphens."]
  }
}
```

---

## HTTP status codes

### Success

| Status | Meaning |
|--------|---------|
| `200 OK` | Request succeeded. Response body contains the resource. |
| `201 Created` | Resource was created. `Location` header points to the new resource. |
| `204 No Content` | Request succeeded. No response body (e.g., delete operations). |
| `302 Found` | Redirect (e.g., invoice PDF download). |

### Client errors

| Status | Error type | Common causes |
|--------|-----------|---------------|
| `400 Bad Request` | `bad-request` | Invalid field value, missing required field, malformed JSON |
| `401 Unauthorized` | `unauthorized` | No API key provided, key is invalid, key is revoked |
| `403 Forbidden` | `forbidden` | Valid key but missing required permission |
| `404 Not Found` | `not-found` | Resource does not exist or belongs to a different tenant |
| `409 Conflict` | `conflict` | Resource already exists (duplicate slug, domain), or operation is invalid for the current state |
| `422 Unprocessable Entity` | `link-limit-exceeded` or `domain-limit-exceeded` | Plan limit reached |
| `429 Too Many Requests` | `too-many-requests` | Rate limit exceeded |

### Server errors

| Status | Meaning |
|--------|---------|
| `500 Internal Server Error` | Unexpected server error. Include the `traceId` when contacting support. |

---

## Common errors and fixes

### `401 Unauthorized`

**Causes:**
- `X-API-Key` header is missing or empty
- The API key has been revoked
- The API key value is malformed

**Fix:** Verify the key is present and matches exactly what was returned on creation. If the key was revoked, [generate a new one](reference/api-keys.md#create-an-api-key).

---

### `403 Forbidden`

**Causes:**
- The API key exists but lacks the required permission
- Accessing a resource that belongs to a different tenant

**Fix:** Check the required permission for the endpoint you are calling (see the relevant [reference page](README.md#api-reference)). [Create a new API key](reference/api-keys.md#create-an-api-key) with the required permissions, or contact your workspace owner to grant access.

---

### `404 Not Found`

**Causes:**
- The resource ID is valid but does not exist
- The resource was soft-deleted
- The resource belongs to a different tenant (the API returns 404, not 403, for cross-tenant lookups to avoid information disclosure)

**Fix:** Verify the ID is correct. If the resource was deleted, check whether it can be [restored](reference/links.md#restore-a-link).

---

### `409 Conflict`

**Causes:**
- Creating a link with a slug that already exists on the domain
- Registering a domain that is already registered
- Suspending a tenant that is already suspended
- Updating a deleted resource

**Detail field example:**

```json
{
  "status": 409,
  "detail": "A link with slug 'summer-sale' already exists on this domain."
}
```

**Fix:** Choose a different slug, check the current resource state, or use the existing resource.

---

### `422 Unprocessable Entity`

**Causes:**
- Your plan's link or domain limit has been reached

**Fix:** Delete unused links or domains, or upgrade your plan. See [Plans](reference/plans.md) for limit details.

---

### `429 Too Many Requests`

**Causes:**
- General API rate limit exceeded
- Domain verification rate limit (1 per 60 seconds)

**Fix:** Wait for `retryAfter` seconds and retry. See [Rate limiting](rate-limiting.md) for back-off strategies.

---

## Handling errors in code

### Python

```python
import requests

def create_link(tenant_id, api_key, domain_id, destination_url, slug=None):
    response = requests.post(
        f"https://api.short.io/api/v1/tenants/{tenant_id}/links",
        headers={"X-API-Key": api_key},
        json={"domainId": domain_id, "destinationUrl": destination_url, "slug": slug},
    )

    if response.ok:
        return response.json()

    error = response.json()
    status = error.get("status")

    if status == 409:
        raise ValueError(f"Slug conflict: {error['detail']}")
    elif status == 422:
        raise ValueError(f"Plan limit reached: {error['detail']}")
    elif status == 401:
        raise PermissionError("Invalid or revoked API key")
    else:
        raise RuntimeError(f"API error {status}: {error.get('detail', 'unknown error')}")
```

### JavaScript

```javascript
async function createLink(tenantId, apiKey, domainId, destinationUrl, slug) {
  const response = await fetch(`https://api.short.io/api/v1/tenants/${tenantId}/links`, {
    method: "POST",
    headers: {
      "X-API-Key": apiKey,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ domainId, destinationUrl, slug }),
  });

  if (response.ok) return response.json();

  const error = await response.json();
  const { status, detail } = error;

  switch (status) {
    case 401: throw new Error("Invalid or revoked API key");
    case 403: throw new Error("Insufficient permissions");
    case 409: throw new Error(`Conflict: ${detail}`);
    case 422: throw new Error(`Plan limit: ${detail}`);
    case 429: {
      const wait = error.retryAfter ?? 60;
      throw new Error(`Rate limited — retry in ${wait}s`);
    }
    default: throw new Error(`API error ${status}: ${detail}`);
  }
}
```

### C\#

```csharp
public record ProblemDetails(
    string Type,
    string Title,
    int Status,
    string Detail,
    int? RetryAfter = null);

public async Task<JsonElement> CreateLinkAsync(string tenantId, string apiKey,
    string domainId, string destinationUrl)
{
    using var client = new HttpClient();
    client.DefaultRequestHeaders.Add("X-API-Key", apiKey);

    var response = await client.PostAsJsonAsync(
        $"https://api.short.io/api/v1/tenants/{tenantId}/links",
        new { domainId, destinationUrl });

    if (response.IsSuccessStatusCode)
        return await response.Content.ReadFromJsonAsync<JsonElement>();

    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
    throw problem!.Status switch
    {
        401 => new UnauthorizedAccessException("Invalid or revoked API key"),
        403 => new UnauthorizedAccessException("Insufficient permissions"),
        409 => new InvalidOperationException($"Conflict: {problem.Detail}"),
        429 => new InvalidOperationException($"Rate limited — retry in {problem.RetryAfter}s"),
        _ => new HttpRequestException($"API error {problem.Status}: {problem.Detail}"),
    };
}
```

---

## Reporting issues

When contacting support about an error, include:

- The `traceId` from the error response
- The endpoint and HTTP method
- The full request body (with sensitive values redacted)
- The full error response body
- The time the request was made (UTC)
