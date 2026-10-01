#!/usr/bin/env bash
# =============================================================================
# Seed the composed cross-surface E2E stack (gap E1).
#
# What it establishes, and by which real path
# -------------------------------------------
#   * one `Users` row whose `ClerkId` matches the `sub` of the bearer token the spec
#     presents  ->  direct SQL. There is no API route that creates a user without a
#     Clerk webhook signature (`POST /api/v1/webhooks/clerk` is Svix-signed and fails
#     closed), and the whole point of the seed is a user the API can resolve. The INSERT
#     below is derived from the real entity configuration
#     (`Aveline.Api/Infrastructure/Data/Configurations/UserConfiguration.cs`) and the
#     live table (`\d "Users"` against the migrated database), not from a guess.
#   * one `Organizations` row plus the owner's `OrganizationMemberships` row  ->  real
#     API call, `POST /api/v1/orgs`, which creates both and makes the owner active
#     (`OrganizationService.CreateOrganizationAsync`). It is only `.RequireAuthorization()`
#     (any authenticated caller), and it answers 404 when the User row is missing, which is
#     why the SQL half has to run first.
#   * one `Customers` row in that organization  ->  direct SQL, for the same reason: the
#     conversation is created with `customerId` so the agent has a customer to address.
#
# Idempotent: safe to run twice (ON CONFLICT DO NOTHING, slug lookup before create).
#
# Invocation
# ----------
#   scripts/seed-e2e-composed-stack.sh
#
# Environment (all optional; defaults target the local host-run stack)
# -------------------------------------------------------------------
#   E2E_API_BASE_URL     API origin                 (default http://127.0.0.1:5091)
#   E2E_IDP_BASE_URL     stub OIDC issuer origin    (default: read from E2E_STATE_FILE_DIR/idp.json)
#   E2E_CLERK_SUB        Clerk-shaped subject for the minted token (default user_e2e_cross_surface)
#   E2E_ORG_SLUG         boutique slug to create/find (default e2e-cross-surface)
#   E2E_ORG_NAME         boutique display name    (default "E2E Cross-Surface Boutique")
#   E2E_CUSTOMER_PHONE   unique per-org phone for the customer seed (default +94770000001)
#   E2E_STATE_DIR        where idp.json/e2e-composed-stack.json live
#                        (default <repo>/test-results/e2e-composed-stack)
#   POSTGRES_HOST/PORT/DB/USER/PASSWORD   composed Postgres (defaults 127.0.0.1/5433/aveline/aveline)
#   PSQL                 psql client command (default psql; use
#                        `docker exec -i aveline_postgres psql -U aveline -d aveline` on a host
#                        without a client)
#
# Output
# ------
#   `$E2E_STATE_DIR/e2e-composed-stack.json` (mode 0600; it carries secrets) and a redacted
#   copy of the same object on stdout:
#     { organizationId, orgSlug, clerkId, userId, customerId, bearerToken, apiKey,
#       apiKeyScopes, apiBaseUrl, idpBaseUrl, tokenEndpoint, mintedAt }
#   The walk itself uses `bearerToken` only. `apiKey` is an `X-Api-Key` secret carrying
#   `conversations:view` that exists to pin the recorded contract gap (the conversation
#   routes answer it 401) - see docs/tests/e2e-integration.md and
#   tests/e2e/integration/api-key-conversation-gap.spec.ts. Set E2E_SEED_API_KEY=0 to skip it.
#   Its plaintext is kept across re-runs in `$E2E_STATE_DIR/e2e-api-keys.json` (mode 0600).
# =============================================================================
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

E2E_API_BASE_URL="${E2E_API_BASE_URL:-http://127.0.0.1:5091}"
E2E_CLERK_SUB="${E2E_CLERK_SUB:-user_e2e_cross_surface}"
E2E_ORG_SLUG="${E2E_ORG_SLUG:-e2e-cross-surface}"
E2E_ORG_NAME="${E2E_ORG_NAME:-E2E Cross-Surface Boutique}"
E2E_CUSTOMER_PHONE="${E2E_CUSTOMER_PHONE:-+94770000001}"
E2E_STATE_DIR="${E2E_STATE_DIR:-$REPO_ROOT/test-results/e2e-composed-stack}"

POSTGRES_HOST="${POSTGRES_HOST:-127.0.0.1}"
POSTGRES_PORT="${POSTGRES_PORT:-5433}"
POSTGRES_DB="${POSTGRES_DB:-aveline}"
POSTGRES_USER="${POSTGRES_USER:-aveline}"
POSTGRES_PASSWORD="${POSTGRES_PASSWORD:-change-me}"

