/**
 * Control-Plane Load Test — Concurrent API Operations
 *
 * Validates the ControlPlane API under sustained concurrent usage:
 *   - Link creation  (POST /tenants/{id}/links)
 *   - Link listing   (GET  /tenants/{id}/links)
 *   - Link retrieval (GET  /tenants/{id}/links/{linkId})
 *   - Link update    (PATCH /tenants/{id}/links/{linkId})
 *   - Link deletion  (DELETE /tenants/{id}/links/{linkId})
 *
 * The test simulates 50 concurrent "users" each performing a random mix
 * of read and write operations, matching expected production API usage.
 *
 * Prerequisites:
 *   - CONTROL_PLANE_BASE_URL points to a running ControlPlane instance.
 *   - CONTROL_PLANE_API_KEY is a valid API key for TENANT_ID.
 *   - DOMAIN_ID is the UUID of a verified domain belonging to TENANT_ID.
 *
 * Usage:
 *   k6 run \
 *     -e CONTROL_PLANE_BASE_URL=http://localhost:5000 \
 *     -e CONTROL_PLANE_API_KEY=sk_test_abc123 \
 *     -e TENANT_ID=00000000-0000-0000-0000-000000000001 \
 *     -e DOMAIN_ID=00000000-0000-0000-0000-000000000002 \
 *     test/LoadTests/k6/scenarios/control-plane-load.js
 *
 * Pass/fail criteria:
 *   p99 < 500 ms, p95 < 200 ms, error rate < 1%
 */

import http from 'k6/http';
import { check, sleep } from 'k6';
import {
  CONTROL_PLANE_BASE_URL,
  CONTROL_PLANE_API_KEY,
  TENANT_ID,
  DOMAIN_ID,
  CONTROL_PLANE_THRESHOLDS,
} from '../lib/config.js';
import {
  randomString,
  checkApiSuccess,
  apiHeaders,
  randomCreateLinkBody,
  buildSummaryData,
  formatSummaryText,
} from '../lib/helpers.js';

const BASE = `${CONTROL_PLANE_BASE_URL}/api/v1/tenants/${TENANT_ID}`;
const HEADERS = () => apiHeaders(CONTROL_PLANE_API_KEY);

export const options = {
  scenarios: {
    mixed_write_load: {
      executor: 'constant-vus',
      vus:      30,
      duration: '10m',
      exec:     'writeScenario',
      tags:     { scenario: 'write' },
    },
    mixed_read_load: {
      executor: 'constant-vus',
      vus:      20,
      duration: '10m',
      exec:     'readScenario',
      tags:     { scenario: 'read' },
    },
  },
  thresholds: {
    ...CONTROL_PLANE_THRESHOLDS,
    'http_req_duration{scenario:write}': ['p(99)<500', 'p(95)<200'],
    'http_req_duration{scenario:read}':  ['p(99)<300', 'p(95)<100'],
  },
  userAgent: 'k6-control-plane-load/1.0',
};

/**
 * Write scenario: create → update → delete lifecycle.
 * Each VU creates a link, updates it, then deletes it.
 */
export function writeScenario() {
  // Create
  const createRes = http.post(
    `${BASE}/links`,
    randomCreateLinkBody(DOMAIN_ID),
    { headers: HEADERS(), timeout: '10s', tags: { name: 'link:create' } }
  );

  const created = checkApiSuccess(createRes, 201);
  if (!created) {
    sleep(1);
    return;
  }

  let linkId;
  try {
    linkId = JSON.parse(createRes.body).id;
  } catch {
    sleep(1);
    return;
  }

  sleep(0.1);

  // Update
  const updateRes = http.patch(
    `${BASE}/links/${linkId}`,
    JSON.stringify({
      destinationUrl: `https://example.com/updated/${randomString(8)}`,
    }),
    { headers: HEADERS(), timeout: '10s', tags: { name: 'link:update' } }
  );
  checkApiSuccess(updateRes, 200);

  sleep(0.1);

  // Delete
  const deleteRes = http.del(
    `${BASE}/links/${linkId}`,
    null,
    { headers: HEADERS(), timeout: '10s', tags: { name: 'link:delete' } }
  );
  check(deleteRes, {
    'delete returns 204': (r) => r.status === 204,
    'delete latency < 200ms': (r) => r.timings.duration < 200,
  });

  sleep(0.5);
}

/**
 * Read scenario: list links, then fetch one by ID.
 */
export function readScenario() {
  // List
  const listRes = http.get(
    `${BASE}/links?limit=20`,
    { headers: HEADERS(), timeout: '10s', tags: { name: 'link:list' } }
  );

  const listed = checkApiSuccess(listRes, 200);
  if (!listed) {
    sleep(1);
    return;
  }

  let items;
  try {
    items = JSON.parse(listRes.body).items ?? [];
  } catch {
    sleep(1);
    return;
  }

  // Get one by ID if list is not empty
  if (items.length > 0) {
    const linkId = items[Math.floor(Math.random() * items.length)].id;
    const getRes = http.get(
      `${BASE}/links/${linkId}`,
      { headers: HEADERS(), timeout: '10s', tags: { name: 'link:get' } }
    );
    checkApiSuccess(getRes, 200);
  }

  sleep(0.3);
}

export function handleSummary(data) {
  const summary = buildSummaryData(data, 'control-plane-load', {
    writeVUs:         30,
    readVUs:          20,
    durationMins:     10,
    baseUrl:          CONTROL_PLANE_BASE_URL,
    tenantId:         TENANT_ID,
  });

  return {
    'test/LoadTests/results/latest-control-plane-load.json':
      JSON.stringify(summary, null, 2),
    stdout: formatSummaryText(summary),
  };
}
