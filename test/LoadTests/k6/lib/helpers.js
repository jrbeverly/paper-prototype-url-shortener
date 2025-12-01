/**
 * Shared utilities for k6 load test scenarios.
 *
 * These helpers are plain k6 ES6 modules — no Node.js APIs, no npm packages.
 */

import { check } from 'k6';
import { TEST_HOSTS, TEST_SLUGS } from './config.js';

// ── Random helpers ────────────────────────────────────────────────────────

export function randomItem(arr) {
  return arr[Math.floor(Math.random() * arr.length)];
}

export function randomString(length = 8) {
  const chars = 'abcdefghijklmnopqrstuvwxyz0123456789';
  return Array.from(
    { length },
    () => chars[Math.floor(Math.random() * chars.length)]
  ).join('');
}

export function randomInt(min, max) {
  return Math.floor(Math.random() * (max - min + 1)) + min;
}

// ── Redirect helpers ──────────────────────────────────────────────────────

/** Returns a random {host, slug} pair from the configured test data. */
export function randomRedirectTarget() {
  return {
    host: randomItem(TEST_HOSTS),
    slug: randomItem(TEST_SLUGS),
  };
}

/**
 * Asserts that the response is a valid HTTP redirect (3xx with Location).
 * Returns true if all checks pass.
 */
export function checkRedirect(res) {
  return check(res, {
    'status is 3xx': (r) => r.status >= 300 && r.status < 400,
    'Location header present': (r) =>
      r.headers['Location'] !== undefined && r.headers['Location'] !== '',
    'latency < 100ms': (r) => r.timings.duration < 100,
  });
}

/**
 * More lenient check — accepts 3xx or 404 (slug not found).
 * Use in smoke tests where test data may not be pre-seeded.
 */
export function checkRedirectOrNotFound(res) {
  return check(res, {
    'status is 3xx or 404': (r) =>
      (r.status >= 300 && r.status < 400) || r.status === 404,
    'no server error (5xx)': (r) => r.status < 500,
    'latency < 2000ms': (r) => r.timings.duration < 2000,
  });
}

// ── Control-plane helpers ─────────────────────────────────────────────────

/** Checks that the response is a successful API response. */
export function checkApiSuccess(res, expectedStatus = 200) {
  return check(res, {
    [`status is ${expectedStatus}`]: (r) => r.status === expectedStatus,
    'has JSON body': (r) => {
      try {
        JSON.parse(r.body);
        return true;
      } catch {
        return false;
      }
    },
    'latency < 500ms': (r) => r.timings.duration < 500,
  });
}

/** Returns common JSON headers including the API key. */
export function apiHeaders(apiKey) {
  return {
    'Content-Type': 'application/json',
    Accept: 'application/json',
    'X-API-Key': apiKey,
  };
}

/** Builds a random CreateLinkRequest body. */
export function randomCreateLinkBody(domainId) {
  return JSON.stringify({
    domainId,
    destinationUrl: `https://example.com/load-test/${randomString(12)}`,
    redirectType: '302',
  });
}

// ── Summary helpers ───────────────────────────────────────────────────────

/**
 * Extracts a compact summary from the k6 handleSummary data object.
 *
 * @param {object} data - The object passed to k6's handleSummary function.
 * @param {string} scenarioName - Human-readable scenario identifier.
 * @param {object} metadata - Arbitrary key/value pairs recorded alongside metrics.
 * @returns {object} Compact summary suitable for appending to history.json.
 */
export function buildSummaryData(data, scenarioName, metadata = {}) {
  const m = data.metrics;

  const val = (metricName, key) => m[metricName]?.values?.[key] ?? null;

  const allThresholdsPassed = Object.values(m).every((metric) => {
    if (!metric.thresholds) return true;
    return Object.values(metric.thresholds).every((t) => t.ok);
  });

  return {
    scenario: scenarioName,
    timestamp: new Date().toISOString(),
    passed: allThresholdsPassed,
    metadata,
    metrics: {
      p50:               val('http_req_duration', 'p(50)'),
      p95:               val('http_req_duration', 'p(95)'),
      p99:               val('http_req_duration', 'p(99)'),
      avg:               val('http_req_duration', 'avg'),
      min:               val('http_req_duration', 'min'),
      max:               val('http_req_duration', 'max'),
      errorRate:         val('http_req_failed', 'rate'),
      requestsPerSecond: val('http_reqs', 'rate'),
      totalRequests:     val('http_reqs', 'count'),
    },
  };
}

/**
 * Formats a compact text summary suitable for stdout.
 */
export function formatSummaryText(summary) {
  const m = summary.metrics;
  const fmt = (v) => (v !== null ? v.toFixed(1) : 'n/a');
  const pct = (v) => (v !== null ? (v * 100).toFixed(3) + '%' : 'n/a');

  return [
    '',
    `=== ${summary.scenario} ===`,
    `  Timestamp : ${summary.timestamp}`,
    `  PASSED    : ${summary.passed}`,
    `  p50       : ${fmt(m.p50)} ms`,
    `  p95       : ${fmt(m.p95)} ms`,
    `  p99       : ${fmt(m.p99)} ms`,
    `  avg       : ${fmt(m.avg)} ms`,
    `  max       : ${fmt(m.max)} ms`,
    `  error rate: ${pct(m.errorRate)}`,
    `  RPS       : ${m.requestsPerSecond !== null ? m.requestsPerSecond.toFixed(0) : 'n/a'}`,
    `  requests  : ${m.totalRequests ?? 'n/a'}`,
    '',
  ].join('\n');
}
