#!/usr/bin/env bash
#
# Grant a Blossom top-up pack through the payment abstraction, end to end, against the MOCK provider.
#
# This is the Phase 3 demonstration ("a demonstration script can grant a pack with no network
# access"). It needs no internet, no provider account and no credentials beyond a local API: the
# mock adapter (Modules/Payments/Providers/MockPaymentProvider.cs) is a first-class IPaymentProvider
# whose Development-only page and settle endpoint live on the API host.
#
# There are two modes.
#
#   --offline (default when no token is supplied)
#     Runs the in-process integration walk that performs the whole Flow with no sockets at all:
#     WebApplicationFactory over an in-memory database, a loopback-only stub OIDC authority, then
#     checkout -> mock settle -> signed webhook -> settlement -> Blossom grant + Verified income.
#     This is the mode that proves the Flow works with no network access.
#
#     dotnet test Aveline.Api.Tests/Aveline.Api.Tests.csproj \
#       --filter "FullyQualifiedName~PaymentEndpointsIntegrationTests.MockCheckout_SettlesTheIntent_AndGrantsTheBlossoms"
#
#   --live (needs AVELINE_OWNER_TOKEN)
#     Drives a locally running API over http://localhost:5091 only:
#       1. GET  .../blossoms/top-up-packs          pick a pack
#       2. GET  .../blossoms/balance               remember the balance
#       3. POST .../blossoms/top-ups/checkout      Idempotency-Key, { skuCode }
#       4. POST the same checkout with the SAME key and assert the SAME intent (a retry is one charge)
#       5. POST {checkoutUrl}/settle?token=tok_aveline_succeed   the mock "hosted page"
#       6. GET  .../payment-intents/{id}           poll until terminal; assert Succeeded
#       7. GET  .../blossoms/balance               assert the balance rose by the pack's Blossoms
#
# Start the API with the mock provider first (this is the whole configuration):
#
#   ASPNETCORE_ENVIRONMENT=Development \
#   Payments__Provider=mock \
#   Payments__Mock__Enabled=true \
#   Payments__Mock__WebhookSigningSecret=whsec_demo_local_secret \
#   dotnet run --project Aveline.Api
#
# Usage:
#   scripts/demo-blossom-top-up.sh                 # offline (no network)
#   scripts/demo-blossom-top-up.sh --offline
#   AVELINE_OWNER_TOKEN=<boutique-owner JWT> scripts/demo-blossom-top-up.sh --live [--pack SKU]
#
# Environment:
#   AVELINE_API_BASE_URL   optional, default http://localhost:5091
#   AVELINE_OWNER_TOKEN    a boutique-owner bearer token (billing:manage + billing:view)
#   AVELINE_SETTLE_TOKEN   optional mock test credential, default tok_aveline_succeed
set -euo pipefail

api_base_url="${AVELINE_API_BASE_URL:-${API_BASE_URL:-http://localhost:5091}}"
owner_token="${AVELINE_OWNER_TOKEN:-${OWNER_TOKEN:-}}"
settle_token="${AVELINE_SETTLE_TOKEN:-tok_aveline_succeed}"

mode=""
pack_sku=""

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  sed -n '2,50p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
  exit 0
fi

while [[ $# -gt 0 ]]; do
  case "$1" in
    --offline) mode="offline"; shift ;;
    --live) mode="live"; shift ;;
    --pack) pack_sku="${2:-}"; shift 2 ;;
    --token) owner_token="${2:-}"; shift 2 ;;
    --api-base) api_base_url="${2:-}"; shift 2 ;;
    *) echo "error: unknown argument '$1' (try --help)" >&2; exit 2 ;;
  esac
done

if [[ -z "$mode" ]]; then
  # Offline unless a token says otherwise: the mode that always works is the honest default.
  if [[ -n "$owner_token" ]]; then mode="live"; else mode="offline"; fi
fi

api_base_url="${api_base_url%/}"

fail() { echo "error: $*" >&2; exit 1; }
step() { echo; echo "== $*"; }
ok() { echo "   ok: $*"; }

# ---------------------------------------------------------------------------- offline