# psql may be given as a full command (e.g. `docker exec -i aveline_postgres psql -U aveline -d aveline`).
read -r -a PSQL_CMD <<<"${PSQL:-psql}"
if [ "${PSQL_CMD[0]}" = "psql" ]; then
  PSQL_CMD+=(-h "$POSTGRES_HOST" -p "$POSTGRES_PORT" -U "$POSTGRES_USER" -d "$POSTGRES_DB")
  export PGPASSWORD="$POSTGRES_PASSWORD"
fi

log() { printf '[seed-e2e] %s\n' "$*" >&2; }
fail() { printf '[seed-e2e] ERROR: %s\n' "$*" >&2; exit 1; }

psql_scalar() { "${PSQL_CMD[@]}" -tAc "$1"; }
psql_exec() { "${PSQL_CMD[@]}" -v ON_ERROR_STOP=1 -q -c "$1" >/dev/null; }

# --- locate the stub issuer -------------------------------------------------
if [ -z "${E2E_IDP_BASE_URL:-}" ]; then
  if [ -f "$E2E_STATE_DIR/idp.json" ]; then
    E2E_IDP_BASE_URL="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["issuer"])' "$E2E_STATE_DIR/idp.json")"
  else
    fail "E2E_IDP_BASE_URL is unset and $E2E_STATE_DIR/idp.json does not exist. Run 'scripts/e2e-composed-stack.sh up' first, or export E2E_IDP_BASE_URL."
  fi
fi
E2E_IDP_BASE_URL="${E2E_IDP_BASE_URL%/}"

# --- preconditions ----------------------------------------------------------
curl -fsS "$E2E_API_BASE_URL/health/live" >/dev/null 2>&1 \
  || fail "the API is not live at $E2E_API_BASE_URL/health/live. Run 'scripts/e2e-composed-stack.sh up' first."
curl -fsS "$E2E_IDP_BASE_URL/health" >/dev/null 2>&1 \
  || fail "the stub issuer is not live at $E2E_IDP_BASE_URL/health."

# --- mint a real bearer token for the seeded subject ------------------------
TOKEN_JSON="$(curl -fsS -X POST "$E2E_IDP_BASE_URL/token" \
  -H 'Content-Type: application/json' \
  -d "{\"sub\":\"$E2E_CLERK_SUB\"}")" || fail "the stub issuer refused to mint a token."
BEARER_TOKEN="$(python3 -c 'import json,sys;print(json.load(sys.stdin)["access_token"])' <<<"$TOKEN_JSON")"
[ -n "$BEARER_TOKEN" ] || fail "the stub issuer returned an empty access_token."

# --- 1. the Users row (the only path that does not need a Clerk webhook) ----
USER_ID="$(psql_scalar "select \"Id\" from \"Users\" where \"ClerkId\" = '$E2E_CLERK_SUB';")"
if [ -z "$USER_ID" ]; then
  USER_ID="$(python3 -c 'import uuid;print(uuid.uuid4())')"
  psql_exec "insert into \"Users\" (
      \"Id\", \"ClerkId\", \"FirstName\", \"LastName\", \"OrganizationId\", \"Email\",
      \"Username\", \"PhoneNumber\", \"UserRole\", \"OrganizationRole\",
      \"HasCompletedOnboarding\", \"ContactPreference\", \"PushNotificationsEnabled\",
      \"IsActive\", \"CreatedAt\", \"UpdatedAt\", \"AccountState\")
    values (
      '$USER_ID', '$E2E_CLERK_SUB', 'E2E', 'Cross-Surface', '', 'e2e-cross-surface@example.test',
      'e2e-cross-surface', '+94770000000', 'org:boutique_owner', 'org:boutique_owner',
      true, 'None', false,
      true, now(), now(), 'Active')
    on conflict (\"ClerkId\") do nothing;"
  # A concurrent/replayed insert adopts the row that won the unique index.
  USER_ID="$(psql_scalar "select \"Id\" from \"Users\" where \"ClerkId\" = '$E2E_CLERK_SUB';")"
  log "inserted the E2E user for sub=$E2E_CLERK_SUB (id=$USER_ID)"
else
  log "reusing the existing E2E user for sub=$E2E_CLERK_SUB (id=$USER_ID)"
fi

