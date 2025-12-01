# Graceful Degradation: Failure Modes & Recovery

## Purpose

Document the failure modes of the redirect hot path and the resilience mechanisms that keep redirects serving when dependent systems fail.

## Architecture Principle

The redirect path depends only on DynamoDB. All other systems (Postgres, Stripe, Analytics/Kinesis, Dashboard UI) are either not in the request path or are called with non-blocking fire-and-forget semantics.

```
Client → CloudFront → API Gateway → Lambda (RedirectFunction)
                                        ├── DynamoDB (GetItem + UpdateItem)
                                        └── Kinesis (fire-and-forget click event)
```

---

## Failure Mode Matrix

| Dependency | Failure Mode | Redirect Impact | Recovery |
|---|---|---|---|
| **DynamoDB** | Timeout / throttling / unavailable | Circuit breaker opens after 3 consecutive failures → cache fallback | Auto-recovery: circuit half-opens after 30s, closes on first successful call |
| **Kinesis (Analytics)** | Timeout / stream unavailable | None (fire-and-forget). Circuit breaker opens after 3 failures → skipped | Auto-recovery: circuit half-opens after 60s |
| **Postgres (ControlPlane)** | Unreachable | None. The redirect path does not query Postgres | N/A |
| **Stripe** | API errors | None. Stripe is only used by the ControlPlane for billing; the redirect path has no Stripe dependency | N/A |
| **Dashboard / Frontend** | Unavailable | None. The redirect path is independent of any frontend | N/A |

---

## Resilience Components

### 1. Circuit Breaker (`CircuitBreaker`)

**States:** Closed → Open → HalfOpen → Closed (or Open)

| State | Behavior |
|---|---|
| **Closed** | All calls pass through to the dependency. Failures are counted. |
| **Open** | Calls fail fast (DynamoDB) or are silently skipped (Analytics). No dependency invocation. |
| **HalfOpen** | One probe request is allowed through. If it succeeds → Closed. If it fails → Open. |

| Circuit | Threshold | Cooldown | Fallback |
|---|---|---|---|
| DynamoDB | 3 consecutive failures | 30s | Hot-link cache |
| Analytics | 3 consecutive failures | 60s | Silent skip |

### 2. Hot-Link Cache (`HotLinkCache`)

- In-memory `ConcurrentDictionary` keyed by `"{hostname}#{slug}"`
- TTL: 5 minutes per entry
- Max entries: 1000 (new writes rejected at capacity)
- Populated on every successful DynamoDB lookup
- Read on circuit-open: returns the last known record for that link

### 3. Timeouts

- **DynamoDB GetItemAsync:** 500ms per call (via `ResilientRedirectRepository`)
- **DynamoDB UpdateItemAsync:** 500ms per call
- **Overall resolve:** 1500ms safety net (via `CancellationTokenSource` in handler)

### 4. Analytics Isolation

The `KinesisClickEventEmitter` uses fire-and-forget (`_ = PutRecordAsync(...)`) so the redirect response is never delayed by analytics. The `ResilientClickEventEmitter` decorator adds a circuit breaker so that after 3 consecutive emission failures, the emission is silently skipped for 60s — avoiding wasted CPU and network calls to a degraded Kinesis.

---

## Health Check Endpoint (`GET /health`)

Reports degradation state as JSON:

```json
{
  "status": "healthy|degraded|unhealthy",
  "version": "1.0.0",
  "dependencies": [
    {
      "name": "dynamodb",
      "status": "healthy|degraded|unhealthy",
      "latencyMs": 12,
      "error": null
    }
  ],
  "circuitBreakers": {
    "dynamodb": "closed|open|halfOpen",
    "analytics": "closed|open|halfOpen"
  },
  "cache": {
    "hotLinkEntries": 42
  }
}
```

- **Unhealthy** returned as HTTP 503 when DynamoDB health check fails 3+ consecutive times
- **Degraded** returned as HTTP 200 when DynamoDB health check fails but hasn't hit the unhealthy threshold
- Circuit breaker state, cache entries, and analytics circuit state are always reported

---

## Recovery Scenarios

### DynamoDB recovers after transient failure

1. Circuit is Open (serving from cache, skipping increments)
2. After 30s cooldown, circuit transitions to HalfOpen
3. Next request probes DynamoDB
4. If successful → circuit closes, normal operation resumes
5. If failed → circuit reopens, cooldown restarts

### Lambda cold start with degraded DynamoDB

1. New container starts with empty hot-link cache
2. First request hits DynamoDB, which fails
3. Circuit opens after 3 failures (3 requests)
4. Cache is empty → redirects return errors until cache populates
5. Mitigation: Lambda provisioned concurrency keeps warm containers with populated caches

### Kinesis recovers

1. Analytics circuit is Open (emissions silently skipped)
2. After 60s cooldown → HalfOpen → probe
3. If successful → circuit closes, emissions resume
4. No data loss for individual click events (they are ephemeral analytics, not critical state)

---

## Postgres Independence Verification

The RedirectService has no reference to:
- `Npgsql` or any PostgreSQL driver
- `Aurora` RDS Data API
- Any ControlPlane service or repository
- The `ControlPlane.Api` project

The redirect path resolves links using only:
- `IRedirectRepository` (DynamoDB-backed)
- `IClickEventEmitter` (Kinesis, fire-and-forget)

Postgres downtime (planned or unplanned) has zero effect on redirects.

---

## Configuration

All resilience parameters are hardcoded with conservative defaults suitable for the redirect hot path. If tuning is needed:

| Parameter | Default | Environment Variable (future) |
|---|---|---|
| DynamoDB circuit threshold | 3 failures | `RESILIENCE_DYNAMO_THRESHOLD` |
| DynamoDB circuit cooldown | 30s | `RESILIENCE_DYNAMO_COOLDOWN_SEC` |
| DynamoDB per-call timeout | 500ms | `RESILIENCE_DYNAMO_TIMEOUT_MS` |
| Analytics circuit threshold | 3 failures | `RESILIENCE_ANALYTICS_THRESHOLD` |
| Analytics circuit cooldown | 60s | `RESILIENCE_ANALYTICS_COOLDOWN_SEC` |
| Cache TTL | 5 min | `RESILIENCE_CACHE_TTL_MIN` |
| Cache max entries | 1000 | `RESILIENCE_CACHE_MAX_ENTRIES` |
| Resolve timeout | 1500ms | `RESILIENCE_RESOLVE_TIMEOUT_MS` |
