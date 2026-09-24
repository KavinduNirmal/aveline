#!/usr/bin/env bash
#
# Seed the Blossom price book with the Phase 0 documented prices.
#
# Prices are DATA, not code: this script creates the three paid plan allowances and
# the three Blossom top-up packs through the admin price-book API, so an operator can
# change a price without a deploy.
#
#   GET   /api/v1/admin/pricing/price-book[?skuKind=...]   idempotency check
#   POST  /api/v1/admin/pricing/price-book                 creates a row as Draft
#   PATCH /api/v1/admin/pricing/price-book/{entryId:guid}  activates it ({"status":"Active"})
#
# The POST body field names are the wire (camelCase) form of CreatePriceEntryRequest,
# verified against Aveline.Api/Modules/Billing/DTOs/PricingDtos.cs:109-118:
#   planTier, organizationId, skuKind, skuCode, blossomQuantity, priceLkr,
#   effectiveFrom, effectiveTo, changeReason
# The activate body is UpdatePriceEntryRequest (same file, :120-126): { status }.
#
# The POST handler creates every entry as Draft (PricingService.CreatePriceEntryAsync),
# so a row that must be purchasable has to be activated with a second call.
#
# Source of truth for the prices and the SKU codes: docs/architecture/pricing_plan.md
# §18. The run contract is docs/backend/implementation-plan.md §10.7.
#
# Idempotent: it reads the book first and skips a SKU that already has a row, sends a
# per-row Idempotency-Key, and treats a 409 as "already exists" (the unique index on
# (PlanTier, OrganizationId, SkuKind, SkuCode, EffectiveFrom)). Re-running creates
# nothing; it only activates a row that is still Draft.
#
# Usage:
#   AVELINE_ADMIN_TOKEN=<team-admin bearer token> scripts/seed-price-book.sh
#
# Environment:
#   AVELINE_API_BASE_URL            optional, default http://localhost:5091
#   AVELINE_ADMIN_TOKEN             required, team Admin with pricing:manage
#   AVELINE_PRICE_EFFECTIVE_FROM    optional, ISO-8601; default 2026-01-01T00:00:00Z
set -euo pipefail

api_base_url="${AVELINE_API_BASE_URL:-${API_BASE_URL:-http://localhost:5091}}"
admin_token="${AVELINE_ADMIN_TOKEN:-${ADMIN_TOKEN:-}}"
effective_from="${AVELINE_PRICE_EFFECTIVE_FROM:-2026-01-01T00:00:00Z}"
idempotency_prefix="p0-price-seed-v1"

# skuKind|planTier|skuCode|blossomQuantity|priceLkr|changeReason
# planTier is empty for a top-up pack (tier-independent), and the row is global
# (organizationId null) in every case.
rows=(
  "PlanAllowance|Bloom|plan_bloom_monthly|750|3500|Phase 0 seed: Bloom monthly plan allowance, documented in docs/architecture/pricing_plan.md section 18."
  "PlanAllowance|Orchid|plan_orchid_monthly|2000|9000|Phase 0 seed: Orchid monthly plan allowance, documented in docs/architecture/pricing_plan.md section 18."
  "PlanAllowance|Rose|plan_rose_monthly|5000|20000|Phase 0 seed: Rose monthly plan allowance, documented in docs/architecture/pricing_plan.md section 18."
  "TopUpPack||blossom_pack_100|100|500|Phase 0 seed: 100-Blossom top-up pack, documented in docs/architecture/pricing_plan.md section 18."
  "TopUpPack||blossom_pack_500|500|2000|Phase 0 seed: 500-Blossom top-up pack, documented in docs/architecture/pricing_plan.md section 18."
  "TopUpPack||blossom_pack_1000|1000|3500|Phase 0 seed: 1,000-Blossom top-up pack, documented in docs/architecture/pricing_plan.md section 18."
)

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  sed -n '2,40p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
  exit 0
fi

fail() {
  echo "error: $*" >&2
  exit 1
}

warn() {
  echo "warning: $*" >&2
}

command -v curl >/dev/null 2>&1 || fail "curl is required."
command -v python3 >/dev/null 2>&1 || fail "python3 is required to parse API responses."

