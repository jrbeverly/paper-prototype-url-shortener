# Load Testing Suite


k6-based performance and load tests for the short.io redirect and control-plane services.

## Requirements

- [k6](https://grafana.com/docs/k6/latest/set-up/install-k6/) ≥ 0.50
- Node.js ≥ 18 (for `summarize-results.js` and `run-load-tests.sh`)
- A deployed target environment (staging or production)

Install k6:

```bash
# macOS
brew install k6

# Debian/Ubuntu
sudo gpg --dearmor -o /usr/share/keyrings/k6-archive-keyring.gpg https://dl.k6.io/key.gpg
echo "deb [signed-by=/usr/share/keyrings/k6-archive-keyring.gpg] https://dl.k6.io/deb stable main" \
  | sudo tee /etc/apt/sources.list.d/k6.list
sudo apt-get update && sudo apt-get install k6
```

---

## Scenarios

| Makefile target    | Script                      | Duration   | Purpose                                    |
|--------------------|-----------------------------|------------|--------------------------------------------|
| `make perf-smoke`  | `ci-smoke.js`               | ~90 s      | Connectivity check; runs in CI on schedule |
| `make perf-load`   | `redirect-load.js`          | 10 min     | Sustained throughput at TARGET_RPS         |
| `make perf-spike`  | `redirect-spike.js`         | ~12 min    | 5× sudden traffic burst                    |
| `make perf-soak`   | `redirect-soak.js`          | ~70 min    | 1-hour soak for resource leaks             |
| `make perf-stress` | `redirect-stress.js`        | ~20 min    | Ramp to breaking point                     |
| `make perf-cp`     | `control-plane-load.js`     | 10 min     | Concurrent CRUD on the control-plane API   |

---

## Configuration

All settings are controlled via environment variables.  No script edits required.

| Variable                 | Default                     | Description                                          |
|--------------------------|-----------------------------|------------------------------------------------------|
| `REDIRECT_BASE_URL`      | `http://localhost:3000`     | CloudFront distribution or Lambda URL                |
| `CONTROL_PLANE_BASE_URL` | `http://localhost:5000`     | ControlPlane API base URL                            |
| `CONTROL_PLANE_API_KEY`  | _(empty)_                   | API key sent via `X-API-Key` header                  |
| `TENANT_ID`              | `00000000-…-000000000001`   | Tenant UUID for control-plane operations             |
| `DOMAIN_ID`              | `00000000-…-000000000002`   | Verified domain UUID for link creation               |
| `REDIRECT_TEST_HOSTS`    | `test.short.io`             | Comma-separated short domains used in redirect tests |
| `REDIRECT_TEST_SLUGS`    | `abc123,def456,…`           | Comma-separated pre-existing slugs to request        |
| `TARGET_RPS`             | `500`                       | Desired redirect arrival rate in req/s               |

---

## Seeding Test Data

Redirect load tests require real links in the target DynamoDB table.

Use the included seed script to create test links automatically:

```bash
CONTROL_PLANE_BASE_URL=https://api.staging.short.io \
CONTROL_PLANE_API_KEY=sk_test_... \
TENANT_ID=<uuid> \
DOMAIN_ID=<uuid> \
LINK_COUNT=2000 \
./test/LoadTests/scripts/seed-test-data.sh
```

The script writes created slugs to `results/test-slugs.txt` and prints the
`export REDIRECT_TEST_SLUGS=...` line to use in subsequent load test runs.

Use at least 2 000 unique slugs to exercise both the in-memory hot-link cache
(1 000 entries, 5-minute TTL) and the DynamoDB cold path.

---

## Running Tests

### Quick smoke test (CI-safe)

```bash
make perf-smoke
```

### Sustained redirect load test

```bash
REDIRECT_BASE_URL=https://r.staging.short.io \
REDIRECT_TEST_HOSTS=acme.short.io,beta.short.io \
REDIRECT_TEST_SLUGS=abc123,def456,ghi789 \
TARGET_RPS=5000 \
make perf-load
```

### Full-scale production validation (38 500 req/s)

```bash
TARGET_RPS=38500 \
REDIRECT_BASE_URL=https://r.short.io \
REDIRECT_TEST_HOSTS=acme.short.io \
REDIRECT_TEST_SLUGS=$(cat slugs.txt | tr '\n' ',') \
make perf-load
```

### Control-plane concurrent API load

```bash
CONTROL_PLANE_BASE_URL=https://api.staging.short.io \
CONTROL_PLANE_API_KEY=sk_test_... \
TENANT_ID=<uuid> \
DOMAIN_ID=<uuid> \
make perf-cp
```

---

## Acceptance Criteria and SLOs

### Redirect path (ADR-002)

| Metric    | SLO (normal load) | Spike tolerance |
|-----------|--------------------|-----------------|
| p50       | < 30 ms            | < 100 ms        |
| p95       | < 50 ms            | < 250 ms        |
| p99       | < 100 ms           | < 500 ms        |
| Error rate| < 0.1%             | < 1%            |

### Control plane

| Metric    | SLO                |
|-----------|--------------------|
| p95       | < 200 ms           |
| p99       | < 500 ms           |
| Error rate| < 1%               |

---

## Results and Dashboard

Each test run writes a compact summary to `results/latest-<scenario>.json`.

After each run, `summarize-results.js` appends the summary to `results/history.json`
and regenerates `dashboards/data.js`.

Open the performance dashboard:

```bash
open test/LoadTests/dashboards/index.html
```

Or serve it locally for live reload:

```bash
npx serve test/LoadTests/dashboards
```

### What's committed vs gitignored

| Path                                  | Committed | Notes                                   |
|---------------------------------------|-----------|-----------------------------------------|
| `results/history.json`               | Yes       | Compact summaries (< 1 KB per run)      |
| `results/latest-*.json`              | Yes       | Latest summary per scenario             |
| `dashboards/data.js`                 | Yes       | Auto-generated from history.json        |
| `results/<scenario>-<timestamp>.json`| No        | Full per-request k6 JSON (can be GBs)   |

---

## Interpreting Stress Test Results

The stress test (`redirect-stress.js`) ramps traffic to 100 000 req/s to find
the ceiling.  It intentionally exceeds healthy operating limits; a k6 "PASS"
is not the goal.

**How to find the breaking point:**

```bash
make perf-stress
# Then open the raw JSON output:
cat test/LoadTests/results/redirect-stress-<timestamp>.json | \
  python3 -c "
import sys, json
for line in sys.stdin:
  try:
    e = json.loads(line)
    if e.get('type') == 'Point' and e['metric'] == 'http_req_failed':
      if e['data']['value'] > 0:
        print(e['data']['time'], 'error at', e['data']['value'])
  except: pass
" | head -20
```

Document the breaking point in `docs/decisions/` with:
- The req/s level where error rate exceeded 1%
- The identified bottleneck (Lambda concurrency limit, DynamoDB throughput, etc.)
- The remediation applied or planned
