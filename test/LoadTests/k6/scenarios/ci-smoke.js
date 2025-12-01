/**
 * CI Smoke Test — Connectivity and basic SLO validation
 *
 * Runs in ~90 seconds and verifies:
 *   - Redirect service responds (3xx or 404 — accepts slug-not-found)
 *   - Control-plane health endpoint returns 200
 *   - No 5xx errors under light concurrent load
 *
 * This is the ONLY scenario that runs in CI (on schedule, not per-PR).
 * It is intentionally lenient; full throughput validation requires a
 * deployed environment with real test data (see redirect-load.js).
 *
 * Usage:
 *   k6 run test/LoadTests/k6/scenarios/ci-smoke.js
 *   k6 run -e REDIRECT_BASE_URL=https://staging.short.io \
 *           -e CONTROL_PLANE_BASE_URL=https://api.staging.short.io \
 *           test/LoadTests/k6/scenarios/ci-smoke.js
 */

import http from 'k6/http';
import { sleep } from 'k6';
import {
  REDIRECT_BASE_URL,
  CONTROL_PLANE_BASE_URL,
  CONTROL_PLANE_API_KEY,
  TEST_HOSTS,
  TEST_SLUGS,
  SMOKE_THRESHOLDS,
} from '../lib/config.js';
import {
  randomItem,
  checkRedirectOrNotFound,
  checkApiSuccess,
  apiHeaders,
  buildSummaryData,
  formatSummaryText,
} from '../lib/helpers.js';

export const options = {
  scenarios: {
    redirect_smoke: {
      executor: 'constant-vus',
      vus: 10,
      duration: '60s',
      exec: 'redirectScenario',
      tags: { scenario: 'redirect-smoke' },
    },
    health_smoke: {
      executor: 'constant-vus',
      vus: 3,
      duration: '60s',
      exec: 'healthScenario',
      tags: { scenario: 'health-smoke' },
    },
  },
  thresholds: {
    'http_req_duration{scenario:redirect-smoke}': ['p(99)<2000', 'p(95)<1000'],
    'http_req_duration{scenario:health-smoke}':   ['p(99)<500',  'p(95)<200'],
    'http_req_failed{scenario:redirect-smoke}':   ['rate<0.05'],
    'http_req_failed{scenario:health-smoke}':     ['rate<0.01'],
    checks: ['rate>0.90'],
  },
};

export function redirectScenario() {
  const host = randomItem(TEST_HOSTS);
  const slug = randomItem(TEST_SLUGS);

  const res = http.get(`${REDIRECT_BASE_URL}/${slug}`, {
    headers: { Host: host },
    redirects: 0,
    timeout: '5s',
    tags: { name: 'redirect' },
  });

  checkRedirectOrNotFound(res);
  sleep(0.1);
}

export function healthScenario() {
  const res = http.get(`${CONTROL_PLANE_BASE_URL}/health`, {
    headers: apiHeaders(CONTROL_PLANE_API_KEY),
    timeout: '5s',
    tags: { name: 'health' },
  });

  // Accept 200 (healthy) or 503 (degraded but responding).
  // A 5xx other than 503 would indicate a configuration problem.
  checkApiSuccess(res, res.status === 503 ? 503 : 200);
  sleep(0.2);
}

export function handleSummary(data) {
  const summary = buildSummaryData(data, 'ci-smoke', {
    redirectBaseUrl: REDIRECT_BASE_URL,
    controlPlaneBaseUrl: CONTROL_PLANE_BASE_URL,
  });

  return {
    'test/LoadTests/results/latest-ci-smoke.json':
      JSON.stringify(summary, null, 2),
    stdout: formatSummaryText(summary),
  };
}
