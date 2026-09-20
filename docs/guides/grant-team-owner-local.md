# Granting an Aveline team role without an existing admin (local dev)

`AdminApprovalService.ApproveAsync` requires an *existing* administrator to review a
request (`AdminReviewPolicy`) and it hardcodes the `admin` team role. When no admin
exists (or you need `owner` instead of `admin`), the grant must be done out-of-band.

## Roles do not live in PostgreSQL

Authorization reads the Clerk JWT, not the database:

- `Authorization/RoleClaimNormalizer.cs` promotes the `user_role` / `org_role` claims
  (minted from `user.public_metadata.role` by the `jwt-aveline-v1` template) into
  `ClaimTypes.Role`.
- `Authorization/PermissionAuthorizationHandler.cs` evaluates those claims.
- `Users.UserRole` is only a **read model**: `UserService.ApplyClaimContext` and
  `ClerkWebhookSyncService.UpsertUserAsync` overwrite it from Clerk.

So `UPDATE "Users" SET "UserRole" = 'owner'` on its own changes nothing: the role still
comes from Clerk. Always set Clerk first, then mirror the row.

`owner` is the superset role (`Authorization/Permissions.cs`: `[Roles.Owner] = All`).

## Preconditions

The Clerk user must already exist. Complete the (now fixed) `/sign-up/admin` flow, or
create the user directly:

```bash
set -a; . ./.env.local; set +a   # provides CLERK_SECRET_KEY

# Passwordless create; the user later sets a password via "Forgot password".
# The team role is applied by the "Grant the role" step below, not at creation time.
curl -s -X POST https://api.clerk.com/v1/users \
  -H "Authorization: Bearer $CLERK_SECRET_KEY" -H "Content-Type: application/json" \
  -d '{"email_address":["www.kumari2002@gmail.com"],"first_name":"Kumari","last_name":"",
       "skip_password_requirement":true}'
```

## Grant the role

```bash
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
set -a; . ./.env.local; set +a

EMAIL="www.kumari2002@gmail.com"
ROLE="owner"          # or: staff | customer_relations | moderator | admin | owner

# 1. Resolve the Clerk user id.
CLERK_ID=$(curl -s -G https://api.clerk.com/v1/users \
  --data-urlencode "email_address=$EMAIL" \
  -H "Authorization: Bearer $CLERK_SECRET_KEY" \
  | python3 -c "import sys,json; us=json.load(sys.stdin); print(us[0]['id'] if us else '')")

[ -n "$CLERK_ID" ] || { echo "No Clerk user for $EMAIL — complete sign-up first." >&2; exit 1; }
echo "Clerk user: $CLERK_ID"

# 2. Grant in Clerk (the source of truth for the user_role claim).
#    NOTE: PATCH /users/{id} no longer accepts public_metadata; use /metadata.
curl -s -X PATCH "https://api.clerk.com/v1/users/$CLERK_ID/metadata" \
  -H "Authorization: Bearer $CLERK_SECRET_KEY" -H "Content-Type: application/json" \
  -d "{\"public_metadata\":{\"role\":\"$ROLE\"}}" >/dev/null
echo "Clerk public_metadata.role=$ROLE"

# 3. Mirror the local read model and mark the access request approved
#    (Status 0=Pending, 1=Approved, 2=Rejected). The Clerk id is a safe token, so it is
#    inlined: psql -v interpolation does not apply to this docker-exec heredoc.
docker exec aveline_postgres psql -U aveline -d aveline -v ON_ERROR_STOP=1 -P pager=off -c "
UPDATE \"Users\" SET \"UserRole\"='$ROLE', \"UpdatedAt\"=now() WHERE \"ClerkId\"='$CLERK_ID';
UPDATE \"AdminApprovalRequests\" SET \"Status\"=1, \"ReviewedAt\"=now()
  WHERE \"ClerkUserId\"='$CLERK_ID' AND \"Status\"=0;
"

# 4. Drop the cached read model so the next request re-reads the row.
docker exec aveline_redis redis-cli DEL "user:onboarding:$CLERK_ID" "user:profile:$CLERK_ID"

echo "Done. Sign out and back in so a fresh JWT carries user_role=$ROLE."
```

## Make the account usable by the admin console

`Common/Middleware/OnboardingMiddleware.cs` normally only lets `AccountState.Active`
accounts reach `/api/v1/admin/*`. The Aveline **console roles** (`Roles.OnboardingExemptRoles`:
`moderator`, `admin`, `owner`) are now exempted from that gate, because they operate the
platform rather than a boutique. (`staff` and `customer_relations` remain gated, as do the
`org:*` boutique roles.) Suspended accounts are never exempted. So once the role above is
granted, the account can use the console while its `AccountState` remains
`OnboardingPending` — no database change is required.

If you are on a build without that exemption, or need a non-team role to reach tenant
endpoints, give the account a legacy org context (this makes `ResolveAccountStateAsync`
return `Active`):

```sql
-- Does NOT grant boutique org-scope permissions: those require a real
-- OrganizationMemberships row (see OrganizationScopeAuthorizationHandler).
UPDATE "Users"
SET "OrganizationId" = '01a078bf-45c1-7231-bae1-3e0ea1dc0471',
    "AccountState"   = 'Active',
    "UpdatedAt"      = now()
WHERE "ClerkId" = '<CLERK_ID>';
```

If the account must also act *within* that boutique, add a membership instead of (or in
addition to) the legacy column:

```sql
INSERT INTO "OrganizationMemberships"
  ("OrganizationId", "UserId", "BoutiqueRole", "Status", "CreatedAt", "UpdatedAt")
SELECT '01a078bf-45c1-7231-bae1-3e0ea1dc0471', u."Id", 'org:boutique_owner', 'Active', now(), now()
FROM "Users" u WHERE u."ClerkId" = '<CLERK_ID>';
```

## Rollback

```bash
curl -s -X PATCH "https://api.clerk.com/v1/users/$CLERK_ID/metadata" \
  -H "Authorization: Bearer $CLERK_SECRET_KEY" -H "Content-Type: application/json" \
  -d '{"public_metadata":{"role":null}}'

docker exec aveline_postgres psql -U aveline -d aveline -c \
  "UPDATE \"Users\" SET \"UserRole\"='user' WHERE \"ClerkId\"='$CLERK_ID';"
docker exec aveline_redis redis-cli DEL "user:onboarding:$CLERK_ID" "user:profile:$CLERK_ID"
```

## Notes for the running stack

- The `jwt-aveline-v1` template now also mints `email`, `first_name` and `last_name`, so
  new `Users` stubs and `AdminApprovalRequests` are populated. `UserService.ApplyClaimContext`
  backfills those onto existing rows on the next request.
- Administrator sign-ups are routed to `/admin/pending` instead of the onboarding wizard,
  and that screen re-queues the access request (the submit endpoint is idempotent).
- The `aveline_api` container has no `Clerk:SecretKey` (compose does not pass one), so the
  normal `/api/v1/admin/requests/{id}/approve` endpoint cannot call Clerk locally. Use the
  script above with the key from `.env.local`, or add `Clerk__SecretKey` to the API service.