run_offline() {
  echo "Offline demonstration: the whole Flow runs in-process, with no network access."
  echo "Repository: $(pwd)"
  echo

  command -v dotnet >/dev/null 2>&1 || fail "dotnet is required for the offline demonstration."

  local test_name="PaymentEndpointsIntegrationTests.MockCheckout_SettlesTheIntent_AndGrantsTheBlossoms"

  # The test host inherits this shell's environment, and a developer shell that has
  # `Media__Provider=cloudinary` exported makes `Program` refuse to boot without Cloudinary
  # credentials - a media configuration the payment walk has nothing to do with. Pin the media
  # provider to its documented safe default for this one process, without touching any file.
  echo "Running: dotnet test --filter FullyQualifiedName~${test_name}"
  echo

  Media__Provider=database \
  Media__AllowDatabaseProviderInProduction=true \
  dotnet test Aveline.Api.Tests/Aveline.Api.Tests.csproj \
    --filter "FullyQualifiedName~${test_name}" \
    --nologo \
    || fail "the offline demonstration failed; scroll up for the test output."

  echo
  echo "The pack was granted: checkout -> mock settle -> signed webhook -> one Blossom ledger"
  echo "grant + one Verified income row, asserted in-process. No socket left loopback."
  echo
  echo "Live mode (a running API and a boutique-owner token):"
  echo "  AVELINE_OWNER_TOKEN=<jwt> scripts/demo-blossom-top-up.sh --live"
}

# ---------------------------------------------------------------------------- live

work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

http_status=""
http_body=""

# http_json METHOD PATH [BODY] [IDEMPOTENCY_KEY]
http_json() {
  local method="$1" path="$2" body="${3:-}" idem="${4:-}"
  local out="$work_dir/response.json"
  local -a args=(
    -sS -o "$out" -w '%{http_code}'
    -X "$method" "$api_base_url$path"
    -H "Authorization: Bearer $owner_token"
    -H "Accept: application/json"
  )
  [[ -n "$idem" ]] && args+=(-H "Idempotency-Key: $idem")
  if [[ -n "$body" ]]; then
    args+=(-H "Content-Type: application/json" --data "$body")
  fi
  http_status="$(curl "${args[@]}")" || fail "curl failed for $method $path (is the API at $api_base_url?)"
  http_body="$(cat "$out")"
}

json_field() {
  python3 -c 'import json,sys; d=json.loads(sys.argv[1]); v=d.get(sys.argv[2]); print("" if v is None else v)' "$1" "$2"
}

# The organization id is what every tenant route is addressed by, so live mode needs it first.
# Resolve it from the owner's memberships rather than asking a human to copy a UUID.
resolve_organization() {
  http_json GET "/api/v1/orgs/my"
  [[ "$http_status" == "200" ]] || fail "GET /api/v1/orgs/my failed with HTTP $http_status: $http_body"
  python3 - "$http_body" <<'PY'
import json, sys
rows = json.loads(sys.argv[1])
if isinstance(rows, dict):
    rows = rows.get("items") or rows.get("memberships") or []
active = [r for r in rows if (r.get("status") or "Active") == "Active"]
if not active:
    raise SystemExit("error: the token has no active organization membership")
print(active[0].get("organizationId") or active[0].get("id"))
PY
}

