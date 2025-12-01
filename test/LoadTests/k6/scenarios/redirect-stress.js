/**
 * Redirect Stress Test — Find the Breaking Point
 *
 * Ramps traffic to extreme levels to identify:
 *   - The req/s rate at which error rate first exceeds 1%
 *   - The req/s rate at which p99 first exceeds 500 ms
 *   - The bottleneck component (Lambda concurrency, DynamoDB, CloudFront)
 *
 * This test intentionally drives the system past healthy operating limits.
 * Thresholds are set high so k6 does not exit early — the goal is to
 * collect data across the full ramp, then inspect the JSON output to
 * identify the inflection point.
 *
 * Traffic shape:
 *   0–2 min   : 100 req/s (baseline verification)
 *   2–7 min   : 100 → 10,000 req/s (first ramp)
 *   7–12 min  : 10,000 → 50,000 req/s (second ramp)
 *   12–17 min : 50,000 → 100,000 req/s (extreme ramp)
 *   17–20 min : Recovery ramp back to 0
 *
 * Interpreting results:
 *   Run with --out json=results/stress-<timestamp>.json and inspect the
 *   per-second data points to find when error rate rose above acceptable
 *   and what the Lambda concurrency limit was at that moment.
 *
 * Usage:
 *   k6 run \
 *     -e REDIRECT_BASE_URL=https://r.staging.short.io \
 *     --out json=test/LoadTests/results/stress-$(date +%s).json \
 *     test/LoadTests/k6/scenarios/redirect-stress.js
 */

import http from 'k6/http';
import { Trend, Rate } from 'k6/metrics';
import {
  REDIRECT_BASE_URL,
  TEST_HOSTS,
  TEST_SLUGS,
} from '../lib/config.js';
import {
  randomItem,
  checkRedirect,
  buildSummaryData,
  formatSummaryText,
} from '../lib/helpers.js';

const redirectLatency = new Trend('redirect_latency_ms', true);
const errorRate       = new Rate('redirect_error_rate');

export const options = {
  scenarios: {
    stress_ramp: {
      executor:        'ramping-arrival-rate',
      startRate:       100,
      timeUnit:        '1s',
      stages: [
        { duration: '2m',  target: 100    }, // baseline verification
        { duration: '5m',  target: 10000  }, // first ramp
        { duration: '5m',  target: 50000  }, // push hard
        { duration: '5m',  target: 100000 }, // extreme — find the ceiling
        { duration: '3m',  target: 0      }, // cool down
      ],
      // Allocate headroom for extreme load; maxVUs caps resource usage.
      preAllocatedVUs: 1000,
      maxVUs:          10000,
      gracefulStop:    '60s',
    },
  },
  thresholds: {
    // Intentionally permissive — we WANT to observe the system breaking.
    // A hard fail at 50% error rate prevents the runner from being overwhelmed.
    http_req_failed:     ['rate<0.50'],
    http_req_duration:   ['p(99)<10000'],
    redirect_error_rate: ['rate<0.50'],
    redirect_latency_ms: ['p(99)<10000'],
  },
  noConnectionReuse: false,
  userAgent:         'k6-redirect-stress/1.0',
};

export default function () {
  const host = randomItem(TEST_HOSTS);
  const slug = randomItem(TEST_SLUGS);

  const res = http.get(`${REDIRECT_BASE_URL}/${slug}`, {
    headers:   { Host: host },
    redirects: 0,
    timeout:   '15s',
    tags:      { name: 'redirect', host },
  });

  const isRedirect = res.status >= 300 && res.status < 400;
  redirectLatency.add(res.timings.duration);
  errorRate.add(!isRedirect);
  checkRedirect(res);
}

export function handleSummary(data) {
  const summary = buildSummaryData(data, 'redirect-stress', {
    peakRps: 100000,
    note: 'Inspect per-second data in the raw JSON output to find the inflection point.',
  });

  return {
    'test/LoadTests/results/latest-redirect-stress.json':
      JSON.stringify(summary, null, 2),
    stdout: [
      formatSummaryText(summary),
      '  NOTE: This test intentionally drives the system past healthy limits.',
      '  Check the raw JSON output for per-second latency and error data.',
      '  Identify the req/s level where error rate first exceeded 1%.',
      '',
    ].join('\n'),
  };
}
