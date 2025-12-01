# Rate Limiting

The API enforces rate limits to ensure fair use and protect platform availability.

## Current limits

| Endpoint | Limit | Window |
|----------|-------|--------|
| `POST /domains/{domainId}/verify` | 1 request | 60 seconds per domain |
| All other endpoints | Enforced — see headers | Per API key |

> **Note:** General rate limits are applied per API key. The exact limits are returned in response headers (see below). Contact support if your use case requires higher limits.

## Rate limit headers

Every API response includes headers that describe your current rate limit status:

| Header | Description |
|--------|-------------|
| `X-RateLimit-Limit` | Maximum requests allowed in the current window |
| `X-RateLimit-Remaining` | Requests remaining in the current window |
| `X-RateLimit-Reset` | Unix timestamp (UTC) when the window resets |
| `Retry-After` | Seconds to wait before retrying (only present on `429` responses) |

**Example response headers:**

```http
X-RateLimit-Limit: 1000
X-RateLimit-Remaining: 847
X-RateLimit-Reset: 1730462400
```

## When you hit a rate limit

When a request exceeds the limit, the API returns `429 Too Many Requests` with a Problem Details body:

```http
HTTP/1.1 429 Too Many Requests
Content-Type: application/problem+json
Retry-After: 60

{
  "type": "https://api.short.io/errors/too-many-requests",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "Rate limit exceeded. Try again in 60 seconds.",
  "retryAfter": 60
}
```

## Domain verification

Domain verification has a separate, strict rate limit: **one verification attempt per domain per 60 seconds**.

This limit exists because each verification triggers live DNS lookups, which are expensive. If you hit it, wait for `retryAfter` seconds before retrying.

```bash
# If you receive 429 on verify, wait retryAfter seconds
curl -X POST https://api.short.io/api/v1/tenants/{tenantId}/domains/{domainId}/verify \
  -H "X-API-Key: sk_your_key_here"

# {"retryAfter": 60, ...}  →  wait 60 seconds then retry
```

## Best practices

### Respect `Retry-After`

When you receive a `429`, always read the `retryAfter` field and wait that many seconds before retrying. Do not immediately retry — it will result in another `429`.

### Exponential back-off

For general-purpose retry logic, use exponential back-off with jitter:

```python
import time, random

def call_with_retry(fn, max_retries=5):
    for attempt in range(max_retries):
        response = fn()
        if response.status_code == 429:
            retry_after = response.json().get("retryAfter", 2 ** attempt)
            jitter = random.uniform(0, 1)
            time.sleep(retry_after + jitter)
            continue
        return response
    raise Exception("Max retries exceeded")
```

```javascript
async function callWithRetry(fn, maxRetries = 5) {
  for (let attempt = 0; attempt < maxRetries; attempt++) {
    const response = await fn();
    if (response.status === 429) {
      const body = await response.json();
      const retryAfter = body.retryAfter ?? 2 ** attempt;
      const jitter = Math.random();
      await new Promise(resolve => setTimeout(resolve, (retryAfter + jitter) * 1000));
      continue;
    }
    return response;
  }
  throw new Error("Max retries exceeded");
}
```

### Bulk operations

For creating large numbers of links, use the [bulk create endpoint](reference/links.md#bulk-create) instead of looping over the single-create endpoint. A single bulk request can contain up to 10,000 links and counts as one API call.

### Caching

Cache responses where possible. For example, the `GET /plans` endpoint returns data that changes infrequently — cache it for several hours rather than fetching it on every page load.

## Increasing your limits

If your use case requires higher rate limits, contact [support@short.io](mailto:support@short.io) with:

- Your tenant ID
- The endpoint(s) being rate-limited
- Expected request volume and pattern
- Use case description