run_live() {
  command -v curl >/dev/null 2>&1 || fail "curl is required."
  command -v python3 >/dev/null 2>&1 || fail "python3 is required to parse API responses."
  [[ -n "$owner_token" ]] || fail "AVELINE_OWNER_TOKEN is not set; live mode needs a boutique-owner bearer token."

  echo "Live demonstration against $api_base_url (localhost only; no external network)."

  local organization_id
  organization_id="$(resolve_organization)"
  [[ -n "$organization_id" ]] || fail "could not resolve the caller's organization id."
  ok "organization $organization_id"

  local base="/api/v1/orgs/$organization_id"

  step "1/7  Read the purchasable top-up packs"
  http_json GET "$base/blossoms/top-up-packs"
  [[ "$http_status" == "200" ]] || fail "GET top-up-packs failed with HTTP $http_status: $http_body"
  if [[ -z "$pack_sku" ]]; then
    pack_sku="$(python3 -c 'import json,sys; rows=json.loads(sys.argv[1]); print(rows[0]["skuCode"] if rows else "")' "$http_body")"
  fi
  [[ -n "$pack_sku" ]] || fail "no top-up pack is configured in the price book; run scripts/seed-price-book.sh first."
  local pack_quantity
  pack_quantity="$(python3 - "$http_body" "$pack_sku" <<'PY'
import json, sys
rows = json.loads(sys.argv[1])
match = next((r for r in rows if r.get("skuCode") == sys.argv[2]), None)
if match is None:
    raise SystemExit(f"error: {sys.argv[2]} is not in the catalogue")
print(format(float(match["blossomQuantity"]), "g"))
PY
)"
  ok "pack $pack_sku grants $pack_quantity Blossoms"

  step "2/7  Remember the current Blossom balance"
  http_json GET "$base/blossoms/balance"
  [[ "$http_status" == "200" ]] || fail "GET balance failed with HTTP $http_status: $http_body"
  local before
  before="$(json_field "$http_body" blossomRemaining)"
  ok "balance before: $before"

  step "3/7  Create the checkout (Idempotency-Key required)"
  local key
  key="$(python3 -c 'import uuid; print(uuid.uuid4())')"
  http_json POST "$base/blossoms/top-ups/checkout" "{\"skuCode\":\"$pack_sku\"}" "$key"
  [[ "$http_status" == "201" ]] || fail "checkout failed with HTTP $http_status: $http_body"
  local intent_id checkout_url amount
  intent_id="$(json_field "$http_body" paymentIntentId)"
  checkout_url="$(json_field "$http_body" checkoutUrl)"
  amount="$(json_field "$http_body" amountLkr)"
  ok "intent $intent_id for LKR $amount, status $(json_field "$http_body" status)"

  step "4/7  Replay the checkout with the same key (a retry must be one charge)"
  http_json POST "$base/blossoms/top-ups/checkout" "{\"skuCode\":\"$pack_sku\"}" "$key"
  local replayed_id
  replayed_id="$(json_field "$http_body" paymentIntentId)"
  [[ "$replayed_id" == "$intent_id" ]] || fail "the retry minted a second intent ($replayed_id); idempotency is broken"
  ok "same intent id on the retry: $replayed_id"

  step "5/7  Complete the mock provider's hosted checkout"
  [[ -n "$checkout_url" ]] || fail "the provider returned no checkoutUrl"
  local settle_url="$api_base_url$checkout_url/settle?token=$settle_token"
  http_status="$(curl -sS -o "$work_dir/settle.json" -w '%{http_code}' -X POST "$settle_url")" \
    || fail "curl failed for the mock settle endpoint"
  [[ "$http_status" == "200" ]] || fail "mock settle failed with HTTP $http_status: $(cat "$work_dir/settle.json")"
  ok "provider applied $settle_token and delivered its signed webhook"

  step "6/7  Poll the intent until it is terminal"
  local status="" attempts=0
  while (( attempts < 30 )); do
    http_json GET "$base/payment-intents/$intent_id"
    [[ "$http_status" == "200" ]] || fail "GET intent failed with HTTP $http_status: $http_body"
    status="$(json_field "$http_body" status)"
    case "$status" in
      Succeeded|Failed|Cancelled|Expired|Refunded) break ;;
    esac
    attempts=$((attempts + 1))
    sleep 0.5
  done
  [[ "$status" == "Succeeded" ]] || fail "the intent is $status, not Succeeded: $http_body"
  ok "terminal state from the server: $status (settledAt $(json_field "$http_body" settledAt))"

  step "7/7  Read the balance again"
  http_json GET "$base/blossoms/balance"
  local after
  after="$(json_field "$http_body" blossomRemaining)"
  ok "balance after: $after"

  python3 - "$before" "$after" "$pack_quantity" <<'PY'
import sys
before, after, quantity = float(sys.argv[1]), float(sys.argv[2]), float(sys.argv[3])
if round(after - before, 6) != round(quantity, 6):
    raise SystemExit(f"error: the balance moved by {after - before}, expected {quantity}")
print(f"   ok: the balance rose by exactly {quantity} Blossoms")
PY

  echo
  echo "Demonstration complete: checkout -> provider settle -> webhook -> grant, all against the"
  echo "mock provider and the local API."
}

if [[ "$mode" == "offline" ]]; then
  run_offline
else
  run_live
fi
