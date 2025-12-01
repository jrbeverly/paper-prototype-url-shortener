#!/usr/bin/env bash
# run-load-tests.sh — orchestrate k6 load test scenarios
#
# Usage:
#   ./test/LoadTests/scripts/run-load-tests.sh <scenario> [k6-flags...]
#
# Scenarios:
#   smoke         CI smoke test (~90 s)
#   load          Sustained redirect throughput (10 min)
#   spike         5× traffic spike (12 min)
#   soak          1-hour soak test (~70 min)
#   stress        Find the breaking point (~20 min)
#   control-plane Control-plane concurrent CRUD (10 min)
#   all           Run smoke → load → spike → control-plane (in order)
#
# Environment variables (all optional — see k6/lib/config.js for defaults):
#   REDIRECT_BASE_URL         e.g. https://r.staging.short.io
#   CONTROL_PLANE_BASE_URL    e.g. https://api.staging.short.io
#   CONTROL_PLANE_API_KEY     API key for control-plane operations
#   TENANT_ID                 UUID of the test tenant
#   DOMAIN_ID                 UUID of a verified domain
#   REDIRECT_TEST_HOSTS       Comma-separated list of test short domains
#   REDIRECT_TEST_SLUGS       Comma-separated list of pre-existing slugs
#   TARGET_RPS                Target requests/second (default: 500)
#   K6_EXTRA_FLAGS            Extra flags passed verbatim to k6
#
# Examples:
#   make perf-smoke
#   TARGET_RPS=2000 make perf-load
#   REDIRECT_BASE_URL=https://r.prod.short.io TARGET_RPS=38500 \
#     ./test/LoadTests/scripts/run-load-tests.sh load --out influxdb=http://localhost:8086/k6

set -euo pipefail

# ── Resolve paths relative to repo root ──────────────────────────────────
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
LOAD_TESTS_DIR="$REPO_ROOT/test/LoadTests"
SCENARIOS_DIR="$LOAD_TESTS_DIR/k6/scenarios"
RESULTS_DIR="$LOAD_TESTS_DIR/results"
TIMESTAMP="$(date +%Y%m%d-%H%M%S)"

# ── Prerequisites ─────────────────────────────────────────────────────────
if ! command -v k6 &>/dev/null; then
  echo "ERROR: k6 not found." >&2
  echo "  Install: https://grafana.com/docs/k6/latest/set-up/install-k6/" >&2
  echo "  macOS:   brew install k6" >&2
  echo "  Linux:   sudo gpg --dearmor -o /usr/share/keyrings/k6-archive-keyring.gpg https://dl.k6.io/key.gpg" >&2
  echo "           echo 'deb [signed-by=/usr/share/keyrings/k6-archive-keyring.gpg] https://dl.k6.io/deb stable main' | sudo tee /etc/apt/sources.list.d/k6.list" >&2
  echo "           sudo apt-get update && sudo apt-get install k6" >&2
  exit 1
fi

mkdir -p "$RESULTS_DIR"

# ── Helpers ───────────────────────────────────────────────────────────────
run_scenario() {
  local scenario="$1"; shift
  local script="$SCENARIOS_DIR/$scenario.js"

  if [[ ! -f "$script" ]]; then
    echo "ERROR: Unknown scenario '$scenario'. Script not found: $script" >&2
    exit 1
  fi

  echo ""
  echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
  echo "  Running: $scenario"
  echo "  Script : $script"
  echo "  Time   : $(date)"
  echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

  # Run k6 from the repo root so handleSummary paths resolve correctly.
  cd "$REPO_ROOT"
  k6 run \
    --out "json=$RESULTS_DIR/${scenario}-${TIMESTAMP}.json" \
    ${K6_EXTRA_FLAGS:-} \
    "$@" \
    "$script"

  # Update history.json with this run's summary.
  if command -v node &>/dev/null && [[ -f "$LOAD_TESTS_DIR/results/latest-${scenario#ci-}.json" || -f "$LOAD_TESTS_DIR/results/latest-${scenario}.json" ]]; then
    node "$LOAD_TESTS_DIR/scripts/summarize-results.js" || true
  fi
}

# ── Argument parsing ──────────────────────────────────────────────────────
SCENARIO="${1:-smoke}"
shift || true  # remaining args forwarded to k6

case "$SCENARIO" in
  smoke|ci-smoke)
    run_scenario "ci-smoke" "$@"
    ;;
  load|redirect-load)
    run_scenario "redirect-load" "$@"
    ;;
  spike|redirect-spike)
    run_scenario "redirect-spike" "$@"
    ;;
  soak|redirect-soak)
    echo "WARNING: Soak test runs for ~70 minutes."
    run_scenario "redirect-soak" "$@"
    ;;
  stress|redirect-stress)
    echo "WARNING: Stress test drives the system past healthy limits."
    run_scenario "redirect-stress" "$@"
    ;;
  control-plane|cp)
    run_scenario "control-plane-load" "$@"
    ;;
  all)
    run_scenario "ci-smoke" "$@"
    run_scenario "redirect-load" "$@"
    run_scenario "redirect-spike" "$@"
    run_scenario "control-plane-load" "$@"
    echo ""
    echo "==> All scenarios complete. Results in $RESULTS_DIR/"
    ;;
  *)
    echo "ERROR: Unknown scenario '$SCENARIO'" >&2
    echo "Valid: smoke, load, spike, soak, stress, control-plane, all" >&2
    exit 1
    ;;
esac