# --- 2. the organisation, through the real API -------------------------------
ORG_ID="$(psql_scalar "select \"Id\" from \"Organizations\" where \"Slug\" = '$E2E_ORG_SLUG';")"
if [ -z "$ORG_ID" ]; then
  ORG_RESPONSE="$(curl -fsS -X POST "$E2E_API_BASE_URL/api/v1/orgs" \
    -H "Authorization: Bearer $BEARER_TOKEN" \
    -H 'Content-Type: application/json' \
    -d "{\"name\":\"$E2E_ORG_NAME\",\"slug\":\"$E2E_ORG_SLUG\"}")" \
    || fail "POST /api/v1/orgs failed for the seeded user; the API answers 404 when the Users row is missing."
  ORG_ID="$(python3 -c 'import json,sys;print(json.load(sys.stdin)["organization"]["id"])' <<<"$ORG_RESPONSE")"
  log "created organization $E2E_ORG_SLUG through POST /api/v1/orgs (id=$ORG_ID)"
else
  log "reusing the existing organization $E2E_ORG_SLUG (id=$ORG_ID)"
fi

# The membership the route created is the authorization input; assert it is Active so a broken
# seed fails here rather than as a 403 inside the spec.
ROLE="$(psql_scalar "select \"BoutiqueRole\" from \"OrganizationMemberships\" where \"OrganizationId\" = '$ORG_ID' and \"UserId\" = '$USER_ID' and \"Status\" = 'Active';")"
[ -n "$ROLE" ] || fail "no active membership for user $USER_ID in organization $ORG_ID after seeding."
log "active membership confirmed: role=$ROLE"

# --- 3. the customer the conversation is opened for --------------------------
CUSTOMER_ID="$(psql_scalar "select \"Id\" from \"Customers\" where \"OrganizationId\" = '$ORG_ID' and \"PhoneNumber\" = '$E2E_CUSTOMER_PHONE';")"
if [ -z "$CUSTOMER_ID" ]; then
  CUSTOMER_ID="$(python3 -c 'import uuid;print(uuid.uuid4())')"
  psql_exec "insert into \"Customers\" (
      \"Id\", \"OrganizationId\", \"PhoneNumber\", \"FullName\", \"Status\",
      \"TotalSpent\", \"VisitCount\", \"CreatedBy\", \"CreatedAt\", \"UpdatedAt\")
    values (
      '$CUSTOMER_ID', '$ORG_ID', '$E2E_CUSTOMER_PHONE', 'E2E Cross-Surface Patron', 'new',
      0, 0, '$USER_ID', now(), now())
    on conflict (\"OrganizationId\", \"PhoneNumber\") do nothing;"
  CUSTOMER_ID="$(psql_scalar "select \"Id\" from \"Customers\" where \"OrganizationId\" = '$ORG_ID' and \"PhoneNumber\" = '$E2E_CUSTOMER_PHONE';")"
  log "inserted the E2E customer (id=$CUSTOMER_ID)"
else
  log "reusing the existing E2E customer (id=$CUSTOMER_ID)"
fi

# --- 4. an X-Api-Key credential, to pin the conversation-route contract gap ----
# `POST …/api-keys` is entitlement-gated (`api.access`, Rose/Enterprise), so on a Seed-plan
# seeded org it answers 403 and is not usable here. The row is inserted directly instead, with
# the same shape and the same SHA-256 hashing the API's own `ApiKeyCredentials` uses
# (`avl_test_<32 base62>`, prefix = first 16 chars, hash = lowercase hex SHA-256). This exists
# only so the E1 finding has a real credential to demonstrate; see
# `tests/e2e/integration/api-key-conversation-gap.spec.ts`.
E2E_API_KEY_NAME="${E2E_API_KEY_NAME:-e1-api-key-finding}"
API_KEY_SCOPES="${E2E_API_KEY_SCOPES:-conversations:view}"
API_KEY_PLAINTEXT=""
if [ "${E2E_SEED_API_KEY:-1}" = "1" ]; then
  API_KEY_PLAINTEXT="$(python3 - "$E2E_STATE_DIR" "$E2E_API_KEY_NAME" <<'PY'
import hashlib, json, os, secrets, string, sys, uuid
state_dir, name = sys.argv[1], sys.argv[2]
secrets_path = os.path.join(state_dir, "e2e-api-keys.json")
store = {}
if os.path.exists(secrets_path):
    store = json.load(open(secrets_path))