if [[ -z "$admin_token" ]]; then
  fail "AVELINE_ADMIN_TOKEN is not set. Export a team-admin bearer token that holds pricing:manage (and pricing:view for the read)."
fi

api_base_url="${api_base_url%/}"

work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

http_status=""
http_body=""

# http_json METHOD PATH [BODY] [IDEMPOTENCY_KEY]
# Sets http_status and http_body. Fails the script only when curl itself fails.
http_json() {
  local method="$1" path="$2" body="${3:-}" idem="${4:-}"
  local out="$work_dir/response.json"
  local -a args=(
    -sS -o "$out" -w '%{http_code}'
    -X "$method" "$api_base_url$path"
    -H "Authorization: Bearer $admin_token"
    -H "Accept: application/json"
  )
  if [[ -n "$idem" ]]; then
    args+=(-H "Idempotency-Key: $idem")
  fi
  if [[ -n "$body" ]]; then
    args+=(-H "Content-Type: application/json" --data "$body")
  fi
  http_status="$(curl "${args[@]}")" || fail "curl failed for $method $path (is the API reachable at $api_base_url?)"
  http_body="$(cat "$out")"
}

# json_field JSON FIELD
json_field() {
  python3 -c 'import json,sys; d=json.loads(sys.argv[1]); v=d.get(sys.argv[2]); print("" if v is None else v)' "$1" "$2"
}

# build_body SKU_KIND PLAN_TIER SKU_CODE QUANTITY PRICE REASON
build_body() {
  local sku_kind="$1" plan_tier="$2" sku_code="$3" quantity="$4" price="$5" reason="$6"
  local plan_tier_json="null"
  if [[ -n "$plan_tier" ]]; then
    plan_tier_json="\"$plan_tier\""
  fi
  printf '{"planTier":%s,"organizationId":null,"skuKind":"%s","skuCode":"%s","blossomQuantity":%s,"priceLkr":%s,"effectiveFrom":"%s","effectiveTo":null,"changeReason":"%s"}' \
    "$plan_tier_json" "$sku_kind" "$sku_code" "$quantity" "$price" "$effective_from" "$reason"
}

# select_entry JSON SKU_KIND SKU_CODE PLAN_TIER
# Prints "id<TAB>status<TAB>quantity<TAB>price<TAB>effectiveFrom" for the best global
# match (an Active row if one exists, otherwise the newest row), or nothing.
select_entry() {
  python3 - "$1" "$2" "$3" "$4" <<'PY'
import json, sys

payload, sku_kind, sku_code, plan_tier = sys.argv[1:5]
try:
    entries = json.loads(payload)
except (json.JSONDecodeError, TypeError):
    sys.exit(0)
if not isinstance(entries, list):
    sys.exit(0)

expected_tier = plan_tier or None
matches = [
    entry for entry in entries
    if entry.get("skuKind") == sku_kind
    and entry.get("skuCode") == sku_code
    and entry.get("organizationId") in (None, "")
    and (entry.get("planTier") or None) == expected_tier
]
if not matches:
    sys.exit(0)

active = [entry for entry in matches if entry.get("status") == "Active"]
chosen = active[0] if active else sorted(
    matches, key=lambda entry: entry.get("effectiveFrom") or "", reverse=True)[0]

print("\t".join([
    str(chosen.get("id", "")),
    str(chosen.get("status", "")),
    format(float(chosen.get("blossomQuantity", 0)), "g"),
    format(float(chosen.get("priceLkr", 0)), "g"),
    str(chosen.get("effectiveFrom", "")),
]))
PY
}

# read_entries SKU_KIND -> http_body (must be called via http_json first)
read_entries() {
  http_json GET "/api/v1/admin/pricing/price-book?skuKind=$1"
  if [[ "$http_status" != "200" ]]; then
    if [[ "$http_status" == "401" || "$http_status" == "403" ]]; then
      fail "reading the price book was refused (HTTP $http_status). AVELINE_ADMIN_TOKEN must be a team Admin (pricing:view / pricing:manage). Body: $http_body"
    fi
    fail "GET price-book failed with HTTP $http_status: $http_body"
  fi
}

