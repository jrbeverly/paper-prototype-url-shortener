#!/usr/bin/env bash
# seed-test-data.sh — create test links in ControlPlane for load testing
#
# Creates LINK_COUNT short links via the ControlPlane API and writes the
# resulting slugs to results/test-slugs.txt (one per line).
#
# Pass the comma-separated output to REDIRECT_TEST_SLUGS before running
# any redirect load test so k6 requests real, resolvable links.
#
# Required environment variables:
#   CONTROL_PLANE_BASE_URL  ControlPlane API base URL
#   CONTROL_PLANE_API_KEY   API key with link:write permission
#   TENANT_ID               UUID of the test tenant
#   DOMAIN_ID               UUID of a verified domain belonging to TENANT_ID
#
# Optional:
#   LINK_COUNT              Number of links to create (default: 100)
#                           Use ≥ 2000 to exercise both the in-memory hot-link
#                           cache (1 000 entries) and the DynamoDB cold path.
#
# Example:
#   CONTROL_PLANE_BASE_URL=https://api.staging.short.io \
#   CONTROL_PLANE_API_KEY=sk_test_abc123 \
#   TENANT_ID=00000000-0000-0000-0000-000000000001 \
#   DOMAIN_ID=00000000-0000-0000-0000-000000000002 \
#   LINK_COUNT=2000 \
#   ./test/LoadTests/scripts/seed-test-data.sh

set -euo pipefail

: "${CONTROL_PLANE_BASE_URL:?CONTROL_PLANE_BASE_URL is required}"
: "${CONTROL_PLANE_API_KEY:?CONTROL_PLANE_API_KEY is required}"
: "${TENANT_ID:?TENANT_ID is required}"
: "${DOMAIN_ID:?DOMAIN_ID is required}"
LINK_COUNT="${LINK_COUNT:-100}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RESULTS_DIR="$(cd "$SCRIPT_DIR/.." && pwd)/results"
SLUGS_FILE="$RESULTS_DIR/test-slugs.txt"

mkdir -p "$RESULTS_DIR"

# Select JSON extraction tool (jq preferred; python3 as fallback).
if command -v jq &>/dev/null; then
  extract_slug() { jq -r '.slug // empty'; }
elif command -v python3 &>/dev/null; then
  extract_slug() { python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('slug',''))"; }
else
  echo "ERROR: jq or python3 is required to parse API responses." >&2
  exit 1
fi

echo "==> Seeding $LINK_COUNT test links for load testing..." >&2
echo "    Control-plane : $CONTROL_PLANE_BASE_URL" >&2
echo "    Tenant        : $TENANT_ID" >&2
echo "    Domain        : $DOMAIN_ID" >&2
echo "    Output        : $SLUGS_FILE" >&2
echo "" >&2

: > "$SLUGS_FILE"
created=0
failed=0

for i in $(seq 1 "$LINK_COUNT"); do
  body=$(printf '{"domainId":"%s","destinationUrl":"https://example.com/load-test/%d","redirectType":"302"}' \
    "$DOMAIN_ID" "$i")

  response=$(curl -sf \
    -X POST \
    "$CONTROL_PLANE_BASE_URL/api/v1/tenants/$TENANT_ID/links" \
    -H "X-API-Key: $CONTROL_PLANE_API_KEY" \
    -H "Content-Type: application/json" \
    -d "$body" 2>/dev/null) || {
    echo "  [WARN] Request $i failed — check API key, tenant, and domain IDs" >&2
    failed=$((failed + 1))
    continue
  }

  slug=$(echo "$response" | extract_slug)
  if [[ -n "$slug" ]]; then
    echo "$slug" >> "$SLUGS_FILE"
    created=$((created + 1))
    if (( created % 50 == 0 )); then
      echo "  Created $created / $LINK_COUNT links..." >&2
    fi
  else
    echo "  [WARN] No slug in response for link $i" >&2
    failed=$((failed + 1))
  fi

  # Brief pause every 25 requests to avoid rate-limit bursts.
  if (( i % 25 == 0 )); then
    sleep 0.5
  fi
done

echo "" >&2
echo "==> Seeding complete: $created created, $failed failed" >&2
echo "    Slugs file: $SLUGS_FILE" >&2
echo "" >&2

if [[ $created -gt 0 ]]; then
  csv=$(tr '\n' ',' < "$SLUGS_FILE" | sed 's/,$//')
  echo "==> Use these slugs in redirect load tests:" >&2
  echo "" >&2
  echo "    export REDIRECT_TEST_SLUGS='$csv'" >&2
  echo "    make perf-load" >&2
  echo "" >&2
fi
