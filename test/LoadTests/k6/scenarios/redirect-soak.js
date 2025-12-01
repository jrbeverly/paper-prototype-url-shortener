/**
 * Redirect Soak Test — 1-Hour Sustained Load
 *
 * Detects resource exhaustion, memory leaks, connection pool degradation,
 * and DynamoDB circuit-breaker instability that only manifest over time.
 *
 * Traffic shape:
 *   0–5 min   : Ramp from 0 → TARGET_RPS
 *   5–65 min  : Sustained at TARGET_RPS (60-minute soak)
 *   65–70 min : Ramp down to 0
 *
 * What to watch for in the results:
 *   - Steadily increasing p99 (memory pressure / connection leak)
 *   - Error rate that rises after 20–30 min (thread exhaustion)
 *   - Latency spikes correlating with GC pauses
 *   - DynamoDB throttle errors after sustained load
 *
 * Pass/fail criteria:
 *   p99 < 100 ms, p95 < 50 ms, error rate < 0.1% — same as load test.
 *   The soak test is considered FAILED if p99 at t=60 min is >20% worse
 *   than p99 at t=10 min (latency drift detection).
 *
 * Usage:
 *   k6 run -e TARGET_RPS=500 \
 *           -e REDIRECT_BASE_URL=https://r.staging.short.io \
 *           --out json=test/LoadTests/results/soak-$(date +%s).json \
 *           test/LoadTests/k6/scenarios/redirect-soak.js
 *
 * Note: Duration is ~70 minutes.  Allocate sufficient time in the CI
 * pipeline or run this test asynchronously in a dedicated job.
 */

import http from 'k6/http';
import { Trend } from 'k6/metrics';
import {
  REDIRECT_BASE_URL,
  TARGET_RPS,
  TEST_HOSTS,
  TEST_SLUGS,
  REDIRECT_THRESHOLDS,
} from '../lib/config.js';
import {
  randomItem,
  checkRedirect,
  buildSummaryData,
  formatSummaryText,
} from '../lib/helpers.js';

const redirectLatency = new Trend('redirect_latency_ms', true);

const preAlloc = Math.max(20, Math.ceil(TARGET_RPS * 0.1));
const maxVUs   = Math.max(100, Math.ceil(TARGET_RPS * 0.3));

export const options = {
  scenarios: {
    soak_test: {
      executor: 'ramping-arrival-rate',
      startRate: 0,
      timeUnit:  '1s',
      stages: [
        { duration: '5m',  target: TARGET_RPS }, // ramp up
        { duration: '60m', target: TARGET_RPS }, // 60-minute soak
        { duration: '5m',  target: 0           }, // cool down
      ],
      preAllocatedVUs: preAlloc,
      maxVUs:          maxVUs,
      gracefulStop:    '60s',
    },
  },
  thresholds: {
    ...REDIRECT_THRESHOLDS,
    redirect_latency_ms: ['p(99)<100', 'p(95)<50'],
  },
  noConnectionReuse: false,
  userAgent:         'k6-redirect-soak/1.0',
};

export default function () {
  const host = randomItem(TEST_HOSTS);
  const slug = randomItem(TEST_SLUGS);

  const res = http.get(`${REDIRECT_BASE_URL}/${slug}`, {
    headers:   { Host: host },
    redirects: 0,
    timeout:   '3s',
    tags:      { name: 'redirect', host },
  });

  redirectLatency.add(res.timings.duration);
  checkRedirect(res);
}

export function handleSummary(data) {
  const summary = buildSummaryData(data, 'redirect-soak', {
    targetRps:    TARGET_RPS,
    durationMins: 70,
    hosts:        TEST_HOSTS,
    slugCount:    TEST_SLUGS.length,
  });

  return {
    'test/LoadTests/results/latest-redirect-soak.json':
      JSON.stringify(summary, null, 2),
    stdout: formatSummaryText(summary),
  };
}
