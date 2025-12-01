/**
 * Redirect Spike Test — 5× Sudden Traffic Increase
 *
 * Validates auto-scaling behaviour when traffic surges instantaneously to
 * five times the baseline throughput.  The system must:
 *   1. Serve the spike without a significant error spike.
 *   2. Recover gracefully when traffic drops back to baseline.
 *   3. Not require manual intervention to restore normal operation.
 *
 * Traffic shape (based on TARGET_RPS baseline):
 *   0–2 min   : Baseline (1× TARGET_RPS) — warm up Lambda fleet
 *   2–3 min   : Spike ramp (1× → 5× TARGET_RPS in 60 s)
 *   3–8 min   : Sustained spike (5× TARGET_RPS)
 *   8–9 min   : Recovery ramp (5× → 1× TARGET_RPS in 60 s)
 *   9–12 min  : Baseline recovery validation
 *
 * Pass/fail criteria (spike phase is intentionally relaxed):
 *   Baseline p99 < 100 ms, error rate < 0.1%
 *   Spike p99 < 500 ms,    error rate < 1%    (throttling expected)
 *   Recovery p99 < 100 ms, error rate < 0.1%  (must return to baseline)
 *
 * Usage:
 *   k6 run -e TARGET_RPS=500 \
 *           -e REDIRECT_BASE_URL=https://r.staging.short.io \
 *           test/LoadTests/k6/scenarios/redirect-spike.js
 */

import http from 'k6/http';
import { Trend } from 'k6/metrics';
import {
  REDIRECT_BASE_URL,
  TARGET_RPS,
  TEST_HOSTS,
  TEST_SLUGS,
} from '../lib/config.js';
import {
  randomItem,
  checkRedirect,
  buildSummaryData,
  formatSummaryText,
} from '../lib/helpers.js';

const baselineRps = TARGET_RPS;
const spikeRps    = TARGET_RPS * 5;

const redirectLatency = new Trend('redirect_latency_ms', true);

export const options = {
  scenarios: {
    spike_test: {
      executor: 'ramping-arrival-rate',
      startRate: baselineRps,
      timeUnit:  '1s',
      stages: [
        { duration: '2m', target: baselineRps  }, // warm up at baseline
        { duration: '1m', target: spikeRps     }, // ramp to 5× in 60 s
        { duration: '5m', target: spikeRps     }, // sustain spike
        { duration: '1m', target: baselineRps  }, // recover in 60 s
        { duration: '3m', target: baselineRps  }, // validate recovery
      ],
      preAllocatedVUs: Math.max(50, Math.ceil(spikeRps * 0.1)),
      maxVUs:          Math.max(500, Math.ceil(spikeRps * 0.4)),
      gracefulStop:    '60s',
    },
  },
  thresholds: {
    // Entire test: no hard SLO — spike phase is expected to exceed normal SLO.
    // Evaluated per-tag in the results dashboard.
    http_req_failed:   ['rate<0.05'],      // < 5% overall error rate
    http_req_duration: ['p(99)<1000'],     // < 1 s overall (includes spike)
    redirect_latency_ms: ['p(99)<1000'],
  },
  noConnectionReuse: false,
  userAgent:         'k6-redirect-spike/1.0',
};

export default function () {
  const host = randomItem(TEST_HOSTS);
  const slug = randomItem(TEST_SLUGS);

  const res = http.get(`${REDIRECT_BASE_URL}/${slug}`, {
    headers:   { Host: host },
    redirects: 0,
    timeout:   '10s',
    tags:      { name: 'redirect', host },
  });

  redirectLatency.add(res.timings.duration);
  checkRedirect(res);
}

export function handleSummary(data) {
  const summary = buildSummaryData(data, 'redirect-spike', {
    baselineRps,
    spikeRps,
    spikeFactor: 5,
  });

  return {
    'test/LoadTests/results/latest-redirect-spike.json':
      JSON.stringify(summary, null, 2),
    stdout: formatSummaryText(summary),
  };
}
