/**
 * Shared configuration for all k6 load test scenarios.
 *
 * Every value is configurable via environment variables so the same script
 * works against local dev, staging, and production targets without edits.

 *
 *   k6 run -e REDIRECT_BASE_URL=https://r.acme.short.io \
 *           -e TARGET_RPS=38500 \
 *           scenarios/redirect-load.js
 */

// ── Target endpoints ──────────────────────────────────────────────────────
// REDIRECT_BASE_URL: the CloudFront distribution or Lambda URL that serves
//   short-link redirects.  The Host header is set per-request to the correct
//   tenant domain, so this can be the CloudFront domain even in staging.
export const REDIRECT_BASE_URL =
  __ENV.REDIRECT_BASE_URL || 'http://localhost:3000';

// CONTROL_PLANE_BASE_URL: the ControlPlane API origin.
export const CONTROL_PLANE_BASE_URL =
  __ENV.CONTROL_PLANE_BASE_URL || 'http://localhost:5000';

// ── Authentication ────────────────────────────────────────────────────────
// CONTROL_PLANE_API_KEY: value sent in the X-API-Key request header.
export const CONTROL_PLANE_API_KEY =
  __ENV.CONTROL_PLANE_API_KEY || '';

// TENANT_ID: UUID of the test tenant used for control-plane operations.
export const TENANT_ID =
  __ENV.TENANT_ID || '00000000-0000-0000-0000-000000000001';

// DOMAIN_ID: UUID of a verified domain belonging to TENANT_ID.
export const DOMAIN_ID =
  __ENV.DOMAIN_ID || '00000000-0000-0000-0000-000000000002';

// ── Throughput ────────────────────────────────────────────────────────────
// TARGET_RPS: desired arrival rate for the redirect load/spike/stress tests.
//
// Scale context:
//   1B clicks/month ÷ 2,592,000 s/month ≈ 386 req/s average.
//   Viral bursts can be 100× the average → ~38,500 req/s peak capacity.
//
// Default is 500 req/s — safe for local development and staging environments.
// Set to 38500 for full-scale production validation.
export const TARGET_RPS = parseInt(__ENV.TARGET_RPS || '500', 10);

// ── Redirect test data ────────────────────────────────────────────────────
// Short links that MUST already exist in the target DynamoDB table before
// running redirect load tests.  Use the control-plane API or the seed script
// in test/LoadTests/scripts/ to populate them.
//
// REDIRECT_TEST_HOSTS: comma-separated hostnames (custom short domains).
export const TEST_HOSTS = (
  __ENV.REDIRECT_TEST_HOSTS || 'test.short.io'
).split(',').map((h) => h.trim());

// REDIRECT_TEST_SLUGS: comma-separated slugs to request during the test.
// Use enough unique slugs to exceed the in-memory cache (default 1 000) to
// exercise both the hot-path cache and the DynamoDB cold path.
export const TEST_SLUGS = (
  __ENV.REDIRECT_TEST_SLUGS ||
    'abc123,def456,ghi789,jkl012,mno345,pqr678,stu901,vwx234,yz0567,aa1890'
).split(',').map((s) => s.trim());

// ── Latency thresholds ────────────────────────────────────────────────────
// Redirect SLOs (per ADR-002): DynamoDB single-digit ms + Lambda ~10 ms +
// CloudFront edge ≤ 40 ms → p99 target ≤ 100 ms end-to-end.
export const REDIRECT_THRESHOLDS = {
  http_req_duration: ['p(99)<100', 'p(95)<50', 'avg<30'],
  http_req_failed: ['rate<0.001'],
  checks: ['rate>0.999'],
};

// Control-plane SLOs: in-memory repos are fast; allow headroom for auth and
// business-logic overhead.
export const CONTROL_PLANE_THRESHOLDS = {
  http_req_duration: ['p(99)<500', 'p(95)<200', 'avg<100'],
  http_req_failed: ['rate<0.01'],
  checks: ['rate>0.99'],
};

// Smoke test uses relaxed thresholds — purpose is connectivity, not SLO.
export const SMOKE_THRESHOLDS = {
  http_req_duration: ['p(99)<2000', 'p(95)<1000'],
  http_req_failed: ['rate<0.05'],
  checks: ['rate>0.90'],
};