if store.get(name):
    print(store[name], end="")
    sys.exit(0)
base62 = string.digits + string.ascii_uppercase + string.ascii_lowercase
plaintext = "avl_test_" + "".join(secrets.choice(base62) for _ in range(32))
store[name] = plaintext
tmp = secrets_path + ".tmp"
with open(tmp, "w", encoding="utf-8") as handle:
    json.dump(store, handle, indent=2)
os.chmod(tmp, 0o600)
os.replace(tmp, secrets_path)
print(plaintext, end="")
PY
)"
  API_KEY_PREFIX="${API_KEY_PLAINTEXT:0:16}"
  API_KEY_HASH="$(python3 -c 'import hashlib,sys;print(hashlib.sha256(sys.argv[1].encode()).hexdigest())' "$API_KEY_PLAINTEXT")"
  scopes_sql="ARRAY[$(python3 -c 'import sys;print(",".join("\x27%s\x27" % s.strip() for s in sys.argv[1].split(",")))' "$API_KEY_SCOPES")]"
  psql_exec "insert into \"ApiKeys\" (
      \"Id\", \"OrganizationId\", \"Name\", \"Prefix\", \"KeyHash\", \"HashAlgorithm\", \"Scopes\",
      \"Environment\", \"Status\", \"CreatedByUserId\", \"CreatedAt\", \"RequestCount\")
    values (
      '$(python3 -c 'import uuid;print(uuid.uuid4())')', '$ORG_ID', '$E2E_API_KEY_NAME', '$API_KEY_PREFIX',
      '$API_KEY_HASH', 'sha256', $scopes_sql, 'Test', 'Active', '$USER_ID', now(), 0)
    on conflict (\"Prefix\") do nothing;"
  log "API key available for the contract-gap spec: $E2E_API_KEY_NAME (scopes: $API_KEY_SCOPES)"
fi

# --- 5. make sure today's telemetry partition exists ---------------------------
# `ApiRequestLogs` is partitioned by day. The migration creates today + tomorrow, and
# `ApiRequestLogPartitionJob` adds tomorrow once a day at 00:05 UTC, so a database that
# outlives a day has no partition for the current date and every request's telemetry write
# fails with `23514: no partition of relation "ApiRequestLogs" found for row`. On some
# handlers that surfaces as a 500 (observed on `POST /internal/customers/memories/search`).
# The function is the migration's own; calling it is idempotent and cheap, and it keeps a
# long-lived local volume from turning into a false failure in the walk.
psql_exec "select aveline_ensure_api_request_log_partition((now() at time zone 'utc')::date);" 2>/dev/null \
  && log "today's ApiRequestLogs partition ensured" \
  || log "could not ensure the ApiRequestLogs partition (non-Postgres or missing function); continuing"

# --- output -----------------------------------------------------------------
mkdir -p "$E2E_STATE_DIR"
OUT="$E2E_STATE_DIR/e2e-composed-stack.json"
python3 - "$OUT" "$ORG_ID" "$E2E_ORG_SLUG" "$E2E_CLERK_SUB" "$USER_ID" "$CUSTOMER_ID" \
  "$BEARER_TOKEN" "$E2E_API_BASE_URL" "$E2E_IDP_BASE_URL" "$API_KEY_PLAINTEXT" "$API_KEY_SCOPES" <<'PY'
import json, sys, time
(out, org_id, slug, clerk_id, user_id, customer_id, token, api, idp,
 key_plaintext, key_scopes) = sys.argv[1:12]
state = {
    "organizationId": org_id,
    "orgSlug": slug,
    "clerkId": clerk_id,
    "userId": user_id,
    "customerId": customer_id,
    "bearerToken": token,
    # Present only to demonstrate the API-key/conversation-route contract gap; the walk itself
    # uses the bearer token.
    "apiKey": key_plaintext or None,
    "apiKeyScopes": key_scopes.split(","),
    "apiBaseUrl": api,
    "idpBaseUrl": idp,
    "tokenEndpoint": f"{idp}/token",
    "mintedAt": int(time.time()),
}
tmp = out + ".tmp"
with open(tmp, "w", encoding="utf-8") as handle:
    json.dump(state, handle, indent=2)
    handle.write("\n")
import os
os.chmod(tmp, 0o600)
os.replace(tmp, out)
json.dump({k: v for k, v in state.items() if k not in {"bearerToken", "apiKey"}}, sys.stdout, indent=2)
sys.stdout.write("\n")
PY
