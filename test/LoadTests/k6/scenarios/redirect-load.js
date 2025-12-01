/**
 * Redirect Load Test — Sustained Throughput
 *
 * Validates the redirect path at the 1B clicks/month peak capacity.
 *
 * Scale context:
 *   Average: 1B ÷ 2,592,000 s/month ≈ 386 req/s
 *   Peak:    Viral bursts assumed 100× average ≈ 38,500 req/s
 *   Default TARGET_RPS=500 is safe for staging; set 38500 for production.
 *
 * Prerequisites:
 *   The DynamoDB table in REDIRECT_BASE_URL's environment MUST contain links
 *   for every combination of REDIRECT_TEST_HOSTS × REDIRECT_TEST_SLUGS.
 *   Seed them via the control-plane API or the seed script before running.
 *
 * Usage:
 *   # Staging (moderate load):
 *   k6 run -e REDIRECT_BASE_URL=https://r.staging.short.io \
 *           -e REDIRECT_TEST_HOSTS=acme.short.io \
 *           -e REDIRECT_TEST_SLUGS=abc123,def456,ghi789 \
 *           -e TARGET_RPS=1000 \
 *           test/LoadTests/k6/scenarios/redirect-load.js
 *
 *   # Production (full scale):
 *   k6 run -e TARGET_RPS=38500 \
 *           --out json=test/LoadTests/results/redirect-load-$(date +%s).json \
 *           test/LoadTests/k6/scenarios/redirect-load.js
 *
 * Pass/fail criteria:
 *   p99 < 100 ms, p95 < 50 ms, error rate < 0.1%
 */

import http from 'k6/http';
import { Trend, Counter } from 'k6/metrics';
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
const cacheHits       = new Counter('redirect_cache_hits');

// preAllocatedVUs assumes ~100 ms average response; allow 3× for burst.
const preAlloc = Math.max(20, Math.ceil(TARGET_RPS * 0.1));
const maxVUs   = Math.max(100, Math.ceil(TARGET_RPS * 0.3));

export const options = {
  scenarios: {
    sustained_redirect: {
      executor:        'constant-arrival-rate',
      rate:            TARGET_RPS,
      timeUnit:        '1s',
      duration:        '10m',
      preAllocatedVUs: preAlloc,
      maxVUs:          maxVUs,
      gracefulStop:    '30s',
    },
  },
  thresholds: {
    ...REDIRECT_THRESHOLDS,
    redirect_latency_ms: ['p(99)<100', 'p(95)<50'],
  },
  noConnectionReuse: false,
  userAgent:         'k6-redirect-load/1.0',
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

  // CloudFront edge cache hit tracking (present on CloudFront responses).
  const cacheStatus = res.headers['X-Cache'] || '';
  if (cacheStatus.startsWith('Hit')) {
    cacheHits.add(1);
  }

  checkRedirect(res);
}

export function handleSummary(data) {
  const cacheHitCount = data.metrics['redirect_cache_hits']?.values?.count ?? 0;
  const totalReqs     = data.metrics['http_reqs']?.values?.count ?? 0;
  const cacheHitRate  = totalReqs > 0 ? cacheHitCount / totalReqs : 0;

  const summary = buildSummaryData(data, 'redirect-load', {
    targetRps:    TARGET_RPS,
    hosts:        TEST_HOSTS,
    slugCount:    TEST_SLUGS.length,
    cacheHitRate: parseFloat(cacheHitRate.toFixed(4)),
  });

  return {
    'test/LoadTests/results/latest-redirect-load.json':
      JSON.stringify(summary, null, 2),
    stdout: formatSummaryText(summary) +
      `  cache hit rate: ${(cacheHitRate * 100).toFixed(1)}%\n\n`,
  };
}