# activate ENTRY_ID SKU_CODE
activate() {
  local entry_id="$1" sku_code="$2"
  http_json PATCH "/api/v1/admin/pricing/price-book/$entry_id" '{"status":"Active"}'
  if [[ "$http_status" != "200" ]]; then
    fail "activating $sku_code (entry $entry_id) failed with HTTP $http_status: $http_body"
  fi
}

created=0
activated=0
present=0
warned=0

echo "Seeding Blossom price book at $api_base_url (effectiveFrom $effective_from)"

for row in "${rows[@]}"; do
  IFS='|' read -r sku_kind plan_tier sku_code quantity price reason <<<"$row"
  scope="${plan_tier:-global}"

  read_entries "$sku_kind"
  existing="$(select_entry "$http_body" "$sku_kind" "$sku_code" "$plan_tier")"

  if [[ -n "$existing" ]]; then
    IFS=$'\t' read -r entry_id entry_status entry_quantity entry_price entry_from <<<"$existing"

    if [[ "$entry_quantity" != "$quantity" || "$entry_price" != "$price" ]]; then
      warn "$sku_code [$scope] already exists (id $entry_id, $entry_status) as $entry_quantity Blossoms for LKR $entry_price, effective $entry_from; the documented price is $quantity Blossoms for LKR $price. Left unchanged."
      warned=$((warned + 1))
      continue
    fi

    if [[ "$entry_status" == "Active" ]]; then
      echo "present    $sku_code [$scope] $entry_quantity Blossoms for LKR $entry_price (Active, id $entry_id)"
      present=$((present + 1))
      continue
    fi

    activate "$entry_id" "$sku_code"
    echo "activated  $sku_code [$scope] $entry_quantity Blossoms for LKR $entry_price ($entry_status -> Active, id $entry_id)"
    activated=$((activated + 1))
    continue
  fi

  body="$(build_body "$sku_kind" "$plan_tier" "$sku_code" "$quantity" "$price" "$reason")"
  http_json POST "/api/v1/admin/pricing/price-book" "$body" "${idempotency_prefix}-${sku_code}"

  case "$http_status" in
    201)
      entry_id="$(json_field "$http_body" id)"
      entry_status="$(json_field "$http_body" status)"
      ;;
    409)
      # The unique (PlanTier, OrganizationId, SkuKind, SkuCode, EffectiveFrom) index
      # rejected a concurrent/previous create. Treat it as "already exists".
      read_entries "$sku_kind"
      existing="$(select_entry "$http_body" "$sku_kind" "$sku_code" "$plan_tier")"
      if [[ -z "$existing" ]]; then
        fail "$sku_code [$scope] was rejected as a duplicate (HTTP 409) but no matching price row is readable: $http_body"
      fi
      echo "present    $sku_code [$scope] already exists (HTTP 409 on create)"
      present=$((present + 1))
      continue
      ;;
    400)
      fail "$sku_code [$scope] was rejected (HTTP 400): $http_body"
      ;;
    401|403)
      fail "creating $sku_code [$scope] was refused (HTTP $http_status). AVELINE_ADMIN_TOKEN must hold pricing:manage. Body: $http_body"
      ;;
    *)
      fail "creating $sku_code [$scope] failed with HTTP $http_status: $http_body"
      ;;
  esac

  if [[ "$entry_status" != "Active" ]]; then
    activate "$entry_id" "$sku_code"
  fi
  echo "created    $sku_code [$scope] $quantity Blossoms for LKR $price (Active, id $entry_id)"
  created=$((created + 1))
done

echo
echo "Done: $created created, $activated activated, $present already present, $warned price mismatch(es)."
echo "Verify with:"
echo "  curl -sS -H \"Authorization: Bearer \$AVELINE_ADMIN_TOKEN\" \"$api_base_url/api/v1/admin/pricing/price-book?skuKind=PlanAllowance\""

if (( warned > 0 )); then
  echo "One or more SKUs already carry a different price; re-run with the matching documented price or adjust the rows by hand." >&2
  exit 2
fi
