# Aveline API — Endpoint Catalog

**Audience:** frontend developers (React dashboard, Flutter associate app) and
backend implementers.
**Spec:** [`openapi.yaml`](openapi.yaml) — OpenAPI 3.0.3, machine-readable and validated.
**Version:** API `v1` · document revision `2026-09-11`

---

## 1. How to use this document

### 1.1 Scope

| Part | Contents | Status |
| --- | --- | --- |
| **Part A** | Conventions every endpoint obeys — auth, errors, pagination, idempotency, rate limits | Shipped |
| **Part B** | Endpoints that exist in the codebase today | **Shipped** — safe to integrate now |
| **Part C** | Endpoints this plan adds | **Planned** — do not integrate yet |

Every Part C endpoint carries a **`Phase`** marker indicating when it will exist.
If a frontend team needs to build against a Part C endpoint early, the shapes in
this document are the contract to code against; the endpoint URL will not change.

### 1.2 Base URL and versioning

```
Local        http://localhost:5091
Production   from `docs/deployment.md`
```

All user-facing routes are prefixed **`/api/v1`**. There is **no versioning
library** — the prefix is a literal group (`Aveline.Api/Program.cs:95`). A future
`v2` would be a second literal group; no client-side negotiation is performed.

Internal routes (`/internal/**`) are **not** versioned and are **not** callable by
frontend clients — they require the `X-Internal-Token` header and are consumed by
the Python agent service and administrative tooling only.

Health endpoints are at the root: `/health`, `/health/live`, `/health/ready`.

### 1.3 Reconciliation with `docs/OpenApi/`

`docs/OpenApi/README.md:27` states: *"Never hand-edit the exported spec — it is
generated from the code."* That rule is correct for **implemented** endpoints, and
the generated document is served at `/openapi/v1.json` in Development
(`Aveline.Api/Program.cs:79`).

`docs/api/openapi.yaml` is the hand-maintained contract. **It is normative for
shipped client-facing endpoints:** every `/api/v1` route the API maps must appear
in it with the shipped method, policy and shapes, with three deliberate
exclusions — the `/internal/**` service-to-service surface, the Development-only
`/api/v1/policies/**` demo routes, and the SignalR hubs
(`/hubs/notifications`, `/hubs/conversations`), none of which belong in a client
contract or are expressible as OpenAPI 3.0.3 operations. Those routes are
documented in prose in this README instead. Endpoints that have no route are
**not** part of the normative contract — they are listed only in the fenced
**Appendix P — Planned / not yet implemented** at the end of this document (and in
the commented YAML sections of `docs/api/openapi.yaml`). The reconciliation
procedure is:

| Situation | Source of truth |
| --- | --- |
| Endpoint is shipped | **Generated** `/openapi/v1.json` wins. `docs/api/openapi.yaml` must be diffed against it each phase. |
| Endpoint is planned / not yet implemented | Listed only in the fenced Appendix P; it is **not** normative and must not be counted as "documented but missing". |
| They disagree about a shipped endpoint | The generated document is correct; **fix `docs/api/openapi.yaml` in the same PR**, not later. |

A CI check that diffs the two documents for shipped endpoints should be added.

### 1.4 Endpoint template

Where a Part C endpoint is documented in full, the sections are:

`Method + path` · `Purpose` · `Auth` · `Permissions` · `Params` · `Body` ·
`Response` · `Errors` · `Pagination/Filter/Sort` · `Rate limit` ·
`Idempotency` · `Example` · `Related statistics` · `Notes`

---

## Part A — Conventions

### A.1 Authentication

Three schemes. A route uses exactly one.

| Scheme | Header | Used by | Token lifetime |
| --- | --- | --- | --- |
| **Clerk JWT** (default) | `Authorization: Bearer <jwt>` | React dashboard, Flutter app | Managed by Clerk SDK |
| **Internal service** | `X-Internal-Token: <secret>` | Python agent service, admin tooling | Static shared secret |
| **API key** *(new, Phase 3)* | `X-Api-Key: avl_live_<32 chars>` | Customer server-to-server integrations (Rose plan) | Until revoked or expired |

**Clerk JWT details.** Validated against the Clerk JWKS for `Clerk:Authority`
(signature, `iss`, `exp`, `nbf`). **`aud` is deliberately not validated**
(`Aveline.Api/Configurations/AuthenticationConfiguration.cs:88-100`, rationale in
`ADR-008`).

Claims read by the API:

| Claim | Meaning | Canonical values |
| --- | --- | --- |
| `sub` / `nameidentifier` | Clerk user id | `user_...` |
| `user_role` | Aveline team role | `staff`, `customer_relations`, `moderator`, `admin`, `owner` |
| `org_role` | Current boutique role | `org:boutique_staff`, `org:boutique_manager`, `org:boutique_supervisor`, `org:boutique_owner` |
| `org_id` | Clerk organization id | `org_...` |
| `org_slug` | Clerk organization slug | string |
| `email` | Email | string |

Both `user_role` and `org_role` are promoted to standard role claims at
authentication time and **lower-cased**
(`Aveline.Api/Authorization/RoleClaimNormalizer.cs:12,21-27`).

**SignalR.** Hubs accept the token from `?access_token=` when no `Authorization`
header is present, but only for paths beginning `/hubs`
(`AuthenticationConfiguration.cs:50-61`).

**Account-state gate.** Every authenticated request passes through
`OnboardingMiddleware` (Phase 0 keeps it, placed after authorization):

| `AccountState` | Behaviour |
| --- | --- |
| `Active` | Allowed |
| `OnboardingPending` | Allowed only on profile (`/api/v1/users/me*`), onboarding-wizard (`/api/v1/users/onboarding`, `/api/v1/onboarding`), invitation (`/api/v1/invitations`) and auth-claims (`/api/v1/auth/claims`) routes, plus the single exact path `POST /api/v1/admin/requests`. Raw `/api/v1/orgs*` routes are deliberately **excluded**, and every other path is **403 `onboarding-required`** (`OnboardingMiddleware.cs`) |
| `Suspended` | **403 `account-suspended`** everywhere |

Responses carry `X-Account-State` and `X-Completed-Onboarding` headers, exposed
through CORS (`Aveline.Api/Configurations/CorsConfiguration.cs:33-38`).

**Client guidance:** branch on `X-Account-State`, not on a client-side role check.
The server is authoritative — `GET /api/v1/auth/claims` returns the resolved state
for debugging.

### A.2 Authorization

Authorization is two-layered and **fails closed**.

**Layer 1 — role/permission policies.** Named policies, never raw role strings in
endpoint code:

| Policy constant | Requirement |
| --- | --- |
| `Associates` | any staff role (9 values) |
| `Managers` | manager-and-above (6 values) |
| `Owners` | `owner`, `org:boutique_owner` |
| `AdminReview` | `moderator`, `admin`, `owner` |
| `BoutiqueAccess` | authenticated **and** an `Active` membership in the org in the route that grants `catalog:view` |
| `BoutiqueMember` | authenticated **and** an `Active` membership in the org — **no permission**. The gate a route uses when it means "an active member" rather than borrowing `catalog:view` |
| `BoutiqueMembershipManage` | same, grants `settings:manage` (the organisation profile and Integrations) |
| `BoutiqueTeamManage` | same, grants `team:manage` (invitations, the member list, role changes, suspend/activate, removal) |
| `BoutiqueCatalogManage` | same, grants `catalog:manage` (create/edit/publish/delete a catalogue item) |
| `BoutiqueCustomerManage` | same, grants `customers:manage` (edit and soft-delete a client record) |
| `BoutiqueOrderManage` | same, grants `orders:manage` (order update/status/cancel/recalculate and business-rule writes) |
| `BoutiqueReportsView` | same, grants `reports:view` (the shop's KPIs, revenue series and income ledger) |
| `BoutiqueConversationAccess` | same, grants `conversations:view` |
| `BoutiqueCustomerAccess` | same, grants `customers:view` (the tenant customer surface: the client book, a client profile and Home's client highlights) |
| `InternalServicePolicy` | `X-Internal-Token` + role `InternalService` |
| `StatsSystemPolicy` | team roles `owner`/`admin` **and** `stats:system` |
| `AuditViewPolicy` | team roles `owner`/`admin` **and** `audit:view` |
| `PricingAdminReadPolicy` | team roles `owner`/`admin` **and** `pricing:view` |
| *one per permission* | policy name **is** the permission string, e.g. `catalog:view` |

> **A9 (B2).** The three team-only policies above previously guarded their routes with
> `RequireRole(owner, admin)` only, so the `stats:system`, `audit:view` and
> `pricing:view` permission policies were registered but referenced by no route. Each
> now also carries its permission requirement. The change is **additive and inert**:
> `owner` and `admin` already hold all three permissions, so no role that passed
> before is refused now, and the permission catalogue matches the wire.

**Layer 2 — organization scope.** For any route containing
`{organizationId:guid}`, `OrganizationScopeAuthorizationHandler` resolves the
caller from `sub`, loads the `OrganizationMembership`, requires
`Status = Active`, and checks that the membership's `BoutiqueRole` grants the
required permission. A still-valid JWT carrying stale org claims **cannot** cross
organizations (`Authorization/OrganizationScopeAuthorizationHandler.cs:38-74`).

**Permission catalog (current, 31):** the eight original
(`catalog:view`, `customers:view`, `catalog:manage`, `approvals:approve`,
`payments:refund`, `reports:view`, `settings:manage`, `conversations:view`), the
billing and pricing family (`billing:view`, `billing:view:self`, `billing:manage`,
`billing:adjust`, `pricing:view`, `pricing:manage`, `pricing:backdate`), the
API-access pair (`apikeys:view`, `apikeys:manage`), statistics
(`stats:view`, `stats:view:agent`, `stats:system`, `analytics:business:read`),
team administration (`admin:users:read`, `admin:users:manage`, `admin:orgs:read`,
`audit:view`), the revenue family (`revenue:read`, `revenue:manage`,
`revenue:refund`), and the three the tenant-dashboard slice adds:
`customers:manage`, `team:manage`, `orders:manage`.

**Role → permission grants (current, from `Authorization/Permissions.cs`).**
`admin` holds every permission except `pricing:backdate` and `revenue:refund`;
`owner` holds every permission.

| Role | Permissions |
| --- | --- |
| `staff` | `catalog:view`, `conversations:view` |
| `customer_relations` | `catalog:view`, `customers:view`, `conversations:view` |
| `moderator` | `catalog:view`, `customers:view`, `approvals:approve`, `conversations:view`, `billing:view`, `stats:view`, `stats:view:agent`, `admin:orgs:read`, `analytics:business:read`, `revenue:read` |
| `org:boutique_staff` | `catalog:view`, `customers:view`, `conversations:view`, `billing:view:self`, `approvals:approve` |
| `org:boutique_manager` | `catalog:view`, `customers:view`, `catalog:manage`, `customers:manage`, `team:manage`, `orders:manage`, `reports:view`, `conversations:view`, `billing:view`, `billing:view:self`, `pricing:view`, `stats:view` |
| `org:boutique_supervisor` | manager grants + `approvals:approve` |
| `org:boutique_owner` | every permission that reaches a boutique: adds `payments:refund`, `settings:manage`, `billing:manage`, `apikeys:view`, `apikeys:manage` |

> **Tenant-dashboard slice (T0a).** Three permissions were added and one grant widened:
> `customers:manage` (nothing correct existed to gate a client write on, because
> `customers:view` is held by every role), `team:manage` (a manager may manage staff
> without also being handed `settings:manage`, which reaches Integrations and its
> WhatsApp/payment-gateway credentials), and `orders:manage` (staff may approve a
> customer order but may not change an order's lifecycle or edit the business rules
> that gate discounts). `approvals:approve` is now granted to `org:boutique_staff`,
> and the approval controller splits its verbs so that grant cannot also cancel an
> order.

**Permissions added by this plan (14, Phase 0–3):** `billing:view`,
`billing:manage`, `billing:adjust`, `pricing:view`, `pricing:manage`,
`pricing:backdate`, `apikeys:view`, `apikeys:manage`, `stats:view`,
`stats:view:agent`, `stats:system`, `admin:users:read`, `admin:users:manage`,
`admin:orgs:read`, `audit:view`.

**`analytics:business:read` (Business KPIs).** Guards the six administrator
business-KPI reads under `/api/v1/admin/statistics/business/*` — growth, active users,
plan mix, subscription trend, usage and the organization ranking. It is deliberately
**separate from `stats:system`**, which is role-guarded to `owner`/`admin` and is about
system health rather than growth. Granted to `moderator`, `admin` and `owner`; denied to
`staff`, `customer_relations` and every `org:boutique_*` role. The routes are
**bearer-only** — the group does not call `AllowBearerOrApiKey`, so an API key is refused —
matching the other team-only statistics families. Two of the six reads
(`usage?organizationId=…` and the organization ranking) additionally require
`admin:orgs:read`, checked in the handler rather than in the policy. See
[`docs/architecture/authorization.md`](../architecture/authorization.md) for the grant
matrix.

### A.3 Error responses

A single global exception handler (`GlobalExceptionHandler`, an `IExceptionHandler`)
is registered **outermost** at `Aveline.Api/Program.cs:40,99`, so every unhandled
exception becomes a stable `500` envelope
(`{ "status": 500, "message": "...", "traceId": "..." }`). The exception type,
message and stack trace are logged server-side and never serialised; correlate the
client-visible `traceId` with the server log. Everything that is not an unhandled
exception is still translated by the endpoint itself. The shapes are:

| Status | Body | Note |
| --- | --- | --- |
| `400` validation | `ValidationProblemDetails` — `{ "type", "title", "status", "errors": { "<field>": ["<msg>"] } }` | From `Results.ValidationProblem` |
| `400` other | `{ "message": "..." }` | The dominant convention |
| `401` | **empty body** | `Results.Unauthorized()` |
| `403` | **empty body**, or a `ProblemDetails` for the account-state gate | `Results.Forbid()` or `OnboardingMiddleware` |
| `403` account state | `{ "type": "https://aveline.app/errors/onboarding-required" \| ".../account-suspended", "title", "status", "detail" }` | `application/problem+json` |
| `404` | `{ "message": "..." }` | |
| `409` | `{ "message": "..." }` | |
| `429` | `{ "message": "..." }` **or** empty | Two shapes exist today |
| `500` | `{ "status": 500, "message": "An unexpected error occurred while processing the request.", "traceId": "..." }` | `GlobalExceptionHandler`; the detail is log-only |
| `502` / `503` | `{ "message": "..." }` | Agent/Clerk proxy failures |

> **Known inconsistency (defect D-10).** Internal endpoints under `/internal/*`
> return `{ "error": "..." }` instead of `{ "message": ... }`
> (`Modules/Billing/Endpoints/UsageEndpoints.cs:58`). Frontend clients never call
> `/internal/*`, so this does not affect them. **Part C endpoints always use
> `{ "message": ... }`.**

**Recommended client error handling:**

```ts
type ApiValidationError = { type: string; title: string; status: number; errors: Record<string, string[]> };
type ApiMessageError    = { message: string };

if (res.status === 401) return signOut();          // body is empty; do not parse
if (res.status === 403) return showUpgradeOrPermissions(); // body may be empty
const body = await res.json().catch(() => ({}));
const msg  = body.message ?? body.detail ?? `Request failed (${res.status})`;
const fieldErrors = body.errors as Record<string, string[]> | undefined;
```

**Phase 0 addition.** A correlation id is added to every response as
`X-Request-Id`, and every Part C error body additionally carries `requestId`. The
status codes and the `message` key do **not** change.

### A.4 Pagination

Convention, verified across `ConversationEndpoints`, `NotificationEndpoints`:

| Param | Type | Default | Bounds |
| --- | --- | --- | --- |
| `page` | integer | `1` | `>= 1` (`Math.Max(page, 1)`); upper-bounded at `10 000` on the admin read endpoints |
| `pageSize` | integer | `50` | `1..200`; values **below** `1` fall back to the default `50` rather than clamping to `1` |

Envelope — note the property is **`total`**, not `totalCount`:

```json
{
  "items": [ /* ... */ ],
  "total": 137,
  "page": 2,
  "pageSize": 50
}
```

**Known deviations** (existing code, frontend must special-case):

| Endpoint | Deviation |
| --- | --- |
| `GET /internal/usage/records/{orgId}` | Bare array, no total, `page` not clamped, only `pageSize` clamped |
| `POST /internal/visual/inventory/search` | Paging in the body, defaults `page=1`, `pageSize=20`, no 200 clamp |
| `GET /api/v1/orgs/{organizationId}/statistics/**` (Part C) | Follows the standard envelope **exactly** |
| `GET /admin/audit`, `GET /admin/orgs`, `GET /admin/users` | `pageSize < 1` falls back to the default `50` instead of clamping to `1` (`UserService.cs`, `AuditEndpoints.cs`, `AdminOrganizationEndpoints.cs`) |

**Part C rule:** every paginated response uses the envelope, `total`, and the
bounds above. New endpoints never return a bare array.

### A.5 Idempotency

| Category | Behaviour |
| --- | --- |
| `GET`, `HEAD` | Inherently idempotent |
| `PUT`, `DELETE` | Idempotent by definition; repeated calls return the same result |
| `POST` — safe to repeat | Behave idempotently by design (`POST /notifications/read-all`, `POST /invitations/accept`, `POST /orgs/{id}/invitations/{iid}/revoke`) |
| **`POST` — money-shaped (Part C)** | **Require the `Idempotency-Key` header** |

**`Idempotency-Key` contract (Phase 2):**

```
Idempotency-Key: <opaque string, 1..128 chars, [A-Za-z0-9._:-]>
```

| Situation | Response |
| --- | --- |
| First request | Normal `200`/`201`; the response is stored |
| Replay, same key, same body | The **byte-identical** original response, plus `Idempotency-Replayed: true` |
| Replay, same key, **different** body | `409 { "message": "...", "code": "idempotency-key-reuse" }` |
| Concurrent request, same key, original still in flight | `409 { "message": "...", "code": "idempotency-key-in-flight" }` |
| Lease store unreachable | `503 { "message": "...", "code": "idempotency-unavailable" }` — the request is **not executed** |
| Missing header on a money-shaped POST | `400 { "message": "The Idempotency-Key header is required.", "code": "idempotency-key-required" }` |
| Retained for | `Billing:IdempotencyRetentionHours`, default 24 h |

Concurrent requests carrying the same key are serialised by a per-key lease, so the
endpoint executes exactly once: the loser waits for the winner (up to
`Billing:IdempotencyLeaseWaitSeconds`, default 10 s) and then replays its stored
response. The `409 idempotency-key-in-flight` body is returned only when the winner is
still running after that budget expires. The lease **fails closed**: if the lock store
is unreachable the request is refused with `503 idempotency-unavailable` rather than
executed without the exactly-once guard.

**Client guidance:** generate a UUIDv4 per logical operation and reuse it across
retries. Do **not** reuse a key for a genuinely different operation. Endpoints
requiring the header are marked **Idempotency: required** below.

### A.6 Rate limits

| Mechanism | Scope | Behaviour when exceeded |
| --- | --- | --- |
| Request rate limit | Per client IP (invitation accept, 10/min default via `Invitations:AcceptRateLimit`) and per org+IP (WhatsApp webhook, 120/min hard-coded) | `429 { "message": "..." }` |
| **Quota** (Part C, Phase 5) | Per org and per API key, per month/day | `429` with `{ "message", "quota": { "metricKey", "limit", "used", "resetsAt" } }` |
| Per-API-key rate limit (Phase 3) | Per key, per plan tier | `429` with `Retry-After` |

> **Known limitation.** The current limiter
> (`Infrastructure/RateLimiting/DistributedRateLimiter.cs`) is a non-atomic
> read-modify-write over Redis and **fails open** if Redis is unavailable. It is
> adequate for abuse protection and **not** adequate for billing. Part C quota
> counters use an atomic Redis increment and fail closed. Frontend should treat a
> `429` as retryable but must not assume the limit is exact.

### A.7 Correlation, caching, and CORS

| Header | Direction | Meaning |
| --- | --- | --- |
| `X-Request-Id` | request + response | Correlation id. Send your own to correlate client and server logs. Must be `1..128` chars of `[A-Za-z0-9._:-]`, otherwise `400`. |
| `X-Trace-Id` | response | Present when an OpenTelemetry trace exists |
| `X-Account-State` | response | `Active` \| `OnboardingPending` \| `Suspended` |
| `X-Completed-Onboarding` | response | `true` \| `false` |
| `Idempotency-Replayed` | response | `true` when a replay store answered |
| `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` | response | Always `nosniff`, `DENY`, `no-referrer` |

**CORS.** One policy, `aveline-cors`, configured by `Cors:AllowedOrigins`
(default `http://localhost:5173`). It allows credentials, any header, any method,
and exposes exactly `X-Completed-Onboarding` and `X-Account-State`. **Phase 0 adds
`X-Request-Id`, `X-Trace-Id`, and `Idempotency-Replayed` to the exposed set** — the
Flutter app is unaffected (no CORS), but the React dashboard cannot read them
until then.

**Caching.** Statistics responses are safe to cache briefly: use
`Cache-Control: private, max-age=15` for system overview and 60 s for everything
else. **The Blossom balance must not be cached by the client** — it is the most
correctness-sensitive value in the product.

### A.8 Conventions reference for implementers

| Concern | Establishment | Source |
| --- | --- | --- |
| Module DI + routes | `Add{Name}Module()` / `Map{Name}Endpoints()` | `Modules/Billing/BillingModule.cs:12-24` |
| Route group | `endpoints.MapGroup("...")`, then `.WithTags/.WithName/.WithSummary/.Produces<T>` | `Endpoints/ConversationEndpoints.cs:20-74` |
| Tenant route | **must** contain `{organizationId:guid}` | `Authorization/OrganizationScopeAuthorizationHandler.cs:38-43` |
| Tenant query filter | **manual** `.Where(x => x.OrganizationId == orgId)` — there is **no EF global tenant filter** | `Modules/Billing/Repositories/UsageRepository.cs:116` |
| DTOs | `sealed record` + static `From(entity)` | `Modules/Conversations/DTOs/ConversationDtos.cs:6-21` |
| Enum serialisation | `JsonStringEnumConverter` globally (`Program.cs:28-31`), so enums are strings | — |

### A.9 Path notation in this document

This document and `openapi.yaml` use **two different but equivalent path styles**.
This is deliberate and is not an inconsistency to report.

| Style | Example | Where used |
| --- | --- | --- |
| ASP.NET route constraint | `/api/v1/orgs/{organizationId:guid}/usage` | This README, and the actual implementation |
| Plain OpenAPI parameter | `/api/v1/orgs/{organizationId}/usage` | `openapi.yaml`, and generated clients |

The `:guid` suffix is an ASP.NET Core route constraint that rejects a
non-Guid segment with `404` **before** authorization runs. It has no effect on the
wire format of the URL. Always send a Guid.

Grouped sections also use a relative shorthand: `#### GET /requests` inside the
**API consumption** section means
`GET /api/v1/orgs/{organizationId:guid}/statistics/api/requests`. Each group states
its base path.

**To find an endpoint:** search this README for the last segment (for example
`/burn-rate`), or search `openapi.yaml` for the fully-qualified path.

---

## Part B — Existing endpoints

All Part B endpoints are implemented. Response shapes were read from the DTOs
cited.

### B.1 Auth

| Method | Path | Auth | Notes |
| --- | --- | --- | --- |
| `GET` | `/api/v1/auth/claims` | authenticated | Raw Clerk claims plus the resolved `AccountState`, `UserRole`, `OrganizationRole`. `email` is read from the mapped `ClaimTypes.Email` claim (falling back to the raw `email` name); the JwtBearer inbound map rewrites the token's `email` claim, so reading the raw name alone returned `null` (**A9 B1**). Source: `Endpoints/AuthEndpoints.cs:16`. |

### B.2 Users

#### `GET /api/v1/users/me`

**Auth:** authenticated. **Purpose:** the caller's Aveline user record.
**Response `200`:** `UserDto`.

```json
{
  "id": "0198f3c2-...",
  "clerkId": "user_2abc...",
  "email": "owner@boutique.lk",
  "firstName": "Nirmal",
  "lastName": "Perera",
  "displayName": null,
  "username": "nirmal",
  "phoneNumber": "+94771234567",
  "address": null,
  "profileImageUrl": null,
  "userRole": "owner",
  "organizationRole": "org:boutique_owner",
  "organizationId": "0198f3c2-...",
  "hasCompletedOnboarding": true,
  "accountState": "Active",
  "contactPreference": "Email",
  "pushNotificationsEnabled": true,
  "isActive": true,
  "createdAt": "2026-09-01T10:00:00Z",
  "updatedAt": "2026-09-10T08:00:00Z"
}
```

**Errors:** `401` empty; `404 { "message": "User record does not exist in Aveline database." }`.
**Source:** `Endpoints/UserEndpoints.cs:16`, DTO `Modules/Shared/DTOs/UserDto.cs`.

#### `POST /api/v1/users/onboarding`

**Auth:** authenticated. **Body:** `CompleteOnboardingRequest`.
**Source:** `Endpoints/UserEndpoints.cs:35`. Returns `400 ValidationProblem` on
field errors (`Endpoints/UserEndpoints.cs:53`).

#### `POST /api/v1/users/me/devices`

Register a push token. **Body:** `{ "token": string, "platform": "Ios" | "Android" | "Web" }`.
**Response `200`:** an empty body (the handler returns `Results.Ok()` with no payload).
**Errors:** `400` missing token, `401` empty, `404` no user record.
**Source:** `Endpoints/DeviceTokenEndpoints.cs:20`.

#### `DELETE /api/v1/users/me/devices/{token}`

Unregister a push token. **Response `204`.** **Errors:** `401` empty, `404` no user record.
**Source:** `Endpoints/DeviceTokenEndpoints.cs:55`.

### B.3 Organizations

| Method | Path | Policy | Request | Response |
| --- | --- | --- | --- | --- |
| `POST` | `/api/v1/orgs` | authenticated | `CreateOrganizationRequest { name, slug? }` | `{ organization: {...}, accountState }` |
| `GET` | `/api/v1/orgs/my` | authenticated | — | `OrganizationMembershipView[]` |
| `GET` | `/api/v1/orgs/by-slug/{slug}` | authenticated | — | `{ organization, membership }` or `404` |
| `GET` | `/api/v1/orgs/{organizationId:guid}` | `BoutiqueAccess` | — | `OrganizationProfileDto` or `404` |
| `POST` | `/api/v1/orgs/{organizationId:guid}/members/{userId:guid}/suspend` | `BoutiqueTeamManage` | — | `{ organizationId, userId, boutiqueRole, status }` |
| `POST` | `/api/v1/orgs/{organizationId:guid}/members/{userId:guid}/activate` | `BoutiqueTeamManage` | — | same |
| `DELETE` | `/api/v1/orgs/{organizationId:guid}/members/{userId:guid}` | `BoutiqueTeamManage` | — | `204` |

**Important:** `ClerkOrgId` is **never** accepted from the request body — it is
derived from the JWT `org_id` claim (`Endpoints/OrganizationEndpoints.cs:169-178`).
A client cannot bind a boutique to a Clerk org it does not control.

**Tenant isolation:** `GET /orgs/by-slug/{slug}` returns `404` for both "no such
slug" and "exists but you are not a member", deliberately, to prevent
boutique-existence enumeration (`Endpoints/OrganizationEndpoints.cs:264-271`).

**Errors:** `400` invalid body / not-invitable role; `400` cannot manage the owner's
membership; `401`; `403`; `404`; `409` slug collision.
**Source:** `Endpoints/OrganizationEndpoints.cs:146-345`,
DTOs `Modules/Organizations/DTOs/OrganizationDtos.cs`.

### B.4 Invitations

| Method | Path | Policy | Body | Response |
| --- | --- | --- | --- | --- |
| `POST` | `/api/v1/orgs/{organizationId:guid}/invitations` | `BoutiqueTeamManage` | `{ boutiqueRole, recipientEmail?, validityHours?, sendSummaryToOwner? }` + **`Idempotency-Key`** | `CreateInvitationResponse` |
| `POST` | `/api/v1/orgs/{organizationId:guid}/invitations/bulk` | `BoutiqueTeamManage` | `{ boutiqueRole, count, validityHours?, sendSummaryToOwner? }` + **`Idempotency-Key`** | `BulkCreateInvitationResponse` |
| `GET` | `/api/v1/orgs/{organizationId:guid}/invitations` | `BoutiqueTeamManage` | — | `PendingInvitationDto[]` |
| `POST` | `/api/v1/orgs/{organizationId:guid}/invitations/{invitationId:guid}/revoke` | `BoutiqueTeamManage` | — | `204` |
| `POST` | `/api/v1/invitations/accept` | authenticated + IP rate limit | `{ code }` | `{ organizationId, userId, boutiqueRole, clerkOrgId, accountState }` |

**Invitable roles are exactly:** `org:boutique_supervisor`, `org:boutique_manager`,
`org:boutique_staff`. The owner role is **not** invitable.

**E-10 / F-4 — what changed in T6.**

- `POST …/invitations/bulk` exists. Before T6 the tenant panel POSTed here and got a `404`; it now
  mints exactly `count` codes for one role in a single call.
- **`count` is clamped to `[1,10]`** server-side and **`validityHours` to `[1,720]`**. The bulk
  response reports `requestedCount`, `createdCount` and `effectiveValidityHours` side by side, so a
  clamp is visible rather than silent. The clamp is the control; a per-organization limiter
  (`Invitations:CreateRateLimit`, default 10/min) is the backstop.
- **Both creation routes require an `Idempotency-Key`.** A retried bulk create would otherwise mint a
  duplicate batch of staff codes. A replay returns the stored response with
  `Idempotency-Replayed: true`; a replay with a different body is `409 idempotency-key-reuse`.
- **`validityHours` and `sendSummaryToOwner` are no longer dropped.** They were sent by the panel and
  silently ignored because the request record did not declare them. `validityHours` reaches
  `ExpiresAt`; `sendSummaryToOwner` is answered with a three-valued status —
  `NotRequested` | `Dispatched` | `NotSent` — plus a note, because "not asked for" and "asked for but
  not sent" are different facts. `Dispatched` means the notice was handed to the configured
  `IEmailService`; the repository's sender records a dispatch, not a delivery, and the note says so.
  The summary email never carries a code.

`CreateInvitationResponse` is `{ invitationId, code, link, mobileLink, boutiqueRole, recipientEmail,
expiresAt, summaryEmailRequested, summaryEmailStatus, summaryEmailNote }`.
`BulkCreateInvitationResponse` is `{ invitations: CreateInvitationResponse[], requestedCount,
createdCount, effectiveValidityHours, summaryEmailRequested, summaryEmailStatus, summaryEmailNote }`.

**Errors:** `400` not-invitable role / not acceptable / recipient mismatch / missing or malformed
`Idempotency-Key`; `404` invitation or user not found; `409` membership already exists;
`429` too many attempts (accept) or too many batches (bulk);
`503` invitation code store unavailable.
**Rate limit:** `POST /invitations/accept`, per client IP, default 10/min;
`POST /invitations/bulk`, per organization, default 10/min.
**Source:** `Endpoints/OrganizationEndpoints.cs`,
`Modules/Organizations/DTOs/InvitationDtos.cs`.

### B.5 Onboarding

Six-step wizard. **Auth:** authenticated.

| Method | Path | Body | Response |
| --- | --- | --- | --- |
| `GET` | `/api/v1/onboarding/status` | — | `OnboardingStatusResponse { hasCompletedOnboarding, currentStep, organization? }` |
| `POST` | `/api/v1/onboarding/owner` | `SaveBoutiqueDetailsRequest` | `OnboardingOrganizationDto` |
| `POST` | `/api/v1/onboarding/plan` | `{ planTier }` | `OnboardingOrganizationDto` |
| `POST` | `/api/v1/onboarding/customize` | `SaveAiCustomizationRequest` | `OnboardingOrganizationDto` |
| `POST` | `/api/v1/onboarding/complete` | — | `CompleteOnboardingResponse { organization, userRole, organizationRole, accountState, blossomAllocation, agentWarmedUp }` |

**`SaveBoutiqueDetailsRequest` validation:**

| Field | Type | Required | Max | Notes |
| --- | --- | --- | --- | --- |
| `name` | string | ✅ | 200 | |
| `address` | string | ✅ | 500 | |
| `phoneNumber` | string | ✅ | 50 | |
| `description` | string | — | 1000 | |
| `logoUrl` | string | — | 1000 | |
| `slug` | string | — | 100 | Generated from `name` if absent |

**`SaveAiCustomizationRequest` validation and tier gating:**

| Field | Max | Seed | Bloom | Orchid+ |
| --- | --- | --- | --- | --- |
| `brandVoice` | 500 | ❌ 400 | ✅ | ✅ |
| `businessRules` | 1000 | ❌ 400 | ✅ | ✅ |
| `preferredColorsFabrics` | 1000 | ❌ 400 | ✅ | ✅ |
| `customerPreferences` | 1000 | ❌ 400 | ❌ 400 | ✅ |

A disallowed field on a lower tier returns `400 { "message": "Custom AI context is
not available on the Seed plan. ..." }`
(`Modules/Organizations/Services/OnboardingService.cs:188-208`).
**Source:** `Endpoints/OnboardingEndpoints.cs:16-124`,
DTOs `Modules/Organizations/DTOs/OnboardingDtos.cs`.

### B.6 Notifications

**Auth:** authenticated. Grouped separately from org routes because notifications
are per user, not per org.

| Method | Path | Query | Response |
| --- | --- | --- | --- |
| `GET` | `/api/v1/notifications` | `page`, `pageSize`, `unreadOnly` | `NotificationPage { items, total, page, pageSize }` |
| `GET` | `/api/v1/notifications/unread-count` | — | `{ count: number }` |
| `GET` | `/api/v1/notifications/{id:guid}` | — | `UserNotificationDto` |
| `PATCH` | `/api/v1/notifications/{id:guid}/read` | — | `204` (idempotent) |
| `POST` | `/api/v1/notifications/read-all` | — | `200` (idempotent) |
| `DELETE` | `/api/v1/notifications/{id:guid}` | — | `204` |

`UserNotificationDto`: `{ id, notificationId, type, title, body, data, isRead,
readAt, deliveredAt, createdAt, organizationId, organizationName }` where `data`
is `Record<string, string|null>` and `organizationName` is null when the
dispatching organisation could not be loaded. `organizationId`/`organizationName`
are the per-row boutique label (a projection, not a filter: the inbox stays
merged).
**Errors:** `401` empty; `404 { "message": "Notification not found." }`.
**Source:** `Endpoints/NotificationEndpoints.cs:19-181`, DTO
`Modules/Notifications/DTOs/UserNotificationDto.cs`.

**Realtime:** SignalR hub at `/hubs/notifications`. Server→client event
`ReceiveNotification` carries a `NotificationDto`
(`{ type, title, body, data, notificationId, unreadCount }`), with
`NotificationType` serialised as a **string**. `notificationId` is the per-user
inbox row (`UserNotification.Id`); `unreadCount` is that recipient's count after
the row was written. Both are additive: a client that receives neither behaves as
before. The hub is client-callable in one direction only: `SubscribeAsync` (no
arguments, idempotent) re-joins the connection to `user:{id}` and every active
`org:{id}` group. SignalR does not preserve group membership across a reconnect,
so a client must invoke `SubscribeAsync` from its `onreconnected` handler.
**Source:** `Modules/Notifications/Hubs/NotificationHub.cs`,
`Modules/Notifications/Models/NotificationDto.cs`.

**Push (FCM):** the same notification is delivered to a recipient's registered
device tokens. The FCM `data` map carries the notification's own keys plus
`type` (the `NotificationType` name) and `notificationId` (the per-user inbox
row). Values are strings and null values are dropped by the sender, so every key
is optional to the consumer. Registration is via `/api/v1/users/me/devices`
(see the Device tokens section). **Source:**
`Modules/Notifications/Channels/FcmPushChannel.cs`,
`Modules/Notifications/Channels/FirebaseMessagingClient.cs`.

### B.7 Conversations (The Salon)

**Auth:** `BoutiqueConversationAccess` on every route.

| Method | Path | Query/Body | Response |
| --- | --- | --- | --- |
| `GET` | `/api/v1/orgs/{organizationId:guid}/conversations` | `page`, `pageSize` | `ConversationPage { items, total, page, pageSize }` |
| `POST` | `/api/v1/orgs/{organizationId:guid}/conversations` | `{ customerId? }` | `ConversationDto` |
| `GET` | `/api/v1/orgs/{organizationId:guid}/conversations/{conversationId:guid}` | — | `ConversationDto` |
| `GET` | `.../{conversationId:guid}/messages` | `page`, `pageSize`, `around?` | `MessagePage` |
| `POST` | `.../{conversationId:guid}/messages` | `{ text }` | `MessageDto` |
| `POST` | `.../{conversationId:guid}/select-customer` | `{ customerId, query? }` | `200` |
| `POST` | `.../messages/{messageId:guid}/sign-off` | `{ approved, contentHash }` | `200` |

`ConversationDto`: `{ id, kind, customerId, customerName, externalRef, threadId, status,
lastMessageAt, lastMessagePreview, lastMessageKind, lastMessageBlock, lastMessageAuthor,
lastMessageAgentKey, markers }`. The list row carries the client's name, a **block-aware**
preview of the newest message, the block it came from (the row's category, because agent
output is published as `kind: Note`), who spoke last in the client's vocabulary
(`Staff`/`Agent`/`Customer`), the agent persona key, and the actionable `markers` set over the
closed vocabulary `approval | choice | draft` in that priority. `kind` keeps its declared
meaning; customer context is the separate `customerId` axis, and `externalRef` discloses a
channel-created thread whose customer is not yet identified.

**A thread with no client and no channel is the organization-shared general Salon** — Aveline's own.
`customerName` is the client's name when the thread is bound to one, and `null` otherwise, so a
client that has no recorded name is distinguishable from a thread that has no client. The general
Salon is the thread a client-less `POST …/conversations` gets or creates.

**`JoinSalon` is additive, and a connection may belong to several Salon groups.** It verifies
membership and the thread's visibility, then adds the connection to `salon:{conversationId}` without
leaving any other group. A client can therefore follow two threads at once — the tenant dashboard's
Salon section and its Aveline drawer do exactly that — and must demultiplex incoming
`ReceiveMessage` / `ReceiveAgentState` payloads by `conversationId`.
`MessageDto`: `{ id, conversationId, authorKind, agentKey, authorUserId, kind,
contentBlocks, contentHash, replyToMessageId, status, createdAt }`.

**Sign-off is content-hash guarded.** The client must echo the `contentHash` of the
message it approved; a mismatch is rejected so an approval cannot be applied to
edited content (`Modules/Conversations/Services/ContentHash.cs`).
**Errors:** `400` empty message text / missing content hash / hash mismatch;
`401` empty; `404 { "message": "Conversation not found." }`.
**Source:** `Endpoints/ConversationEndpoints.cs:22-217`,
DTOs `Modules/Conversations/DTOs/*.cs`.

**Realtime:** SignalR hub `/hubs/conversations`; events `conversation.created`,
`message.created`, `message.updated`, `agent.status` (names from
`Modules/Conversations/Services/ConversationEvents.cs`).
Server→client methods: `ReceiveMessage` (a `MessageDto`), `ReceiveAgentState` (an
`AgentStateDto`), and **`ReceiveConversationChanged`** (a `ConversationDto` - the inbox tile,
so an open list updates without a re-list). The tile is broadcast to
`org:{organizationId}` for organization-shared threads and to `user:{ownerUserId}` for a
per-user general Salon, so a colleague's private thread never reaches the org group. The API
broadcasts directly from its own creation sites (the WhatsApp webhook, `POST /conversations`,
`select-customer`, and a staff message) because `conversation.created` has no publisher.

### B.8 Integrations

**Auth:** `BoutiqueMembershipManage`.

| Method | Path | Body | Response |
| --- | --- | --- | --- |
| `GET` | `/api/v1/orgs/{organizationId:guid}/integrations` | — | integration list with status |
| `GET` | `/api/v1/orgs/{organizationId:guid}/integrations/messages` | query: paging | inbound message log |
| `PUT` | `/api/v1/orgs/{organizationId:guid}/integrations/{type:alpha}` | credentials | `200` |
| `POST` | `/api/v1/orgs/{organizationId:guid}/integrations/{type:alpha}/test` | — | health result |
| `DELETE` | `/api/v1/orgs/{organizationId:guid}/integrations/{type:alpha}` | — | `204` |

Credentials are **encrypted at rest** with AES-256-GCM (`ADR-011`) and are never
returned by any read endpoint.

**`{type}` is one of `WhatsApp`, `Instagram`, `PaymentGateway`** — these are the
only three values the API accepts, and each has a fixed, required credential field
set (`Modules/Integrations/Services/IntegrationService.cs:15-24`):

| `type` | Required credential fields | Primary field |
| --- | --- | --- |
| `WhatsApp` | `accessToken`, `phoneNumberId`, `appSecret`, `webhookVerifyToken` | `accessToken` |
| `Instagram` | `clientId`, `clientSecret`, `accessToken` | `accessToken` |
| `PaymentGateway` | `secretKey` | `secretKey` |

**Errors:** `400 { "message": "Unknown integration type '<x>'." }`.
**Source:** `Endpoints/IntegrationEndpoints.cs:24-105`,
`Modules/Integrations/Models/IntegrationType.cs`.

### B.9 Billing / usage (existing)

#### `GET /api/v1/orgs/{organizationId:guid}/usage`

**Auth:** `BoutiqueAccess`. **Purpose:** the current billing period summary, used
by the dashboard's Blossom widget.
**Response `200`:** `UsageSummary`.

```json
{
  "organizationId": "0198f3c2-...",
  "periodStart": "2026-09-01T00:00:00Z",
  "periodEnd": "2026-10-01T00:00:00Z",
  "monthlyBlossomLimit": 750.0,
  "blossomUsed": 132.4,
  "blossomRemaining": 617.6,
  "status": "Active"
}
```

**Errors:** `401` empty; `403` not a member.
**Source:** `Modules/Billing/Endpoints/OrgUsageEndpoints.cs:19-26`.

> **⚠ Live defect.** `UsageTrackerService.GetOrCreateCurrentAccountAsync` always
> creates the period row with the **Seed** limit of 150, ignoring the org's actual
> tier (`Modules/Billing/Services/UsageTrackerService.cs:34,80`), and
> `UsageRepository` hardcodes the same value
> (`Modules/Billing/Repositories/UsageRepository.cs:44-53`). An org that has not
> completed onboarding, or whose period row is created lazily by a usage write,
> will report **150 Blossoms on any plan**. Fixed in **Phase 2** by
> `IEntitlementResolver`. `monthlyBlossomLimit` is also **not** updated on a plan
> upgrade, which Phase 2 adds.

#### `POST /internal/usage/record`

**Auth:** `InternalServicePolicy` (`X-Internal-Token`). **Not callable by frontends.**
**Body:** `{ organizationId, requestId, workflowId, provider, model, inputTokens,
outputTokens, cachedTokens, actualCostUsd }`.
**Response `201`:** `AiUsageRecordResponse { id, organizationId, workflowId,
provider, model, inputTokens, outputTokens, cachedTokens, actualCostUsd,
blossomUnits, createdAt }`.
**Errors:** `400 { "error": "..." }` (note: `error`, not `message`);
`401` empty.
**Idempotency:** **none today.** A retried report double-charges. Phase 4 makes
ingestion idempotent on `(organizationId, workflowId)`.

#### `GET /internal/usage/summary/{orgId:guid}` and `GET /internal/usage/records/{orgId:guid}`

Internal reads. `records` returns a **bare array** and does not clamp `page`.

### B.10 Webhooks

| Method | Path | Auth | Notes |
| --- | --- | --- | --- |
| `GET` | `/api/v1/webhooks/whatsapp/{organizationId:guid}` | Meta `hub.verify_token` | Verification challenge |
| `POST` | `/api/v1/webhooks/whatsapp/{organizationId:guid}` | `X-Hub-Signature-256` HMAC, constant-time | Inbound messages |

**Rate limit:** per `organizationId` + client IP, 120/min, hard-coded.
**Errors:** `403` (empty — HMAC failure or IP not in `Webhook:AllowedIps`);
`429` (empty).
**Source:** `Endpoints/WebhookEndpoints.cs:32-118`,
`Modules/Integrations/Services/WebhookSignatureVerifier.cs:24-45`.

### B.11 Internal: agent, customer concierge, visual intelligence

**Auth:** `InternalServicePolicy` on all. **Not callable by frontends.**

| Method | Path | Purpose |
| --- | --- | --- |
| `POST` | `/api/v1/agents/ping` | Agent reachability probe |
| `POST` | `/internal/customers/identify` | Resolve/create a customer from a message |
| `GET` | `/internal/customers/{customerId:guid}/profile` | Customer profile for the agent |
| `POST` | `/internal/customers/lookup` | Search by name/phone/email |
| `POST` | `/internal/customers/{customerId:guid}/memories` | Store a memory |
| `POST` | `/internal/customers/memories/search` | pgvector semantic search |
| `GET` | `/internal/customers/{customerId:guid}/brief` | Interaction brief |
| `POST` | `/internal/customers/{customerId:guid}/interactions` | Record an interaction |
| `GET`/`POST` | `/internal/customers/{customerId:guid}/consent` | Read/update consent |
| `POST`/`GET` | `/internal/customers/{customerId:guid}/events` | Customer events |
| `POST` | `/internal/customers/{customerId:guid}/status` | Recompute derived status |
| `POST` | `/internal/visual/inventory/search` | Inventory search |
| `GET` | `/internal/visual/inventory/{itemId:guid}` | Item read |
| `POST` | `/internal/visual/inventory` | Item create |
| `PUT` | `/internal/visual/inventory/{itemId:guid}` | Item update |
| `PATCH` | `/internal/visual/inventory/{itemId:guid}/status` | Status change |
| `GET` | `/internal/visual/inventory/low-stock` | Low-stock list |
| `POST` | `/internal/visual/analyze-image` | Vision analysis |
| `GET` | `/internal/visual/customer-matches/{itemId:guid}` | Matches for an item |
| `POST` | `/internal/visual/customer-matches/{itemId:guid}/generate` | Generate matches |
| `POST` | `/internal/visual/outfits/compose` | Compose an outfit |
| `POST` | `/internal/visual/sourcing-requests` | Create a sourcing request |
| `GET` | `/internal/visual/suppliers/{supplierId:guid}/catalog` | Supplier catalog |

> **⚠ Duplicate route registrations.**
> `Endpoints/VisualEndpoints.cs` registers the **same 13 handlers under three
> overlapping prefixes**: `/internal/visual` (`:21`), `/api/internal/visual`
> (`:109`), and `/internal/inventory` (`:128`). That is 27 mappings for 13
> handlers. The canonical prefix is **`/internal/visual`**; the others are legacy
> aliases. They are listed here rather than omitted so the duplication is visible.
> Frontends never call any of them.

### B.12 Health

| Method | Path | Auth | Response |
| --- | --- | --- | --- |
| `GET` | `/health` | anonymous | **JSON** — the same `HealthReport` as `/health/ready`, `503` when unhealthy |
| `GET` | `/health/ready` | anonymous | **JSON** `HealthReport`, `503` when unhealthy |
| `GET` | `/health/live` | anonymous | JSON `{ "status": "Healthy" }` |
| `GET` | `/metrics` | `MetricsPolicy`: `X-Internal-Token` **or** `Authorization: Bearer <Metrics:ScrapeToken>` | Prometheus text format |

> **Health note.** `/health` and `/health/ready` share one JSON writer
> (`HealthCheckResponseWriter`), so `/health` is an alias of `/health/ready`, **not**
> a `text/plain` string. It returns `application/json` with up to four checks in a
> stable order — `database`, `redis` (registered only when a Redis connection string
> is configured), `agent-service`, and `clerk-jwks` — plus `status` and
> `totalDurationMs`. The `version` block is **omitted in Production** so the public
> probe cannot fingerprint the release (finding M-3, fixed in #242); other
> environments include it. §C.9 documents the full response.

**Source:** `Modules/SystemHealth/Endpoints/HealthEndpoints.cs:22-24`,
`Modules/SystemHealth/HealthChecks/HealthCheckResponseWriter.cs`,
`Modules/SystemHealth/SystemHealthModule.cs:28-51`,
`Configurations/EventingConfiguration.cs:24,33`.

### B.13 Admin access requests (existing)

The admin sign-up flow (issue #59): a prospective Aveline administrator submits a
request after email verification, and an existing administrator reviews it.
Approval grants the `admin` role via the Clerk Backend API.

| Method | Path | Policy | Request | Response |
| --- | --- | --- | --- | --- |
| `POST` | `/api/v1/admin/requests` | authenticated | — (identity read from the JWT) | `AdminApprovalRequestSummary` |
| `GET` | `/api/v1/admin/requests` | `AdminReview` | — | `AdminApprovalRequestSummary[]` |
| `POST` | `/api/v1/admin/requests/{requestId:guid}/approve` | `AdminReview` | — | `AdminApprovalRequestSummary` |
| `POST` | `/api/v1/admin/requests/{requestId:guid}/reject` | `AdminReview` | — | `AdminApprovalRequestSummary` |

`AdminApprovalRequestSummary`: `{ id, clerkUserId, email, firstName, lastName,
status, requestedAt, reviewedAt, reviewedByClerkUserId }` where `status` is a
string-backed `AdminApprovalStatus`.

**Errors:** `401` empty; `404 { "message": "..." }` unknown request;
`409 { "message": "..." }` already decided;
`502 { "message": "..." }` Clerk Backend API failure.
**Source:** `Endpoints/AdminEndpoints.cs:18-113`.

### B.14 Admin business statistics (Business KPIs)

**Auth:** Clerk JWT only. **Not available to API keys.**
**Permission:** `analytics:business:read` (see [A.2](#a2-authorization)). Granted to
`moderator`, `admin` and `owner`; denied to `staff`, `customer_relations` and every
`org:boutique_*` role. Only `owner` and `admin` reach the console, so the `moderator` grant is
inert at the UI layer and exists so the catalogue is semantically correct.
**Source:** `Modules/Analytics/Endpoints/BusinessKpiEndpoints.cs`.
**Catalog:** S-44…S-49 in
[statistics-catalog.md](../backend/statistics-catalog.md#7b-business-kpis-growth-activity-plan-mix).

| Method | Path | Statistic | Notes |
| --- | --- | --- | --- |
| `GET` | `/api/v1/admin/statistics/business/growth` | S-44 | New users, new boutiques and access requests per bucket + `previousTotals` |
| `GET` | `/api/v1/admin/statistics/business/active-users` | S-45 | Distinct active users per bucket + the DAU/WAU/MAU reading and stickiness |
| `GET` | `/api/v1/admin/statistics/business/plan-mix` | S-46 | Per-tier organization/subscription/user counts and the free-versus-premium split |
| `GET` | `/api/v1/admin/statistics/business/subscriptions` | S-47 | Active organizations by tier over time, with starts, cancellations and churn |
| `GET` | `/api/v1/admin/statistics/business/usage` | S-48 | Messages, agent runs, API calls, Blossom units and actual AI cost per bucket |
| `GET` | `/api/v1/admin/statistics/business/organizations` | S-49 | Organizations ranked by a usage measure, with last-activity recency |

**Shared query parameters**

| Param | Type | Default | Validation |
| --- | --- | --- | --- |
| `from` | ISO 8601 datetime | `to − 30 d` | must be `< to` |
| `to` | ISO 8601 datetime | now (UTC) | `(to − from).TotalDays ≤ BusinessAnalytics:MaxWindowDays` (400) |
| `granularity` | `day` \| `week` \| `month` | `day` | anything else is `400` |
| `organizationId` | uuid | absent | `usage` only; additionally requires `admin:orgs:read` |
| `metric` | `messages` \| `agentRuns` \| `apiRequests` \| `blossomUnits` | `apiRequests` | `organizations` only; anything else is `400` |
| `limit` | int | `20` | `organizations` only; clamped to 100, and `> 1000`, `0` or negative is `400` |

**Errors:** `400 { message }` on every validation failure; `401` anonymous; `403` a role
without `analytics:business:read`; `500 { status, message, traceId }` on a database failure.
An empty source is `200` with a `null` measure and a `dataQuality` note, never a fabricated
zero baseline.

**Caching:** `Cache-Control: private, max-age=60`. The result is cached in
`IDistributedCache` at `BusinessAnalytics:CacheSeconds`, so `dataQuality` may be up to 60
seconds stale.

**Null versus zero.** A count of `0` inside the observed period is `0`. A bucket before the
earliest observation (`observedFrom`), or a measure that cannot be computed at all, is
`null`. `dataQuality.userAttributionAvailable = false` accompanies a `null` active-user
reading.

**Two operations carry a second permission.** `usage?organizationId=…` and `organizations`
both require `admin:orgs:read` in addition to `analytics:business:read`, because the first
drills into one tenant's usage and the second enumerates tenant names. The check is performed
in the handler; a caller holding only the KPI permission is refused with `403`.

**Subscription history is a snapshot, not a read of `OrganizationSubscriptions`.**
`OrganizationSubscriptionSnapshots` holds one row per organization per UTC day, written at
02:00 UTC. The tier comes from `Organizations.PlanTier`, so an organization that never changed
plan (and therefore has no subscription row) is still counted. Buckets reconstructed from the
audit ledger are flagged `isBackfilled` and `dataQuality.subscriptionHistoryBackfilled`.

**Usage without `organizationId` includes unattributed API requests** (`BR-6.1`), which is
correct for a platform total and is stated in `dataQuality.notes`. With `organizationId`, those
rows belong to no organization and are excluded.

### B.15 Authorization policy demo endpoints (fixtures)

These exist to prove the policy wiring end-to-end and are covered by
`AuthorizationPolicyTests`. They return a fixed message and are **not** part of
the product surface. They are documented here because they are reachable and a
frontend developer should not mistake them for features.

| Method | Path | Policy exercised |
| --- | --- | --- |
| `GET` | `/api/v1/policies/associate` | `Associates` |
| `GET` | `/api/v1/policies/manager` | `Managers` |
| `GET` | `/api/v1/policies/owner` | `Owners` |
| `GET` | `/api/v1/policies/approvals/approve` | `approvals:approve` |
| `GET` | `/api/v1/policies/payments/refund` | `payments:refund` |
| `GET` | `/api/v1/policies/orgs/{organizationId:guid}/catalog` | `BoutiqueAccess` (organization-scoped) |
| `GET` | `/api/v1/policies/fallback/authed-by-default` | The fallback policy (authenticated) |

Each returns `200 { "message": "<explanation>" }` on success, `401` empty when
unauthenticated, `403` empty when the policy denies.
**Source:** `Endpoints/AuthPolicyDemoEndpoints.cs:17-45`.
**Excluded from `openapi.yaml` deliberately** — they are test fixtures, not
contract.

### B.16 Admin revenue writes

> **Status: implemented** (Revenue Ledger R2, issue #343). The three verbs that put
> money into the append-only income ledger. The reads arrive with R3.

**The two entry classes.** There is no payment-provider client in this repository, so
the ledger distinguishes what a list price says *should* be billed (`Derived`) from
money an operator confirmed was received (`Verified`). No response in this family may
conflate them, and only a `Verified` entry may be described as collected.

| Method | Path | Permission | Body | Response |
| --- | --- | --- | --- | --- |
| `POST` | `/api/v1/admin/revenue/ledger/verify` | `revenue:manage` | `{ organizationId, sourceKind, sourceRef, amount, reason }` | `201` `IncomeLedgerEntryDto` |
| `POST` | `/api/v1/admin/revenue/ledger/refund` | `revenue:refund` | `{ organizationId, sourceKind, sourceRef, amount, reason }` | `201` `IncomeLedgerEntryDto` |
| `POST` | `/api/v1/admin/revenue/ledger/adjust` | `revenue:manage` | `{ organizationId, amount, reason, sourceRef?, supersedesEntryId? }` | `201` `IncomeLedgerEntryDto` |

**Idempotency: required on all three.** A missing key is `400
{ "code": "idempotency-key-required" }`; a replay returns the stored response with the
`Idempotency-Replayed: true` header and writes **nothing** a second time. The filter
fails closed: if the lease store is unreachable the request is `503
{ "code": "idempotency-unavailable" }` rather than applied twice.

**Authorization.** `revenue:manage` is held by `admin` and `owner`; `revenue:refund`
by `owner` only, because sending money back is irreversible in a way that correcting
the ledger is not. A `moderator` holds `revenue:read` and is refused `403` on all
three. Bearer-only: an API key can never reach these routes.

**Validation.**

| Field | Rule |
| --- | --- |
| `amount` | `> 0`. The ledger stores a positive amount and derives the sign from the entry kind, so a non-positive value is a `400` rather than a credit |
| `reason` | 10–500 characters, matching the Blossom ledger's constraint |
| `sourceRef` | Required on verify and optional on adjust; it is the dedup identity |
| actor | Resolved from the Clerk subject. An unresolvable actor is refused, so no money entry can be unattributable |

**Behaviour worth knowing.**

- **Verify takes over the derived charge.** A verified receipt for an existing
  `(sourceKind, sourceRef)` nulls its derived counterpart rather than sitting beside
  it: the derived row was an *expectation* of the very charge now collected. The
  original row survives with `status: "Voided"` and its `sourceRef` cleared, and the
  new row carries `supersedesEntryId` pointing back at it. Counting both would
  double-book the period.
- **A refund needs a real receipt.** Refunding a charge with no `Verified`
  counterpart is `409 { "code": "refund-not-allowed" }`. You cannot return money the
  ledger never recorded receiving, and recording it as a negative total would hide
  that.
- **An adjustment counts as verified movement**, never as derived revenue: it corrects
  the ledger rather than being something a list price asked for.
- **Nothing is ever updated or deleted.** A correction is a new row, and a cancelled
  one is marked `Voided` by the row that supersedes it.

**Errors:** `400 validation`; `400 idempotency-key-required`; `401`; `403`;
`404` unknown organization or supersede target; `409 refund-not-allowed`;
`409 duplicate-revenue-entry`; `409 income-entry-not-voidable`;
`409 idempotency-key-reuse`; `503 idempotency-unavailable`.

**Audit.** Each verb writes `revenue.ledger.{verified|refunded|adjusted}` through the
same `IAuditService` the entitlement journal uses, so both money journals read as one
history in the Audit Explorer. The rollover's derived charges are
`revenue.ledger.derived` — deliberately not one of the three administrative verbs,
because a job is not an operator.

**Source:** `Modules/Revenue/Endpoints/RevenueEndpoints.cs`,
`Modules/Revenue/DTOs/RevenueDtos.cs`.

**The system-side writers**, which write `Derived` and never `Verified`:

| Source | Site | Rule |
| --- | --- | --- |
| Blossom top-up | `POST /api/v1/orgs/{organizationId}/blossoms/top-ups` | Writes a `TopUpPurchase` row **only** when `paymentReference` is present. A free or unreferenced grant writes nothing, because a grant is not a charge |
| Subscription period charge | `BillingPeriodRolloverJob` | Writes one `SubscriptionCharge` for an `Active` subscription with `PriceLkr > 0`. A zero price writes nothing, which is why `subscriptionPricesConfigured` is `false` and MRR is `null` rather than `0` today |

Both dedupe on a natural reference — the provider reference for a top-up, the period
start in ISO-8601 for a charge — so a retry or a re-run cannot double-book.

### B.18 Blossom reconciliation report (S-56)

> **Status: implemented** (Revenue Ledger R4, issue #345). Gated `stats:system`, matching the rest
> of the `/admin/statistics/billing` family, and **not** `revenue:read`: a `moderator` reads revenue
> and does not read system statistics.

| Method | Path | Returns |
| --- | --- | --- |
| `GET` | `/api/v1/admin/statistics/billing/reconciliation` | S-56 `BlossomReconciliationResponse` — optional `organizationId` |

**Why it exists.** Drift is already a Critical alert: `SystemMetricCollector` emits
`aveline.blossom.reconciliation.drift` over the accounts it scans, and `blossom.ledger.drift` watches
it. An alarm is only actionable if the operator can find **which** account drifted, and until this
endpoint the only place to look was Grafana.

**The formula is not reimplemented.** It calls the same `BlossomService.LedgerDerivedBalance` and
`ReconciliationDrift` the collector uses, because a second derivation would let the console and the
alarm disagree about the same account — the one outcome this surface must never produce.

**Shape.** Only drifted accounts appear, ranked by the **magnitude** of the drift so the worst is
first regardless of sign; a consistent account is not noise to page through, and `accountsChecked`
distinguishes *"consistent"* from *"not looked at"*. `reconciliationChecked` is `null` here rather
than `false`: this read does not itself run a reconciliation pass, so saying `false` would claim it
checked and found nothing.

**Errors:** `401`; `403`.
**Source:** `Modules/Billing/Endpoints/BlossomReconciliationEndpoints.cs`.

### B.17 Admin revenue reads

> **Status: implemented** (Revenue Ledger R3, issue #344). All six reads are gated
> `revenue:read` and are bearer-only: an API key can never reach a revenue figure.

| Method | Path | Returns |
| --- | --- | --- |
| `GET` | `/api/v1/admin/revenue/ledger` | S-50 `IncomeLedgerPageResponse` — filters `from`, `to`, `page`, `pageSize` |
| `GET` | `/api/v1/admin/revenue/accounts` | S-51 `RevenueAccountsResponse` — per-organization derived/verified/net |
| `GET` | `/api/v1/admin/statistics/revenue/overview` | S-52 `RevenueOverviewResponse` — MRR, ARR, ARPU |
| `GET` | `/api/v1/admin/statistics/revenue/timeseries` | S-53 `RevenueTimeseriesResponse` |
| `GET` | `/api/v1/admin/statistics/revenue/collections` | S-54 `RevenueCollectionsResponse` |
| `GET` | `/api/v1/admin/statistics/revenue/blossoms` | S-55 `RevenueBlossomSalesResponse` |

**Every response carries `dataQuality`** (`IncomeDataQuality`, the fifth vocabulary), and
every one sets `Cache-Control: private, max-age=…` — `max-age=0` on the ledger register,
which is deliberately uncached, and the console TTL elsewhere. `private` matters: a shared
proxy must never hold one organization's revenue figures.

**Windows.** `from`/`to` with `Revenue:MaxWindowDays` (default **400**, matching the
retention the S-catalog claims). A window over the cap is **`400` naming the effective
limit**, never silently shortened — the response must describe the period the caller asked
about. `granularity` is `day | week | month`. Buckets are dense and aligned to calendar
boundaries, and `isPartial` marks a bucket the window clips at either edge.

**Null is not zero, and this is the contract the family exists to keep.** A measure that
could not be computed is `null`, never `0`:

- **MRR, ARR and ARPU are `null` while every `PriceLkr` is `0`**, with
  `subscriptionPricesConfigured: false` and a note. That is the production case today,
  because `SubscriptionService.UpsertSubscriptionAsync` never assigns the price. A `0` MRR
  would read as "we earn nothing"; the truth is "no price is configured".
- **`collectionRate` is `null`, not `0`, when nothing was billed.** `0` would claim that
  something was billed and none of it collected, which is a different and false statement.
- **`arpu` is `null` rather than a divide-by-zero** when nothing is priced.
- A measured `0` inside the observed period stays `0`.

**Derived versus verified is never merged.** The timeseries returns three separate series,
and the register's reconciliation exposes `derivedTotal`, `verifiedTotal` and the
`unverifiedGap` between them. The gap is a **magnitude**: a receipt with no matching charge
is as much a finding as a charge with no receipt. The signed view is
`collectionRate.outstanding`, which is a balance, so a negative value correctly means more
came in than was billed. `isBalanced` means at least as much was collected as billed.

**MRR is list-price scheduled revenue**, not recognised or collected revenue. The response
states `revenueProviderSettlementAvailable: false`, and the console must not label it
"collected". An `Annual` subscription is divided by twelve before it enters MRR, so a single
annual row does not appear as twelve times its monthly value.

**The ledger register is uncached and returns the window's totals**, not the page's: the
reconciliation describes the period the caller asked about rather than the slice on screen.
Totals and `dataQuality` are computed over the whole window.

**Errors:** `400` invalid window or granularity; `401`; `403`.
**Source:** `Modules/Revenue/Endpoints/RevenueEndpoints.cs`,
`Modules/Revenue/DTOs/RevenueReadDtos.cs`.

---

### B.19 Commerce — orders, business rules, payments, deliveries, approvals

These are the tenant-scoped commerce controllers. They were previously **not documented at all**,
which is part of how the two defects below survived: a route that appears in no catalogue is a route
nobody reviews.

**The route token is `{organizationId:guid}`.** This matters beyond style: the organization-scope
authorization handler reads `RouteValues["organizationId"]` and nothing else
(`Authorization/OrganizationScopeAuthorizationHandler.cs:38`), so a route declared with any other
token cannot bind an org-scoped policy.

The tenant-dashboard slice's T1 fixed two blocking defects here:

- **F-1** — `OrdersController` and `BusinessRulesController` carried **no `[Authorize]` at all** and
  used `{orgId:guid}`. The fallback policy is "authenticated", so **any authenticated caller,
  including another boutique's staff, could read and write any organisation's orders and business
  rules.** Both controllers now carry an org-scoped policy and the `organizationId` token.
- **F-5** — the approval actor was resolved by `Guid.TryParse` on the Clerk `sub`. A Clerk subject is
  a string such as `user_2abc…`, so the parse always failed and every decision recorded a **null
  actor**. It now resolves through `IUserRepository.GetByClerkIdAsync`, the way every other actor in
  the codebase does.

#### Orders

| Method | Path | Policy | Permission |
| --- | --- | --- | --- |
| `GET` | `/api/v1/orgs/{organizationId:guid}/orders` | `BoutiqueMember` | — (any active member) |
| `GET` | `/api/v1/orgs/{organizationId:guid}/orders/{id:guid}` | `BoutiqueMember` | — |
| `POST` | `/api/v1/orgs/{organizationId:guid}/orders` | `BoutiqueMember` | — (the counter creates orders) |
| `PUT` | `/api/v1/orgs/{organizationId:guid}/orders/{id:guid}` | `BoutiqueOrderManage` | `orders:manage` |
| `PATCH` | `/api/v1/orgs/{organizationId:guid}/orders/{id:guid}/status` | `BoutiqueOrderManage` | `orders:manage` |
| `POST` | `/api/v1/orgs/{organizationId:guid}/orders/{id:guid}/cancel` | `BoutiqueOrderManage` | `orders:manage` |
| `POST` | `/api/v1/orgs/{organizationId:guid}/orders/{id:guid}/recalculate` | `BoutiqueOrderManage` | `orders:manage` |

Reads and create stay at member level because a counter associate must see and create the order they
are serving. The lifecycle verbs take `orders:manage` because Q8 says staff may **approve** a customer
order but may not change its lifecycle — and `BoutiqueAccess` (= `catalog:view`, held by every role)
would have granted staff the ability to cancel orders.

#### Business rules

| Method | Path | Policy | Permission |
| --- | --- | --- | --- |
| `GET` | `/api/v1/orgs/{organizationId:guid}/business-rules` | `BoutiqueOrderManage` | `orders:manage` |
| `GET` | `/api/v1/orgs/{organizationId:guid}/business-rules/{id:guid}` | `BoutiqueOrderManage` | `orders:manage` |
| `POST` | `/api/v1/orgs/{organizationId:guid}/business-rules` | `BoutiqueOrderManage` | `orders:manage` |
| `PUT` | `/api/v1/orgs/{organizationId:guid}/business-rules/{id:guid}` | `BoutiqueOrderManage` | `orders:manage` |
| `DELETE` | `/api/v1/orgs/{organizationId:guid}/business-rules/{id:guid}` | `BoutiqueOrderManage` | `orders:manage` |
| `POST` | `/api/v1/orgs/{organizationId:guid}/business-rules/evaluate` | `BoutiqueOrderManage` | `orders:manage` |

Every route here is `orders:manage`, including the reads and the evaluator, because a business rule
*is* order policy: it decides when an order needs approval (`OrderService.cs:112`) and what caps a
discount.

#### Payments, deliveries, approvals

| Method | Path | Policy | Notes |
| --- | --- | --- | --- |
| `GET`/`POST`/… | `/api/v1/orgs/{organizationId:guid}/payments/**` | `BoutiqueAccess`, refund additionally `BoutiquePaymentRefund` | generate / confirm / refund |
| `GET`/`POST`/… | `/api/v1/orgs/{organizationId:guid}/deliveries/**` | `BoutiqueAccess` | delivery plans |
| `GET` | `/api/v1/orgs/{organizationId:guid}/approvals` | `BoutiqueAccess` | the pending queue |
| `POST` | `/api/v1/orgs/{organizationId:guid}/approvals/{id:guid}/decision` | `BoutiqueApprovalDecision`, plus `BoutiqueOrderManage` for a `reject`/`revise` body | `approvals:approve`; `orders:manage` for the cancel/rewrite verbs |
| `POST` | `/api/v1/orgs/{organizationId:guid}/approvals/{id:guid}/approve` | `BoutiqueApprovalDecision` | `approvals:approve` — **staff hold it** |
| `POST` | `/api/v1/orgs/{organizationId:guid}/approvals/{id:guid}/reject` | `BoutiqueOrderManage` | **cancels the order**; requires `orders:manage` (T6) |
| `POST` | `/api/v1/orgs/{organizationId:guid}/approvals/{id:guid}/revise` | `BoutiqueOrderManage` | **rewrites discount/total/margin**; requires `orders:manage` (T6) |

**The four decision verbs are split by what they do, and the split is by verb — not only by route
(T6 / Q14).** `reject` sets `order.Status = "cancelled"` and `revise` rewrites the money fields, so
granting `org:boutique_staff` `approvals:approve` (Q8) would otherwise hand them exactly the
abilities Q8 denied. `/approve` stays on `approvals:approve`; `/reject` and `/revise` move to
`BoutiqueOrderManage` (`orders:manage`, manager/supervisor/owner). **`/decision` carries its verb in
its body**, so the route policy alone would leave a door open: a `reject` or `revise` decision
submitted through `/decision` is refused with `403 order-manage-required` unless the caller also
holds `orders:manage`. The classification is one named function,
`Modules/Commerce/Models/ApprovalDecisions.cs`, so a future verb cannot be added without being
classified.

**Errors:** `400` invalid body or transition; `401`; `403` (policy or cross-tenant); `404` (missing,
or not in this organisation — deliberately indistinguishable); `409` where a state conflict applies.
**Source:** `Modules/Commerce/Controllers/{Orders,BusinessRules,Payments,Deliveries,Approvals}Controller.cs`.

---

### B.20 Tenant customer surface

The client book Home and the counter use. Reads take `BoutiqueCustomerAccess` (`customers:view`,
held by every role); the two writes take `BoutiqueCustomerManage` (`customers:manage`, held by
manager/supervisor/owner — T0a added it because `customers:view` is held by everyone, so there was
nothing correct to gate a write on).

| Method | Path | Policy | Notes |
| --- | --- | --- | --- |
| `GET` | `/api/v1/orgs/{organizationId:guid}/customers` | `customers:view` | query: `search?`, `level?`, `page`, `pageSize` (clamped `[1,500]`, default 200) |
| `GET` | `/api/v1/orgs/{organizationId:guid}/customers/highlights` | `customers:view` | Home's client row; `activity` is generated from a real interaction, and there is deliberately **no** `hasNewActivity` flag — no read marker exists in the schema |
| `GET` | `/api/v1/orgs/{organizationId:guid}/customers/{customerId:guid}` | `customers:view` | **E-6.** The tenant-safe detail |
| `GET` | `/api/v1/orgs/{organizationId:guid}/customers/{customerId:guid}/interactions` | `customers:view` | **E-9.** Paged history, newest first |
| `POST` | `/api/v1/orgs/{organizationId:guid}/customers` | `customers:view` | Walk-in creation; requires `Idempotency-Key`. Also creates the client's organization-shared Salon, seeded with Aveline's greeting (see below) |
| `POST` | `/api/v1/orgs/{organizationId:guid}/customers/{customerId:guid}/interactions` | `customers:view` | Records an interaction; requires `Idempotency-Key` |
| `PATCH` | `/api/v1/orgs/{organizationId:guid}/customers/{customerId:guid}` | **`customers:manage`** | **E-7.** Partial update of the writable subset |
| `DELETE` | `/api/v1/orgs/{organizationId:guid}/customers/{customerId:guid}` | **`customers:manage`** | **E-8.** Soft delete |

#### The client's Salon is created with the client

Creating a client also creates its **organization-shared Salon** (`Conversations` row with
`CustomerId` set, `OwnerUserId` `NULL` per ADR-021) and seeds Aveline's greeting into it. This is a
side effect of `POST …/customers`, not a separate route.

It is load-bearing rather than tidy: the Salon list (`GET …/conversations`) lists **conversations**,
not clients, so a client without a Salon is invisible to the concierge surface even though it is in
the client book. The demo tenant was in exactly that state — two clients, no customer-bound
conversations.

Clients created before this behaviour are repaired once at application start by
`CustomerSalonBackfillJob`, which inserts the missing Salon **and** its greeting through the same
service path that the create endpoint uses. It is idempotent, so a restart or a second instance is a
no-op, and it is capped by `Conversations:CustomerSalonBackfillMaxCustomers` (default 500) per boot.
It is deliberately not a SQL migration: the greeting is domain text, and duplicating it in SQL would
give repaired Salons a different opening message from new ones.

#### The detail shape (E-6)

`TenantCustomerDetailDto` carries `customerId`, `fullName`, `nickname`, `phoneNumber`, `email`,
`level`, `status`, `totalSpent`, `visitCount`, `lastVisitAtUtc`, `loyaltyTierIsDerived`,
`createdAtUtc`, `updatedAtUtc`, `interactionCount` and `tags`. It is deliberately **not** the
internal customer shape: no memory, no extracted AI context and no internal-token fields reach a
staff device.

`loyaltyTierIsDerived` is a constant `1`, not a boolean: it exists so the client cannot render an
editable tier control. `status` is computed by `CustomerLoyaltyService.RecommendStatus` from spend,
visits and recency.

**E-6 repairs a dangling contract.** `POST …/interactions` already emitted
`Location: …/customers/{customerId}` and that GET did not exist. This slice creates it.

#### The update shape (E-7)

`{ fullName?, nickname?, phoneNumber?, email?, level? }` — and nothing else. **`status` is absent on
purpose**: it is derived, so making it writable would let the UI contradict the loyalty rule, and a
`status` sent in the body is ignored.

`phoneNumber` is normalised through the existing `PhoneNormalizer.ToE164`. A number that is not a
recognised Sri Lankan form is a `400`; a number another client in the same organisation already
holds is a **`409 { "code": "customer-phone-conflict" }`**.

The same `409` applies on **create**: the unique `(OrganizationId, PhoneNumber)` index used to
surface as an unmapped `DbUpdateException` and therefore a `500`. A pre-check inside the service
makes the collision a typed outcome on every provider, so the behaviour a client sees does not
depend on the storage engine.

#### The delete semantics (E-8)

**Soft** — `DeletedAt` is set, matching the existing global query filter, so a deleted client is
invisible to every read.

**Idempotent** — deleting an already-deleted client is `204`, not `404`. The deletion is the end
state, and the soft-delete filter means the caller could not have distinguished the two anyway. The
service reads through `IgnoreQueryFilters()` with the tenant scope applied explicitly, so
"already deleted in *this* organisation" is reachable while another organisation's row is not.

**Refused with `409 { "code": "customer-has-open-orders", "openOrders": n }`** when the client has
orders in a **non-terminal** status. Terminal means `completed`, `cancelled` or `rejected` — the
three with no outgoing edge in `OrderService.ValidTransitions`. This is a **business guard, not
referential integrity**: `Order.CustomerId` is a bare `Guid` with no foreign key or navigation, and
`Order.CustomerName` is denormalised, so nothing is orphaned and a deleted client's orders stay
readable. The guard exists so a sale-to-delete flow cannot remove a client mid-transaction.

#### Errors

`400` invalid body, a non-Sri-Lankan phone, or a visit in the future / older than 30 days;
`401`; `403` (policy or cross-tenant); `404` missing, deleted, **or in another organisation** —
deliberately indistinguishable, matching the by-slug precedent; `409` as above.
**Source:** `Endpoints/CustomerTenantEndpoints.cs`,
`Modules/CustomerConcierge/Services/CustomerTenantService.cs`,
`Modules/CustomerConcierge/DTOs/CustomerTenantDtos.cs`.

---

### B.21 Boutique income — the register and the per-kind breakdown

**A different economy from §B.16 and §B.17.** Those are the **admin** revenue surface, which records
what Aveline billed a boutique. This is what a **boutique** took from **its clients**, from the
`BoutiqueSaleEntries` table. The two are never summed and no route reads both. The wire calls it
**Income** because that is the owner-facing word; the entity is a `BoutiqueSaleEntry`, named so that
nobody merges it with Aveline's revenue journal.

| Method | Path | Policy | Permission |
| --- | --- | --- | --- |
| `GET` | `/api/v1/orgs/{organizationId:guid}/income/ledger` | `BoutiqueReportsView` | `reports:view` |
| `GET` | `/api/v1/orgs/{organizationId:guid}/income/accounts` | `BoutiqueReportsView` | `reports:view` |

Both routes are **org-scoped**: the policy resolves the caller's membership from the database against
the route's `organizationId`, so a still-valid JWT carrying another organisation's claim cannot cross
tenants. A staff member holds no `reports:view` and receives `403`; their reduced takings card is a
separate route.

#### The two-basis rule, and why it cannot be flattened

Every entry carries a `chargeBasis`:

- **`Derived`** — "this is what the order says was sold". **Billed value**, with no evidence that
  money moved.
- **`Verified`** — somebody asserted this money was taken: a counter sale, a confirmed payment, a
  refund.

`amount` is **always positive** and the sign is applied by the reader from `kind`, so a column sum can
never net a refund against a sale by accident. The figures the response reports are, and must remain,
separate:

| Field | Meaning |
| --- | --- |
| `verifiedTotal` | Money taken, excluding refunds |
| `derivedTotal` | Billed value awaiting confirmation |
| `unverifiedGap` | **Equal to `derivedTotal`.** The headline honesty number, not an error |
| `netVerified` | `verifiedTotal − refundTotal` |

**No screen may present `derivedTotal + verifiedTotal` as one unlabelled figure.** `isReconciled` is
`true` only when `derivedTotal` is zero.

Each `kind` (`Sale`, `PaymentReceived`, `Refund`, `Adjustment`) is also reported in **its own total**,
so a refund is never netted into a sale figure without a label.

#### The register

`GET …/income/ledger` — query: `from`, `to`, `kind`, `basis`, `q` (searches `reason` and `sourceRef`),
`page`, `pageSize` (clamped `[1,200]`, default 50).

The **window totals and the reconciliation block describe the whole window, not the page**, so a
caller paging through a week still sees the week's figures. An unknown `kind` or `basis` is a **`400`
naming the known values**, never a silently ignored filter.

**The window cap is echoed, never silent.** A window longer than `TenantDashboard:MaxWindowDays`
(default 400) is capped, `windowCapped` is `true`, the effective window is returned, and a
data-quality note says so — a chart labelled "1 year" over 400 days of data is a lie about the shape
of the series.

**The register is uncached**, because it carries the reconciliation banner.

#### The per-kind breakdown

`GET …/income/accounts` — query: `from`, `to`. Returns one total and count per `kind`, plus a
`byPaymentMethod` split for cash entries. That split is read from the **payment rows**, joined on
`paymentId`, rather than inferred: a ledger entry carries no payment method of its own, and inventing
one would be exactly the fabrication this surface exists to avoid. An entry with no payment simply has
no method to report.

#### The data-quality block

`BoutiqueIncomeDataQualityDto` is this surface's own vocabulary — the sixth in the API, deliberately
not a reuse of the revenue family's. `paymentRowsPresent` is `false` when the shop has no `Payments`
rows at all, which is the single most likely reason a real shop's cash figures read zero; the notes
say so rather than leaving the owner to conclude their shop took nothing. `incomeLedgerBackfilled`
reports whether the register contains only rows the reconciliation job repaired. `currency` is read
from `Organization.Currency` — a real column — rather than hardcoded.

#### The reconciliation job, and the gap it cannot close

`IncomeLedgerReconciliationJob` runs hourly and repairs a ledger write that failed **after** its
business event succeeded — a payment confirmation returns `200` because the money moved, so a failed
insert would leave the register silently understating the shop's takings. It is bounded to a 7-day
window and **idempotent by construction**: each repair reuses the product writer's own `SourceRef`,
so the ledger's filtered unique index turns a second attempt into a no-op. It reports the repair
count at **warning** level, because a job that quietly repairs rows every run is telling an operator
that a writer upstream is broken.

**It cannot repair counter sales, and does not pretend to.** The plan specified it as repairing
"confirmed payments or interactions carrying a purchase total"; implementation established that the
second half is impossible, because `CustomerInteraction` has **no amount column** — the purchase
total is read from the request, folded into `Customer.TotalSpent`, and discarded. The job repairs
payments, counts the interactions it could not repair, and logs them. A fabricated amount would be
worse than a known gap. Persisting the amount on the interaction is the honest fix and is its own
slice.

#### Errors

`400` unknown `kind`/`basis`, or a window whose start is not before its end; `401`; `403` (no
`reports:view`, or cross-tenant); the route is not reachable with the internal-token scheme.
**Source:** `Modules/Commerce/Endpoints/IncomeEndpoints.cs`,
`Modules/Commerce/Services/BoutiqueIncomeReadService.cs`,
`Modules/Commerce/DTOs/BoutiqueIncomeDtos.cs`.

---

### B.22 Tenant dashboard KPIs and the reduced takings read

**Two policies, and the split is the design.**

| Method | Path | Policy | Permission |
| --- | --- | --- | --- |
| `GET` | `/api/v1/orgs/{organizationId:guid}/dashboard/takings` | `BoutiqueMember` | — (any active member) |
| `GET` | `/api/v1/orgs/{organizationId:guid}/dashboard/summary` | `BoutiqueReportsView` | `reports:view` |
| `GET` | `/api/v1/orgs/{organizationId:guid}/dashboard/revenue-series` | `BoutiqueReportsView` | `reports:view` |
| `GET` | `/api/v1/orgs/{organizationId:guid}/dashboard/top-items` | `BoutiqueReportsView` | `reports:view` |

A section is visible when its **cheapest panel is readable**; every richer panel inside it is
**hidden, never 403'd**. `…/takings` is the cheapest panel, so every boutique role can call it; the
full strip carries margin, per-catalogue splits and the series, so it stays behind `reports:view`.

#### `GET …/dashboard/takings` — the reduced read (E-13)

Returns **exactly two money figures**: `collected` (verified money minus refunds) and
`billedUnconfirmed` (derived billed value), plus the window, currency, `paymentRowsPresent`,
`ledgerBackfilled` and the `dataQuality` block. **There is no margin, no per-client or per-operator
split, no register row and no series.** The two-basis rule survives the reduction, so there is never
one unlabelled earnings number even here.

The reduction is enforced by the **shape of the response**, not by a UI convention: a test enumerates
the serialised field names and fails if anything outside the allowed set appears, so a later addition
that leaked margin would fail a test rather than quietly ship.

#### `GET …/dashboard/summary` — the KPI strip (S-57)

`?window=7d|30d|90d|mtd|ytd`, default `30d`. Returns the `sales`, `cash`, `customers`, `catalog`,
`team`, `usage` and `operations` groups plus `dataQuality`.

**Every absent number is `null`, never `0`.** `0` is a measurement — nobody ordered — while `null` is
the absence of one. An average order value over no orders does not exist, so it is `null`; a margin
percentage against zero revenue does not exist, so it is `null`.

Two provenance facts the response states rather than implies:

- **`sales.marginCostsComplete`** is `false` when any contributing order's line items carry a zero
  `WholesaleCost`. That cost is **caller-supplied** rather than read from `InventoryItem.Cost`, so a
  margin built on it is only as trustworthy as what somebody typed, and the flag is how a reader
  finds out.
- **`cash` reports `collected`, `outstanding` and `refunded` separately** so an expiring payment
  request is never presented as a receivable and a refund is never netted invisibly into what was
  taken.

**The order-status classification is a named constant with a test that walks the transition map.**
`payment_expired` is **counted** because it is not terminal — it can return to `payment_requested` —
and `confirmed` and `revised` are counted because the approval path writes them and the model's own
comment forgets they exist. Excluded are exactly `cancelled` and `rejected`.

**Caching.** A 60-second `IDistributedCache` entry per `(organizationId, window)`. **A cache outage
degrades rather than fails**: the figures are computed directly and the `dataQuality` notes say the
cache was unreachable, so an operator reading the dashboard during a Redis outage can tell that the
numbers are current and only the caching is degraded.

#### `GET …/dashboard/revenue-series` (S-58)

`?from&to&bucket=day|week|month`. Dense buckets, each with `grossOrderValue`, `collected` and
`refunded`. **A bucket with no orders carries `null`, not `0`**, so the client sets
`connectNulls={false}` and renders a gap as a gap rather than drawing a line through a measurement the
server never produced. The series has its **own** cap (`TenantDashboard:MaxWindowDays`, default 92 for
this route) and echoes `windowCapped` rather than clamping silently.

#### `GET …/dashboard/top-items` (S-59)

`?window&limit` (1..20, default 5). Grouped on the denormalised `ItemName`, because
`OrderItem.ItemId` has no enforced link to the catalogue: a **renamed piece appears under both names**
rather than being silently merged into one.

#### Errors

`400` unknown `window`/`bucket`, a limit outside its clamp, or an inverted window; `401`; `403` (no
`reports:view` on the three strip routes, or cross-tenant on any of them). `…/takings` admits every
active member and refuses a cross-tenant caller the same way.
**Source:** `Modules/Commerce/Endpoints/DashboardEndpoints.cs`,
`Modules/Commerce/Services/TenantDashboardService.cs`,
`Modules/Commerce/DTOs/TenantDashboardDtos.cs`.

---

### B.23 Tenant billing reads — period history and the top-up catalogue (E-11, E-12)

Two reads the tenant Billing and Usage sections were missing. Both are org-scoped, both filter
`OrganizationId` explicitly, and neither writes anything.

| Method | Path | Policy | Permission |
| --- | --- | --- | --- |
| `GET` | `/api/v1/orgs/{organizationId:guid}/billing/periods` | `BillingView` | `billing:view` |
| `GET` | `/api/v1/orgs/{organizationId:guid}/blossoms/top-up-packs` | `BillingManage` | `billing:manage` |

The two permissions are deliberately different. The period history is a read of the same billing
surface the statement belongs to, so it takes `billing:view`. The pack catalogue feeds the purchase,
so it takes **the purchase's own permission**: whoever may not buy may not see what is for sale
either (TD9).

#### `GET …/billing/periods` (S-62)

`?take` (1..24, default 12); an out-of-range value is `400` with the effective range rather than a
silent clamp. Returns the most recent billing periods, newest first: the period's Blossom account
(`monthlyBlossomLimit`, `blossomGranted`, `blossomAdjusted`, `blossomUsed`, `blossomRemaining`), its
plan (`planTier`, `hasSubscriptionRow`), the top-ups that landed in it (`topUpBlossoms`,
`topUpCount`) and its list price.

Two provenance rules the response states rather than implies:

- **`planListPriceLkr` is `null` whenever the stored `PriceLkr` is zero**, never `0`, with
  `subscriptionPricesConfigured` saying whether a price was configured at all.
  `OrganizationSubscription.PriceLkr` **is never assigned anywhere** in the product — creation,
  upsert and the snapshot job all leave the column default — so "no row ⇒ null, else the column"
  would print `LKR 0` as the plan price of every subscription (C-4/TD8). The same rule the admin
  revenue read layer uses is applied here; a zero is "not configured", not "free".
- **`planTier` and `hasSubscriptionRow` come from the day's `OrganizationSubscriptionSnapshot`**
  (or the live subscription row for the current period), not from `Organization.PlanTier`. Every
  organization has a tier; only some have ever had a billing row, and reporting the former as the
  latter would invent a subscription.

This is a **statement of account, not an invoice.** No payment provider is connected and no invoice
entity, numbering rule or currency column exists, so no row here is a demand for payment.

#### `GET …/blossoms/top-up-packs` (S-63)

Returns the active `BlossomPriceEntry` rows with `SkuKind = TopUpPack`, ordered by size, as
`{ skuCode, blossomQuantity, priceLkr, currency }`. The lookup is **identical** to the one
`POST …/blossoms/top-ups` performs (`BlossomSkuKind.TopUpPack, planTier: null, organizationId: null`,
filtered to `BlossomRuleStatus.Active`), so a pack the catalogue offers cannot be rejected at
purchase and a pack the purchase accepts cannot be missing (B-4). A draft price-book row is not for
sale and is not listed. `currency` is `LKR` because the price book is denominated in LKR; no currency
column exists to read.

#### Errors

`400` out-of-range `take`; `401`; `403` (no `billing:view` / `billing:manage`, or a cross-tenant
caller).
**Source:** `Modules/Billing/Endpoints/OrgUsageEndpoints.cs`,
`Modules/Billing/Endpoints/BlossomEndpoints.cs`,
`Modules/Billing/Services/TenantBillingReadService.cs`,
`Modules/Billing/DTOs/TenantBillingDtos.cs`.

---

## Part C — Planned endpoints

`Phase` in the right column is from
[implementation-plan.md §10.3](../backend/implementation-plan.md). Every endpoint
below is registered under the `/api/v1` group unless the path says otherwise.

### C.1 Blossom pricing — admin (Phase 1)

> **Status: implemented, with two caveats.** The read/write rule and price-book
> endpoints are live on `feature/admin-backend-api` (issue #186). The two caveats:
>
> 1. **Rules do not affect billing under the shipped configuration.**
>    `appsettings.json` ships `"Pricing:UseLegacyFormula": true`, so ingest prices
>    with the legacy ceil-to-1dp formula and ignores the rule engine entirely (and
>    never writes the FR-1.4 pricing snapshot). Every endpoint below still returns
>    `200/201` and persists a rule; a rule only starts pricing usage once the flag is
>    flipped to `false`. This is the deliberate rollout gate recorded in
>    [implementation-plan.md §10.6](../backend/implementation-plan.md#106-feature-flags)
>    ("`true` on first deploy, flipped to `false` after the rule cache is verified
>    warm"), not a rejected request. **Do not present a rule or price-book edit as a
>    billing change until the flag is off.**
> 2. **`POST /rules/{ruleId}/recompute` returns `501 Not Implemented`**, because the
>    compensating-ledger recompute job is not scheduled yet. See the endpoint below
>    for the status response and the intended contract.

Permissions: the read routes are enforced by the team-only `PricingAdminRead` policy,
which requires the `admin` or `owner` role **and** `pricing:view` (**A9 B2**); writes
need `pricing:manage`, and a past effective date additionally needs `pricing:backdate`.
`pricing:view` therefore does gate the read routes again. **Never available to boutique
roles.**

---

#### `GET /api/v1/admin/pricing/rules`

**Purpose:** List conversion rules with their effective windows.
**Params:** `page`, `pageSize`, `scopeKind?` (`Global|Provider|ProviderModel`),
`provider?`, `model?`, `status?` (`Draft|Active|Superseded|Cancelled`),
`activeAt?` (ISO timestamp — returns the rule that would price usage at that instant).
**Response `200`:** `PricingRulePage`.

```json
{
  "items": [
    {
      "id": "0198f3c2-1a2b-7c3d-8e4f-5a6b7c8d9e0f",
      "scopeKind": "ProviderModel",
      "provider": "openai",
      "model": "gpt-4o",
      "unitsPerBlossom": 1000,
      "minimumChargeBlossoms": 0.1,
      "roundingMode": "Ceiling",
      "roundingDecimals": 1,
      "effectiveFrom": "2026-09-01T00:00:00Z",
      "effectiveTo": null,
      "status": "Active",
      "version": 2,
      "changeReason": "Align gpt-4o normalisation with observed cost per workflow.",
      "createdByUserId": "0198f3c2-...",
      "approvedByUserId": "0198f3c2-...",
      "createdAt": "2026-08-28T09:15:00Z",
      "updatedAt": "2026-09-01T00:00:00Z"
    }
  ],
  "total": 1,
  "page": 1,
  "pageSize": 50
}
```

**Errors:** `401`, `403`.
**Related statistics:** [S-5 `blossomCostByPlan`](../backend/statistics-catalog.md).

---

#### `POST /api/v1/admin/pricing/rules`

**Purpose:** Create a conversion rule. Created as `Draft`.
**Idempotency:** optional.

**Body — `CreatePricingRuleRequest`:**

| Field | Type | Required | Validation |
| --- | --- | --- | --- |
| `scopeKind` | enum | ✅ | `Global` \| `Provider` \| `ProviderModel` |
| `provider` | string | conditional | Required for `Provider`/`ProviderModel`; max 64 |
| `model` | string | conditional | Required for `ProviderModel`; max 128 |
| `unitsPerBlossom` | integer | ✅ | `> 0`, `<= 10_000_000` |
| `minimumChargeBlossoms` | number | — | `>= 0`, max 4 dp, default `0.1` |
| `roundingMode` | enum | — | `Ceiling` \| `HalfUp` \| `Down` \| `Up`; default `Ceiling` |
| `roundingDecimals` | integer | — | `0..6`, default `1`; `HalfUp` requires `>= 1` |
| `effectiveFrom` | string | ✅ | ISO 8601 UTC. Past dates require `pricing:backdate` |
| `changeReason` | string | ✅ | 10–500 chars |

**Response `201`:** the created `PricingRule`.
**Errors:**

| Status | Body `code` | When |
| --- | --- | --- |
| `400` | `validation` | Field validation |
| `400` | `scope-inconsistent` | e.g. `scopeKind=Global` with a `provider` |
| `403` | — | Missing `pricing:manage`, or a past `effectiveFrom` without `pricing:backdate` |
| `409` | `rule-overlap` | The effective window overlaps an existing non-cancelled rule for the same scope |

**Example:**

```bash
curl -X POST https://api.aveline.app/api/v1/admin/pricing/rules \
  -H "Authorization: Bearer $ADMIN_JWT" \
  -H "Content-Type: application/json" \
  -H "X-Request-Id: 6f1c2a90-4b7e-4d21-9c3a-8e5f1b2d4c60" \
  -d '{
    "scopeKind": "ProviderModel",
    "provider": "openai",
    "model": "gpt-4o",
    "unitsPerBlossom": 1200,
    "minimumChargeBlossoms": 0.1,
    "roundingMode": "Ceiling",
    "roundingDecimals": 1,
    "effectiveFrom": "2026-10-01T00:00:00Z",
    "changeReason": "Raise units per Blossom after September cost review."
  }'
```

---

#### `GET /api/v1/admin/pricing/rules/{ruleId:guid}`

Read one rule. **Errors:** `404`.

#### `PATCH /api/v1/admin/pricing/rules/{ruleId:guid}`

**Purpose:** Edit a rule. **Only while `status = Draft`.**
**Body:** any subset of the create fields except `scopeKind`.
**Errors:** `400 validation`; `403` if `effectiveFrom` is in the past and the caller
lacks `pricing:backdate`; `404`; `409 { "code": "rule-immutable" }` if the rule is not
a `Draft` (supersede it with a new rule instead).

#### `POST /api/v1/admin/pricing/rules/{ruleId:guid}/activate`

**Purpose:** Make the rule effective. Trims the predecessor's `effectiveTo` to this
rule's `effectiveFrom` and marks the predecessor `Superseded`, atomically.
**Body:** `{ "effectiveFrom": "<iso8601>" }` (optional override).
**Response `200`:** the activated `PricingRule`.
**Errors:** `400 { "code": "rule-not-draft" }` if not a `Draft`; `403` (missing
`pricing:manage`, or a past `effectiveFrom` without `pricing:backdate`); `404 { message }`;
`409 { "code": "rule-overlap" }`.
**Side effects:** publishes `pricing.rule.activated`; invalidates the pricing cache
in-process immediately, and on other instances within the 5-second TTL safety net
(no cross-instance event subscriber yet, see [../backend/README.md](../backend/README.md)).

#### `POST /api/v1/admin/pricing/rules/{ruleId:guid}/cancel`

**Body:** `{ "reason": string }` (10–500). **Response `200`.**
**Errors:** `400 { "code": "rule-priced" }` if the rule is already `Active` and has
priced usage → use a superseding rule instead; `400 { "code": "validation" }` for a bad
reason; `409`.

#### `POST /api/v1/admin/pricing/rules/{ruleId:guid}/recompute`

> **Status: not implemented — returns `501`.** The handler exists but answers
> `501 Not Implemented` with
> `{ "message": "Pricing recompute is not available until the ledger (Phase 2) ships." }`
> (`Modules/Billing/Endpoints/PricingEndpoints.cs`). The ledger *has* shipped; what
> is still missing is the recompute job that would write the compensating
> `BlossomLedgerEntries`, so the route remains an honest placeholder. The
> `AiUsageRecord` pricing-snapshot columns it needs already exist (Phase 2,
> `AddAiUsageRecordPricingSnapshot`).

**Purpose (intended):** Re-price already-recorded usage that the rule affects,
writing compensating ledger entries. **Never mutates `AiUsageRecord`.**
**Body (intended):** `{ "asOf": "<iso8601>", "dryRun": true|false }`.
**Response `200` (intended, not produced today):**

```json
{
  "asOf": "2026-09-30T23:59:59Z",
  "dryRun": true,
  "affectedProviders": ["openai"],
  "affectedModels": ["gpt-4o"],
  "recordCount": 412,
  "currentBlossomTotal": 1382.6,
  "recomputedBlossomTotal": 1152.2,
  "deltaBlossoms": -230.4,
  "organizationsAffected": 4,
  "ledgerEntriesWritten": 0
}
```

**Errors (today):** `401`, `403` (requires `pricing:backdate`), then `501`.
**Errors (intended):** add `400`, `404`, `503` if the recompute queue is full.
**Notes:** when implemented, always run `dryRun: true` first. This is the
highest-impact administrative operation in the API.

#### `GET` · `POST` · `PATCH` · `DELETE /api/v1/admin/pricing/price-book[/{entryId:guid}]`

The Blossom→LKR price book. Same effective-dating semantics and the same overlap
rule. `POST` body: `{ planTier?, organizationId?, skuKind, skuCode?, blossomQuantity,
priceLkr, effectiveFrom, changeReason }` where `skuKind` ∈
`PlanAllowance | TopUpPack | OverageUsage`.
**Errors:** identical set to the rules endpoints.

`GET /price-book` accepts optional `skuKind`, `planTier` and `organizationId`
filters and returns a **bare JSON array of entries — there is no `page`/`pageSize`
and no `{ items, total }` envelope**. This is a known, deliberate deviation from the
§A.4 pagination convention (finding M-8, deferred to a dedicated pagination pass);
callers must not expect the page envelope here even though every neighbouring list
endpoint returns one.

`GET /price-book/{entryId:guid}` accepts an optional `organizationId` query filter.
A global entry (no `organizationId`) is always readable; a per-organization override
is readable only when the caller passes that same `organizationId` (#237).

---

### C.2 Blossom balance and operations (Phase 2)

> **Status: implemented** on `feature/admin-backend-api` (issues #194 and #195).
> Balance, usage, statement, top-ups and the admin credit/debit/revoke/statement
> operations are live; the recompute-dependent pricing endpoint remains 501.

---

#### `GET /api/v1/orgs/{organizationId:guid}/blossoms/balance`

**Auth:** `billing:view` (org-scoped). **Purpose:** the authoritative balance for
the dashboard's Blossom widget.
**Response `200`:**

```json
{
  "organizationId": "0198f3c2-...",
  "periodStart": "2026-09-01T00:00:00Z",
  "periodEnd": "2026-10-01T00:00:00Z",
  "periodIsClosed": false,
  "planTier": "Bloom",
  "monthlyBlossomLimit": 750.0,
  "blossomGranted": 500.0,
  "blossomAdjusted": 0.0,
  "blossomUsed": 132.4,
  "blossomRemaining": 1117.6,
  "percentUsed": 10.6,
  "lowBalanceThresholdPercent": 20.0,
  "status": "Active",
  "nextExpiringGrant": {
    "blossomDelta": 500.0,
    "expiresAt": "2026-10-01T00:00:00Z"
  },
  "asOf": "2026-09-11T09:30:00Z"
}
```

**Errors:** `401`, `403`, `404` (org not found **or** caller not a member).
**Related statistics:** [S-1](../backend/statistics-catalog.md).
**Client guidance:** do not cache. Poll at most once per 30 s; the value changes
only on consumption or an adjustment.

---

#### `GET /api/v1/orgs/{organizationId:guid}/blossoms/usage`

**Params:** `from`, `to` (ISO 8601 UTC, required, max 92 days), `groupBy`
(`day` \| `provider` \| `model` \| `workflowId`), `page`, `pageSize`.
**Response `200`:**

```json
{
  "window": { "from": "2026-09-01T00:00:00Z", "to": "2026-09-11T00:00:00Z", "timezone": "UTC" },
  "totalBlossoms": 132.4,
  "totalNormalizedUnits": 132400,
  "series": [
    { "key": "2026-09-09", "blossoms": 41.2, "normalizedUnits": 41200, "workflowCount": 12 },
    { "key": "2026-09-10", "blossoms": 91.2, "normalizedUnits": 91200, "workflowCount": 27 }
  ],
  "generatedAt": "2026-09-11T09:30:00Z"
}
```

**Errors:** `400` for a missing/oversized window or an unsupported `groupBy`;
`401`; `403`.
**Related statistics:** [S-2](../backend/statistics-catalog.md).

---

#### `GET /api/v1/orgs/{organizationId:guid}/blossoms/statement`

> **Changed in Revenue Ledger R4 (issue #345).** The statement is now paged, filtered and ordered
> **server-side**, gained query parameters and response fields, and its window cap moved from 92 to
> **400 days**. The wire change is deliberate; the reasoning is below.

**Purpose:** full statement of account with a reconciliation check.

**Params:**

| Param | Meaning |
| --- | --- |
| `from`, `to` | The UTC window. Capped at `Billing:StatementMaxWindowDays` (**400**, matching the retention S-3 claims); a longer one is `400` **naming the effective limit** rather than silently shortened |
| `page`, `pageSize` | `pageSize` must be within `[1, 200]`; outside it is `400` **naming the range**, not clamped |
| `kind` | `all` \| `entitlement` \| `consumption`. An unrecognised value is `400`, never ignored |
| `entryType` | Restricts entitlement rows to one `BlossomLedgerEntryType`. Unrecognised is `400` |
| `sourceKind` | Restricts entitlement rows to one `BlossomSourceKind` |
| `q` | Substring match over the reason or the source reference; for consumption rows, over the workflow id, provider or model |
| `minAmount`, `maxAmount` | Inclusive bounds on the **signed** ledger movement |

**Two filters select the ledger leg only.** `entryType` and an amount bound are both
ledger concepts — a consumption row has no entry type and no delta — so a request carrying either
returns **no** consumption rows. This is worth stating because both were bugs before it held: the
consumption leg ignored `entryType` entirely, and its zero delta satisfied `delta >= 1`.

**Ordering is total and stable** (`occurredAt DESC, id DESC`; `id` is UUIDv7, so it is monotonic).
A row can therefore be neither repeated nor skipped across pages, which is what makes the pager
trustworthy. `total` is the window total, not the page length, and it is the same on every page.

**Response `200`:**

```json
{
  "organizationId": "0198f3c2-...",
  "periodStart": "2026-09-01T00:00:00Z",
  "periodEnd": "2026-10-01T00:00:00Z",
  "openingBalance": 750.0,
  "items": [
    {
      "id": "0198f3c2-...",
      "occurredAt": "2026-09-01T00:00:00Z",
      "kind": "Entitlement",
      "entryType": "PeriodAllocation",
      "blossomDelta": 750.0,
      "balanceAfter": 750.0,
      "reason": "Bloom plan allowance for September 2026",
      "sourceKind": "PlanChange",
      "sourceRef": null,
      "expiresAt": null,
      "createdByUserId": null
    },
    {
      "id": "0198f3c2-...",
      "occurredAt": "2026-09-09T14:02:11Z",
      "kind": "Consumption",
      "entryType": null,
      "blossomDelta": -3.7,
      "balanceAfter": 612.9,
      "reason": "Blossom consumption.",
      "sourceKind": null,
      "sourceRef": null,
      "expiresAt": null,
      "createdByUserId": null,
      "provider": null,
      "model": null,
      "normalizedUnits": null,
      "actualCostUsd": null
    }
  ],
  "total": 39,
  "page": 1,
  "pageSize": 50,
  "closingBalance": 617.6,
  "reconciliation": {
    "projectedBalance": 617.6,
    "ledgerDerivedBalance": 617.6,
    "drift": 0.0,
    "isConsistent": true
  },
  "generatedAt": "2026-09-11T09:30:00Z",
  "maxWindowDays": 400,
  "dataQuality": {
    "reconciliationChecked": true,
    "openingBalanceFromProjection": true,
    "windowCapped": false,
    "maxWindowDays": 400,
    "notes": [
      "The opening balance is derived from the balance projection, not accumulated forward from the ledger rows in this window."
    ]
  }
}
```

**The four additive item fields** are `null` on an entitlement row and populated on a consumption one:
`provider`, `model`, `normalizedUnits` (input + output + cached tokens) and `actualCostUsd`. **On the
org-scoped route they are always `null`**, and a consumption row's `reason` is the neutral
`"Blossom consumption."` with its `sourceRef` (the agent workflow id) nulled: a boutique reads its
usage in Blossoms, and runs, tokens, provider/model and USD cost are agent internals. The team-only
admin route (`GET /api/v1/admin/orgs/{id}/blossoms/statement`, `billing:adjust`) keeps the full
detail. The `q` filter still matches the stored provider/model/workflow id on both routes, because
that is how the row is found.

**`availableToRevoke`** is present on a row that is a revocable grant and absent otherwise. It is
how the console offers a revoke action instead of letting an operator discover non-revocability from
a `409`. The `409 grant-not-revocable` response remains the server's authoritative answer, and the
two cannot disagree: both apply the same rule — the right entry type, a positive delta, not expired,
and not already fully revoked.

**`dataQuality` states what the numbers rest on.** `openingBalanceFromProjection` is always `true`
today: the opening balance is derived **backwards** from the cached `BlossomRemaining` projection,
not accumulated forward from the ledger, so it inherits that row's state. `reconciliationChecked`
is `false` when the reconciliation could not be evaluated, and a client must render that as
*"reconciliation status unknown"* — **never** as consistent.

**Errors:** `400` (bad window, page size outside `[1, 200]`, unrecognised `kind` or `entryType`,
`min > max`), `401`, `403`.
**Related statistics:** [S-3](../backend/statistics-catalog.md).
**Client guidance:** `reconciliation.isConsistent = false` is a **Critical**
condition. Surface it to the owner and to Aveline support; do not silently render a
balance.

---

#### `POST /api/v1/orgs/{organizationId:guid}/blossoms/top-ups`

**Auth:** `billing:manage` (org-scoped). **Idempotency: required.**
**Purpose:** purchase a Blossom pack.
**Body:**

| Field | Type | Required | Validation |
| --- | --- | --- | --- |
| `skuCode` | string | ✅ | Must exist in the price book as an `Active` `TopUpPack`, e.g. `blossom_pack_500` |
| `blossomQuantity` | number | — | Must equal the SKU's quantity; a mismatch is `400` |
| `paymentReference` | string | — | Max 128; required when `Billing:RequirePaymentReference` is true |

**Response `201` — the shipped `BlossomLedgerEntryDto`:**

```json
{
  "id": "0198f3c2-...",
  "entryType": "TopUpGrant",
  "blossomDelta": 500.0,
  "blossomBalanceAfter": 1117.6,
  "reason": "Top-up purchase blossom_pack_500 (500 Blossoms).",
  "sourceKind": "PaymentProvider",
  "sourceRef": null,
  "expiresAt": "2026-10-01T00:00:00Z",
  "createdByUserId": "0198f3c2-...",
  "createdAt": "2026-09-11T09:35:00Z"
}
```

> **Field-name note.** The `201` body on every Blossom *grant* route (this top-up and
> the four admin operations below) is the shipped `BlossomLedgerEntryDto`, whose
> balance and timestamp fields are **`blossomBalanceAfter`** and **`createdAt`**. It
> carries no `balanceAfter`, `occurredAt`, `kind`, `skuCode` or `priceLkr`. The
> statement line items in §C.2 use a **different** DTO (`BlossomStatementItem`:
> `occurredAt`, `kind`, `balanceAfter`). Do not deserialise one as the other; see
> finding M-17.

**Errors:** `400 validation`; `400 { "code": "unknown-sku" }`; `400` quantity
mismatch; `401`; `403`; `409 { "code": "period-closed" }`;
`409 { "code": "idempotency-key-reuse" }`.
**Notes:** expiry is capped at the subscription's `CurrentPeriodEnd` unless
`Billing:AllowCrossPeriodTopUps` is true. **Phase 3** attaches a payment provider;
until then a top-up is a recorded grant, not a charge.

---

#### Admin Blossom operations

All four require `billing:adjust` (Aveline team only) and `Idempotency-Key`.

| Method | Path | Body | Response |
| --- | --- | --- | --- |
| `POST` | `/api/v1/admin/orgs/{organizationId:guid}/blossoms/credit` | `{ amount, reason, expiresAt?, sourceKind?, sourceRef? }` | `201` `BlossomLedgerEntryDto` (see the field-name note above) |
| `POST` | `/api/v1/admin/orgs/{organizationId:guid}/blossoms/debit` | `{ amount, reason, allowNegative? }` | `201` `BlossomLedgerEntryDto` (see the field-name note above) |
| `POST` | `/api/v1/admin/orgs/{organizationId:guid}/blossoms/revoke` | `{ ledgerEntryId, reason }` | `201` `BlossomLedgerEntryDto` (see the field-name note above) |
| `GET` | `/api/v1/admin/orgs/{organizationId:guid}/blossoms/statement` | query: `from`, `to`, `page`, `pageSize`, `kind`, `entryType`, `sourceKind`, `q`, `minAmount`, `maxAmount` | same shape as the org statement (§B.9) |

**Validation:**

| Field | Rule |
| --- | --- |
| `amount` | `> 0`, max 4 dp, `<= Billing:MaxAdjustmentBlossoms` (default 10 000) |
| `reason` | 10–500 chars, **required** |
| `expiresAt` | ISO 8601, must be after now |
| `allowNegative` | boolean, default `false` |
| `ledgerEntryId` | must reference an existing **positive, non-expired, non-revoked** grant |

**Errors:** `400` (validation, cap exceeded); `401`; `403`;
`404` org not found; `409 { "code": "insufficient-balance", "available": 120.0, "requested": 500.0 }`;
`409 { "code": "grant-not-revocable", "availableToRevoke": 250.0 }`;
`409 { "code": "period-closed" }`; `409 { "code": "idempotency-key-reuse" }`.

**Example — credit:**

```bash
curl -X POST "https://api.aveline.app/api/v1/admin/orgs/$ORG_ID/blossoms/credit" \
  -H "Authorization: Bearer $ADMIN_JWT" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: credit-2026-09-11-org-a-001" \
  -d '{
    "amount": 250.0,
    "reason": "Goodwill credit for the 2026-09-08 agent outage.",
    "expiresAt": "2026-10-01T00:00:00Z",
    "sourceKind": "Admin",
    "sourceRef": "SUP-1042"
  }'
```

**Errors response example:**

```json
{
  "message": "The debit would overdraw the balance. Available: 120.0, requested: 500.0.",
  "code": "insufficient-balance",
  "available": 120.0,
  "requested": 500.0,
  "requestId": "6f1c2a90-4b7e-4d21-9c3a-8e5f1b2d4c60"
}
```

---

### C.3 Subscription and entitlements (Phase 2)

> **Status: implemented** on `feature/admin-backend-api` (issue #196). A `nextPeriod`
> plan change is recorded on the subscription and applied at period rollover.

#### `GET /api/v1/orgs/{organizationId:guid}/subscription`

**Auth:** `billing:view`. **Response `200`:**

```json
{
  "organizationId": "0198f3c2-...",
  "planTier": "Bloom",
  "billingCycle": "Monthly",
  "status": "Active",
  "currentPeriodStart": "2026-09-01T00:00:00Z",
  "currentPeriodEnd": "2026-10-01T00:00:00Z",
  "seatsIncluded": 3,
  "priceLkr": 3500.0,
  "currency": "LKR",
  "cancelAtPeriodEnd": false,
  "cancelledAt": null,
  "externalProvider": null
}
```

**Errors:** `401`, `403`, `404`. **If no subscription row exists yet:** `200` with
`status: "None"` and the tier derived from `Organization.PlanTier`, so the
frontend never has to handle a 404 for a valid org.

#### `POST /api/v1/orgs/{organizationId:guid}/subscription/change-plan`

**Auth:** `billing:manage`. **Idempotency: required.**
**Body:** `{ "planTier": "Orchid", "effective": "immediate" | "nextPeriod", "reason": string? }`
(default `nextPeriod` for a downgrade, `immediate` for an upgrade).
**Response `200`:** the updated subscription, plus `blossomDelta`, plus the new
balance.
**Errors:**

```json
// 409 — downgrade blocked
{
  "message": "The target plan's limits are exceeded by current usage.",
  "code": "plan-limit-violation",
  "violations": [
    { "key": "blossoms.monthly", "observed": 1840.0, "allowed": 750.0 },
    { "key": "staff.max",       "observed": 7,       "allowed": 3 }
  ]
}
```

Also `400 { "code": "no-op-plan-change" }`, `403`, `404`.
**Related statistics:** [S-8](../backend/statistics-catalog.md), [S-9](../backend/statistics-catalog.md).
**Client guidance:** on `plan-limit-violation`, render each `violations` entry with
its `key` mapped to a human label and the observed/allowed pair.

#### `POST /api/v1/orgs/{organizationId:guid}/subscription/cancel`

**Auth:** `billing:manage`. **Body:** `{ "reason": string? }`. Sets
`cancelAtPeriodEnd = true`. **Response `200`.** **Errors:** `403`, `404`, `409` if
already cancelled.

#### `GET /api/v1/orgs/{organizationId:guid}/entitlements`

**Auth:** `billing:view`. **Response `200`:**

```json
{
  "organizationId": "0198f3c2-...",
  "planTier": "Bloom",
  "items": [
    { "key": "blossoms.monthly",       "valueType": "Decimal", "value": 750.0,  "source": "Plan",     "effectiveFrom": "2026-09-01T00:00:00Z" },
    { "key": "staff.max",              "valueType": "Integer", "value": 3,      "source": "Plan",     "effectiveFrom": "2026-09-01T00:00:00Z" },
    { "key": "customers.active.max",   "valueType": "Integer", "value": 250,    "source": "Plan",     "effectiveFrom": "2026-09-01T00:00:00Z" },
    { "key": "api.access",             "valueType": "Boolean", "value": false,  "source": "Plan",     "effectiveFrom": "2026-09-01T00:00:00Z" },
    { "key": "api.requests.perMinute", "valueType": "Integer", "value": 60,     "source": "Plan",     "effectiveFrom": "2026-09-01T00:00:00Z" },
    { "key": "analytics.level",        "valueType": "String",  "value": "basic","source": "Plan",     "effectiveFrom": "2026-09-01T00:00:00Z" }
  ],
  "generatedAt": "2026-09-11T09:30:00Z"
}
```

**The full key list** is in
[domain-model.md §4.5](../backend/domain-model.md). Frontend should drive all plan
gating from this endpoint rather than hardcoding a plan matrix.

#### `GET /api/v1/orgs/{organizationId:guid}/entitlements/usage`

**Auth:** `billing:view`. **Response `200`:**

```json
{
  "items": [
    { "key": "blossoms.monthly",     "observed": 132.4, "allowed": 750.0, "percentUsed": 17.7, "hardLimit": false },
    { "key": "staff.max",            "observed": 3,     "allowed": 3,     "percentUsed": 100.0, "hardLimit": true },
    { "key": "customers.active.max", "observed": 118,   "allowed": 250,   "percentUsed": 47.2, "hardLimit": true }
  ],
  "materialisedAt": "2026-09-11T09:25:00Z",
  "dataQuality": { "materialisedCounts": true }
}
```

**Errors:** `401`, `403`.
**Related statistics:** [S-10](../backend/statistics-catalog.md).

---

### C.4 API keys (Phase 3)

> **Implemented in Phase 3** (issues #201–#204). Deviations from the plan below:
> the plaintext secret is returned in the 201 body as
> `{ key: ApiKeyDto, secret: string }` and never again; `DELETE` on a key that has
> served traffic returns **409** and the key must be revoked instead; creating a key
> with an **API key** (rather than a user token) returns **403** — a machine
> credential may not mint another key; `Environment` is `live` or `test` and defaults
> to `live`. Key scope grants are rejected at creation and at authorization time.

---

#### `POST /api/v1/orgs/{organizationId:guid}/api-keys`

**Auth:** `apikeys:manage` (org-scoped). **Requires entitlement `api.access`.**
**Idempotency: recommended.**
**Body:**

| Field | Type | Required | Validation |
| --- | --- | --- | --- |
| `name` | string | ✅ | 1–100 chars |
| `scopes` | string[] | ✅ | Non-empty subset of the permission catalog, **excluding** `pricing:*`, `billing:adjust`, `admin:*` |
| `environment` | enum | — | `live` \| `test`; default `live` |
| `expiresAt` | string | — | ISO 8601, must be in the future |

**Response `201` — the secret is returned here and never again:**

```json
{
  "id": "0198f3c2-...",
  "name": "Warehouse sync",
  "prefix": "avl_live_7Qk2mZ",
  "secret": "avl_live_7Qk2mZpX9rT4vBnL6sW1cYhJ3dF8gAeU",
  "scopes": ["catalog:view", "customers:view"],
  "environment": "live",
  "status": "Active",
  "createdAt": "2026-09-11T09:40:00Z",
  "expiresAt": null,
  "lastUsedAt": null,
  "warning": "Store this secret now. It cannot be retrieved again."
}
```

**Errors:** `400 validation`; `400 { "code": "scope-not-permitted", "scopes": ["pricing:manage"] }`;
`403 { "code": "entitlement-required", "key": "api.access", "requiredPlan": "Rose" }`;
`404`.

---

#### `GET /api/v1/orgs/{organizationId:guid}/api-keys`

**Auth:** `apikeys:view`. **Params:** `page`, `pageSize`, `status?`.
**Response `200`:** `ApiKeyPage`. **`secret` and `keyHash` are never present.**
Each item: `{ id, name, prefix, scopes, environment, status, createdAt, expiresAt,
lastUsedAt, revokedAt, revokedReason, requestCount }`.
**Errors:** `401`, `403`.

#### `POST /api/v1/orgs/{organizationId:guid}/api-keys/{keyId:guid}/revoke`

**Auth:** `apikeys:manage`. **Body:** `{ "reason": string }` (max 300). **Idempotency: required.**
**Response `200`:** the updated key with `status: "Revoked"`. Effective within 60 s
on all instances. **Errors:** `400`, `403`, `404`, `409` if already revoked.

#### `DELETE /api/v1/orgs/{organizationId:guid}/api-keys/{keyId:guid}`

**Auth:** `apikeys:manage`. Hard-deletes a key **that has never been used**.
**Response `204`.** **Errors:** `403`, `404`,
`409 { "code": "key-in-use", "requestCount": 4211 }`.

**Frontend guidance:** after creating a key, show the secret **once** in a
copy-to-clipboard dialog with the warning text. Never store it in client state
beyond that dialog.

---

### C.5 Users and organization settings (Phase 3)

> **Implemented in Phase 3** (issues #205–#207). Deviations from the plan below:
> `DELETE /users/me` returns **200** with `{ message, accountState }` (idempotent on
> repeat); `GET /users/me/sessions` returns `[{ id, status, createdAt, lastActiveAt,
> expireAt }]` proxied from Clerk; `POST /users/me/sessions/revoke-all` returns
> `{ revoked }`; `PATCH /admin/users/{userId}/state` accepts `OnboardingPending`,
> `Active` or `Suspended`, persists the optional `reason` on the audit row, and
> enforces the FR-3.9 matrix with **409** on an invalid transition; `GET /admin/users`
> returns `{ items, page, pageSize, total }`, binds the documented `accountState` and
> `organizationId` filters through the membership table, pages at the §A.4 convention
> (50/200), and still accepts `state` as a legacy alias for `accountState`. New in
> this phase: `PATCH /orgs/{organizationId}` (settings, AI fields entitlement-gated,
> **409** on slug collision), `GET /orgs/{organizationId}/settings`,
> `GET /orgs/{organizationId}/members` (filters `status`, `role`, `q`) and
> `PATCH /orgs/{organizationId}/members/{userId}` (**403** non-owner owner-change,
> **409** self-change or last-owner demotion). `POST /webhooks/clerk` is anonymous
> and requires `Clerk:WebhookSecret` (**503** when unset, **401** on a bad signature).
>
> **Implemented in #241:** the audit read surface (`GET /admin/audit`,
> `GET /admin/audit/{entryId:guid}`), the Aveline-team organization search
> (`GET /admin/orgs`, FR-4.8) and per-organization entitlement overrides
> (`PATCH /admin/orgs/{organizationId}/entitlement-overrides`, FR-4.9). The audit log
> was previously write-only. `AuditViewPolicy` requires the team `owner`/`admin` role
> **and** the `audit:view` permission; the permission requirement was added in **A9 B2**.


| Method | Path | Policy | Body | Response |
| --- | --- | --- | --- | --- |
| `PATCH` | `/api/v1/users/me` | authenticated | `{ firstName?, lastName?, displayName?, phoneNumber?, profileImageUrl?, contactPreference?, pushNotificationsEnabled? }` | `UserDto` |
| `DELETE` | `/api/v1/users/me` | authenticated | `{ "reason": string? }` | `204` |
| `GET` | `/api/v1/users/me/sessions` | authenticated | — | `{ items: [{ id, ipAddressHash, userAgent, createdAt, lastActiveAt, isCurrent }] }` |
| `POST` | `/api/v1/users/me/sessions/revoke-all` | authenticated | `{ exceptCurrent?: boolean }` | `204` |
| `GET` | `/api/v1/admin/users` | `admin:users:read` | query: `page`, `pageSize`, `q`, `accountState`, `organizationId` | `AdminUserPage` |
| `PATCH` | `/api/v1/admin/users/{userId:guid}/state` | `admin:users:manage` | `{ accountState: "OnboardingPending" \| "Active" \| "Suspended", reason?: string }` | `UserDto` |
| `PATCH` | `/api/v1/orgs/{organizationId:guid}` | `settings:manage` | see below | `OrganizationProfileDto` |
| `GET` | `/api/v1/orgs/{organizationId:guid}/settings` | `settings:manage` | — | settings + entitlements |
| `GET` | `/api/v1/orgs/{organizationId:guid}/members` | `team:manage` | query: `page`, `pageSize`, `status?`, `role?`, `q?` | `MemberPage` |
| `PATCH` | `/api/v1/orgs/{organizationId:guid}/members/{userId:guid}` | `team:manage` | `{ boutiqueRole }` | `{ organizationId, userId, boutiqueRole, status }` |
| `GET` | `/api/v1/admin/orgs` | `admin:orgs:read` | query: `page`, `pageSize`, `q?`, `isActive?`, `planTier?` | `{ items, page, pageSize, total }` |
| `PATCH` | `/api/v1/admin/orgs/{organizationId:guid}/entitlement-overrides` | `billing:adjust` | `{ overrides: [{ key, valueType, value, reason, effectiveFrom?, effectiveTo? }] }` | `{ organizationId, entitlements }` |
| `GET` | `/api/v1/admin/audit` | `audit:view` | query: `page`, `pageSize`, `organizationId?`, `actorUserId?`, `entityType?`, `entityId?`, `action?`, `from?`, `to?` | `AuditPage` |
| `GET` | `/api/v1/admin/audit/{entryId:guid}` | `audit:view` | — | `AuditLogEntry` (**404** `{ message }` when unknown) |

**`PATCH /orgs/{organizationId}` validation:**

| Field | Max | Tier requirement |
| --- | --- | --- |
| `name` | 200 | — |
| `address` | 500 | — |
| `phoneNumber` | 50 | — |
| `description` | 1000 | — |
| `logoUrl` | 1000 | — |
| `brandVoice` | 500 | Bloom+ (`ai.customContext` != `none`) |
| `businessRules` | 1000 | Bloom+ |
| `preferredColorsFabrics` | 1000 | Bloom+ |
| `customerPreferences` | 1000 | Orchid+ |

A field the plan does not entitle returns
`400 { "code": "entitlement-required", "key": "ai.customContext", "requiredPlan": "Bloom", "field": "brandVoice" }`.

**`PATCH /orgs/{organizationId}/members/{userId}` errors:**
`400 { "code": "invalid-role" }`;
`403 { "code": "owner-role-requires-owner" }` if a non-owner tries to grant or
revoke `org:boutique_owner`;
`409 { "code": "last-owner" }`;
`409 { "code": "self-role-change" }`; `404`.

**`GET /members` item shape:**

```json
{
  "userId": "0198f3c2-...",
  "organizationId": "0198f3c2-...",
  "firstName": "Kaveesha",
  "lastName": "Silva",
  "email": "kaveesha@boutique.lk",
  "profileImageUrl": null,
  "boutiqueRole": "org:boutique_manager",
  "status": "Active",
  "createdAt": "2026-09-02T11:00:00Z",
  "updatedAt": "2026-09-05T09:00:00Z",
  "lastActiveAt": "2026-09-11T08:12:00Z"
}
```

**`GET /api/v1/admin/audit` item shape:**

```json
{
  "id": "0198f3c2-...",
  "occurredAt": "2026-09-11T09:35:00Z",
  "actorKind": "User",
  "actorUserId": "0198f3c2-...",
  "actorRef": "user_2abc...",
  "action": "blossom.ledger.adjust",
  "entityType": "BlossomLedgerEntry",
  "entityId": "0198f3c2-...",
  "reason": "Goodwill credit for the 2026-09-08 agent outage.",
  "requestId": "6f1c2a90-4b7e-4d21-9c3a-8e5f1b2d4c60",
  "before": null,
  "after": { "blossomDelta": 250.0, "balanceAfter": 1117.6 }
}
```

> **Secrets never appear in `before`/`after`.** The write path runs an
> `IAuditRedactor`. If a frontend ever sees a secret in an audit response, that is
> a security defect worth reporting.

`GET /admin/audit` returns the §A.4 envelope `{ items, page, pageSize, total }`
(newest first, default `pageSize` 50, max 200); `GET /admin/audit/{entryId:guid}`
returns one entry or **404** `{ message }`.

**`GET /api/v1/admin/orgs` item shape:**

```json
{
  "id": "0198f3c2-...",
  "name": "Aurora Boutique",
  "slug": "aurora-boutique",
  "clerkOrgId": "org_2abc...",
  "ownerUserId": "0198f3c2-...",
  "planTier": "Orchid",
  "isActive": true,
  "createdAt": "2026-01-11T09:35:00Z",
  "updatedAt": "2026-09-11T09:35:00Z"
}
```

**`PATCH /api/v1/admin/orgs/{organizationId:guid}/entitlement-overrides`:** the body is
a set of overrides (`Decimal`/`Integer` values are JSON numbers, `Boolean` is a JSON
boolean, `String` is a JSON string). Each override carries `key`, `valueType`, `value`,
`reason`, and the optional validity bounds `effectiveFrom` and `effectiveTo`. Each
override is upserted on `(organizationId, key, effectiveFrom)` and an
`entitlement.override.updated` audit entry is written. The **200** response is
`{ "organizationId": "...", "entitlements": [{ key, valueType, value, source, effectiveFrom }] }`
with the override carrying `source: "Override"`. An unknown key, unknown `valueType`
or a `value` that does not match its `valueType` is a **400** `{ message }`; an unknown
organization is a **404** `{ message }`.

---

### C.6 Agentic statistics (Phase 4)

> **The organisation-facing routes in this section were removed.** A boutique reads its usage in
> Blossoms; runs, tokens and provider cost are agent internals, so
> `/api/v1/orgs/{organizationId}/statistics/agents/**` is no longer mounted (every path answers
> **404**) and no boutique role holds `stats:view:agent`. The team-only subset —
> `GET /api/v1/admin/statistics/agents/{overview,runs,reliability}` behind `stats:system` — is the
> live surface. The sketch below is kept as the historical Phase 4 contract for that subset.

Base: `/api/v1/orgs/{organizationId:guid}/statistics/agents`. **Auth:** `stats:view:agent`.
All accept `from`, `to` (ISO 8601 UTC, max 92 days), plus the filters listed.

> **Implemented (Phase 4, issues #210–#213).** Deviations from the sketch below:
>
> - The window default is 30 days and the maximum enforced by the implementation is
>   **400 days** (the run-retention horizon), not 92.
> - An invalid `status` or `triggerKind` value is a `400 {"message": "..."}` rather
>   than a model-binding error.
> - `dataQuality` currently contains exactly five flags — `latencyInstrumented`,
>   `nodeFailuresObserved`, `perStepAttribution`, `toolInstrumented`,
>   `costInstrumented` — and **all are `false`** until the Python instrumentation
>   (gaps G-1…G-14) lands. `latencyInstrumented` returning `false` means
>   `GET /latency` returns a null series, not zeros.
> - Runs omit `requestId`/`errorMessage` on the wire; the run summary carries the
>   columns the M6 model persists. `page`/`pageSize` default to `1`/`50` (max 200).
> - The admin subset is exactly `GET /api/v1/admin/statistics/agents/overview`,
>   `/runs` and `/reliability` (`stats:system`, team-only).
> - `/internal/agent-runs` is documented under [§C.6.1](#c61-internal-agent-run-ingest).

---

#### `GET /runs`

**Params:** `from`, `to`, `status?`, `agentKey?`, `triggerKind?`, `customerId?`,
`conversationId?`, `page`, `pageSize`, `sort` (`startedAt|-startedAt|durationMs|-durationMs`).
**Response `200`:** `AgentRunPage`.

```json
{
  "items": [
    {
      "id": "0198f3c2-...",
      "workflowId": "a1b2c3d4e5f6478899aabbccddeeff00",
      "requestId": "6f1c2a90-4b7e-4d21-9c3a-8e5f1b2d4c60",
      "triggerKind": "WhatsAppInbound",
      "conversationId": "0198f3c2-...",
      "customerId": "0198f3c2-...",
      "initiatedByUserId": null,
      "status": "Succeeded",
      "agentsInvolved": ["customer_memory", "visual_insight"],
      "startedAt": "2026-09-11T09:12:03.114Z",
      "completedAt": "2026-09-11T09:12:11.902Z",
      "durationMs": 8788,
      "approvalWaitMs": null,
      "stepCount": 14,
      "toolCallCount": 6,
      "retryCount": 0,
      "inputTokens": 4820,
      "outputTokens": 1130,
      "cachedTokens": 0,
      "actualCostUsd": 0.0,
      "blossomUnits": 6.0,
      "errorCode": null,
      "errorMessage": null,
      "isUnattributed": false
    }
  ],
  "total": 412,
  "page": 1,
  "pageSize": 50,
  "dataQuality": {
    "latencyInstrumented": true,
    "nodeFailuresObserved": false,
    "perStepAttribution": false,
    "toolInstrumented": true,
    "retryInstrumented": false,
    "costIsEstimated": true
  },
  "generatedAt": "2026-09-11T09:30:00Z"
}
```

**Errors:** `400` (bad window), `401`, `403`.
**Related statistics:** [S-13](../backend/statistics-catalog.md).

#### `GET /runs/{workflowRunId:guid}`

Full step-by-step trace. **Response `200`:** the run plus `steps[]`, each
`{ id, stepIndex, agentKey, nodeName, stepKind, toolName, status, attemptNumber,
startedAt, completedAt, durationMs, provider, model, inputTokens, outputTokens,
cachedTokens, actualCostUsd, argsHash, resultBytes, errorCode }`.
**Errors:** `401`, `403`, `404`.
**Related statistics:** [S-22](../backend/statistics-catalog.md).

#### `GET /reliability`

**Params:** `from`, `to`, `groupBy` (`agentKey` \| `triggerKind` \| `nodeName` \| `day` \| `hour`).
**Response `200`:**

```json
{
  "series": [
    {
      "key": "customer_memory",
      "totalRuns": 380, "succeeded": 361, "failed": 15, "timedOut": 3, "cancelled": 1,
      "successRate": 0.9516,
      "byErrorCode": { "llm_timeout": 11, "tool_http_500": 4 }
    }
  ],
  "dataQuality": { "nodeFailuresObserved": false }
}
```

**Notes:** runs still `Running` or `PausedForApproval` are excluded from
`totalRuns`. **Related statistics:** [S-14](../backend/statistics-catalog.md), [S-15](../backend/statistics-catalog.md).

#### `GET /latency`

**Params:** `from`, `to`, `groupBy` (`agentKey` \| `triggerKind` \| `day` \| `hour`).
**Response `200`:**

```json
{
  "series": [
    {
      "key": "customer_memory",
      "sampleCount": 380,
      "avgMs": 4120, "p50Ms": 3870, "p95Ms": 9110, "p99Ms": 14200, "maxMs": 21040
    }
  ],
  "dataQuality": { "latencyInstrumented": true },
  "precision": "exact"
}
```

**If `sampleCount < Telemetry:MinSampleForPercentile` (default 20):** the
percentile fields are `null` and `precision` is `"insufficient-sample"` with a
`reason`. **Do not render zeros.**
**Errors:** `400`, `401`, `403`. **Related statistics:** [S-16](../backend/statistics-catalog.md).

#### `GET /steps`, `GET /tokens`, `GET /cost`, `GET /tools`, `GET /failures`, `GET /approvals`

Same envelope shape; filters and series keys per
[statistics-catalog.md §5](../backend/statistics-catalog.md):
[S-17](../backend/statistics-catalog.md) steps, [S-18](../backend/statistics-catalog.md) tokens,
[S-19](../backend/statistics-catalog.md) cost, [S-20](../backend/statistics-catalog.md) tools,
[S-15](../backend/statistics-catalog.md) failures, [S-21](../backend/statistics-catalog.md) approvals.

Each returns a `dataQuality` object naming exactly which flags are false. **Frontend
contract: if a flag is false, show a "not yet instrumented" note rather than a
zero, or the dashboard will report a system that appears healthy and idle.**

#### Internal ingest

#### `POST /internal/agent-runs`

**Auth:** `InternalServicePolicy`. **Not callable by frontends.**
**Idempotency:** enforced on `(organizationId, workflowId)`.
**Body:**

```json
{
  "organizationId": "0198f3c2-...",
  "workflowId": "a1b2c3d4e5f6478899aabbccddeeff00",
  "requestId": "6f1c2a90-4b7e-4d21-9c3a-8e5f1b2d4c60",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "triggerKind": "WhatsAppInbound",
  "triggerRef": "wamid.HBgM...",
  "conversationId": "0198f3c2-...",
  "customerId": "0198f3c2-...",
  "initiatedByUserId": null,
  "status": "Succeeded",
  "startedAt": "2026-09-11T09:12:03.114Z",
  "completedAt": "2026-09-11T09:12:11.902Z",
  "pausedAt": null,
  "resumedAt": null,
  "inputTokens": 4820,
  "outputTokens": 1130,
  "cachedTokens": 0,
  "actualCostUsd": 0.0,
  "errorCode": null,
  "errorMessage": null,
  "steps": [
    {
      "stepIndex": 0,
      "agentKey": "orchestrator",
      "nodeName": "intent_gate",
      "stepKind": "Decision",
      "status": "Succeeded",
      "attemptNumber": 1,
      "startedAt": "2026-09-11T09:12:03.114Z",
      "completedAt": "2026-09-11T09:12:03.180Z",
      "durationMs": 66
    },
    {
      "stepIndex": 1,
      "agentKey": "customer_memory",
      "nodeName": "parse",
      "stepKind": "LlmCall",
      "status": "Succeeded",
      "attemptNumber": 1,
      "startedAt": "2026-09-11T09:12:03.180Z",
      "completedAt": "2026-09-11T09:12:05.402Z",
      "durationMs": 2222,
      "provider": "openai",
      "model": "gpt-4o-mini",
      "inputTokens": 1820,
      "outputTokens": 240,
      "cachedTokens": 0,
      "actualCostUsd": 0.000412
    },
    {
      "stepIndex": 2,
      "agentKey": "customer_memory",
      "nodeName": "retrieve",
      "stepKind": "ToolCall",
      "toolName": "get_customer_memory",
      "status": "Succeeded",
      "attemptNumber": 1,
      "startedAt": "2026-09-11T09:12:05.402Z",
      "completedAt": "2026-09-11T09:12:05.590Z",
      "durationMs": 188,
      "argsHash": "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
      "resultBytes": 3120
    }
  ]
}
```

**Response `201`:**

```json
{
  "workflowRunId": "0198f3c2-...",
  "aiUsageRecordId": "0198f3c2-...",
  "blossomUnits": 6.0,
  "normalizedUnits": 5950,
  "pricingRuleId": "0198f3c2-...",
  "unitsPerBlossom": 1000,
  "roundingMode": "Ceiling",
  "roundingDecimals": 1,
  "stepsRecorded": 3,
  "idempotent": false
}
```

**Errors:** `400 { "error": "..." }` (internal convention); `400 { "code": "step-cap-exceeded", "max": 200 }`;
`401`; `409 { "code": "run-terminal", "status": "Succeeded" }` if a report conflicts
with a terminal run; `413` if the payload exceeds the step cap.

**Privacy guarantee:** `argsHash` and `resultBytes` are the **only** tool-call
detail accepted. A payload containing `args`, `result`, `prompt`, or `content` is
rejected with `400 { "code": "forbidden-field", "field": "args" }`, not silently
stored. **Related statistics:** [S-13](../backend/statistics-catalog.md)–[S-22](../backend/statistics-catalog.md).

---

### C.7 API consumption statistics (Phase 5)

Base: `/api/v1/orgs/{organizationId:guid}/statistics/api`. **Auth:** `stats:view`.
All accept `from`, `to`, ISO 8601 UTC, max `Telemetry:MaxWindowDays` (92) days.

| Endpoint | Extra filters | Response series key |
| --- | --- | --- |
| `GET /requests` | `groupBy` (`hour`\|`day`\|`routeTemplate`\|`statusClass`\|`apiKeyId`\|`userId`), `routeTemplate?`, `statusClass?`, `apiKeyId?`, `userId?` | count, errorCount, throttledCount |
| `GET /errors` | `groupBy`, `routeTemplate?` | clientErrorRate, serverErrorRate, throttleRate |
| `GET /latency` | `groupBy`, `routeTemplate?` | avg, p50, p95, p99, max, sampleCount |
| `GET /endpoints` | `sort` (`count`\|`-count`\|`errorRate`) | requestCount, errorRate, avgMs |
| `GET /users` | `groupBy` (`day`\|`userId`) | requestCount, errorRate |
| `GET /quota` | — | limit, used, remaining, percentUsed, resetsAt |
| `GET /slow-requests` | `minDurationMs?`, `limit?` (max 200) | individual rows |
| `GET /billable` | `month` | billableRequestCount |

**Response shape (`GET /requests` shown):**

```json
{
  "window": { "from": "2026-09-01T00:00:00Z", "to": "2026-09-11T00:00:00Z", "timezone": "UTC" },
  "totals": { "requestCount": 41230, "errorCount": 291, "throttledCount": 44, "billableRequestCount": 40806 },
  "series": [
    { "key": "2026-09-10T14:00:00Z", "requestCount": 5120, "errorCount": 31, "throttledCount": 6 }
  ],
  "generatedAt": "2026-09-11T09:30:00Z",
  "freshness": { "currentHourIsPartial": true, "lastRollupAt": "2026-09-11T09:05:00Z" }
}
```

**`GET /latency` response:**

```json
{
  "series": [
    {
      "key": "/api/v1/orgs/{organizationId}/usage",
      "sampleCount": 8412,
      "avgMs": 84, "p50Ms": 61, "p95Ms": 210, "p99Ms": 640, "maxMs": 3120
    }
  ],
  "precision": "bucket-interpolated",
  "bucketBoundsMs": [5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000, null]
}
```

**`GET /quota` response:**

```json
{
  "items": [
    { "metricKey": "api.requests.monthly", "apiKeyId": null, "limit": 1000000, "used": 41230, "remaining": 958770, "percentUsed": 4.12, "resetsAt": "2026-10-01T00:00:00Z" },
    { "metricKey": "api.requests.monthly", "apiKeyId": "0198f3c2-...", "limit": 250000, "used": 9000, "remaining": 241000, "percentUsed": 3.6, "resetsAt": "2026-10-01T00:00:00Z" }
  ],
  "generatedAt": "2026-09-11T09:30:00Z"
}
```

**Errors (all):** `400` for an oversized window, an unsupported `groupBy`, or a
malformed timestamp; `401`; `403`.

> **Implementation status (Phase 5, issues #220–#226): implemented with deviations.**
> The routes are live:
> `/api/v1/orgs/{organizationId:guid}/statistics/api/{requests,errors,latency,endpoints,users,quota,slow-requests,billable}`
> and `/api/v1/orgs/{organizationId:guid}/statistics/api-keys`, plus the team-only
> `/api/v1/admin/statistics/api/{requests,errors,latency,endpoints}` and
> `/api/v1/admin/statistics/api-keys`. Confirmed deviations from the shape above:
>
> - `groupBy` accepts only `hour`, `day` and `month` (default `hour`), not
>   `routeTemplate`/`statusClass`/`apiKeyId`/`userId`; those are filters instead.
>   `/quota` does not accept `groupBy`.
> - Unknown `status`, `statusClass` or `groupBy` values, a reversed window or a window
>   longer than `Telemetry:MaxWindowDays` (92) return `400` with `{ "message" }`.
> - `requests` returns flat totals (`requestCount`, `successCount`, `errorCount`,
>   `clientErrorCount`, `serverErrorCount`, `throttledCount`) plus a `dataQuality`
>   envelope, not the `window`/`series`/`freshness` wrapper shown above.
> - `latency` returns `p50Ms`/`p95Ms`/`p99Ms` as `null` with a `reason`
>   (`insufficient_samples`) below `Telemetry:MinSampleForPercentile` (20), and
>   `precision: "bucket-interpolated"`. The `bucketBoundsMs` array is not emitted.
> - `users`, `api-keys` and `slow-requests` return the
>   `{ items, page, pageSize, total, dataQuality }` envelope.
> - Quota is enforced only when `Quotas:EnforcementEnabled` is `true` (default
>   `false`); a limit of `0` means unlimited. When enforcing and exhausted the
>   documented `429 { message, quota: { metricKey, limit, used, resetsAt } }` is
>   returned.
> - Hour→day compaction beyond 90 days and the load-test p99 gate are deferred; see
>   [backend README](../backend/README.md#implementation-status-phase-5--api-consumption-statistics).

**Frontend guidance:**
- `precision: "bucket-interpolated"` means the percentiles are approximate within
  the containing bucket. Label them as approximate.
- `freshness.currentHourIsPartial: true` means the last point is incomplete —
  render it differently or exclude it from a trend line.
- A `429` from a **quota** carries a `quota` object; a `429` from a **rate limit**
  does not. Branch on the presence of that object.

**Related statistics:** [S-24](../backend/statistics-catalog.md)–[S-32](../backend/statistics-catalog.md).

#### `GET /api/v1/orgs/{organizationId:guid}/statistics/api-keys`

**Auth:** `stats:view`. **Params:** `from`, `to`, `groupBy` (`apiKeyId` \| `day` \| `endpoint`).
**Response series item:** `{ key, name, requestCount, errorRate, avgMs,
lastUsedAt, topEndpoint }`. **Related statistics:** [S-28](../backend/statistics-catalog.md).

> **Deferred (not implemented).** The three organization billing statistics —
> `GET /orgs/{id}/statistics/billing/burn-rate`, `GET /orgs/{id}/statistics/customers/active`
> and `GET /orgs/{id}/statistics/staff/seats` — have no route and return **404**. They
> are listed, with their intended shapes, in the fenced
> **Appendix P — Planned / not yet implemented** at the end of this document and are
> **not** part of the normative contract.

---

### C.8 System statistics (Phase 6)

> **Status: partially implemented** on `feature/admin-backend-api` (issues #227–#230).
> The schema, the metric collector and retention job, the alert evaluation service and
> the **eight `/system/*` endpoints listed in the table below** are live. The five
> **`/billing/*` statistics endpoints are deferred** (see below); the earlier draft of
> this section presented all thirteen rows as one "live" table, which was wrong — only
> the `/system/*` eight exist.
> Confirmed deviations: the metrics endpoint names its
> granularity parameter `windowSize` (not `groupBy`) and accepts
> `instant|minute|hour|day`; an unknown metric returns an empty series with a
> `dataQuality.omitted` reason rather than a 400; `inbound_message_backlog` and
> `publish_latency_ms` are reported in `omitted` because the current schema cannot measure
> them; `GET /system/eventbus` binds no `from`/`to` and returns instantaneous counters
> (finding M-11); `GET /system/overview` is **not** cached server-side despite the
> 15-second TTL this document previously claimed (finding M-10); and only an
> organization-scoped critical alert creates a `NotificationRecord` because
> `NotificationRecords.OrganizationId` is a required FK to `Organizations`.

Base: `/api/v1/admin/statistics`. **Auth:** the `StatsSystem` policy — the team
`owner`/`admin` role **and** `stats:system` (A9 B2); Aveline team only.

| Endpoint | Params | Returns |
| --- | --- | --- |
| `GET /system/overview` | — | Composite: readiness, uptime, version, top alerts, throughput, error rate, queue depths, running agent runs |
| `GET /system/metrics` | `metric`, `from`, `to`, `windowSize?` | Time series for one named metric |
| `GET /system/queues` | — | Queue depths, including running agent runs |
| `GET /system/errors` | `from`, `to`, `groupBy` | Error rate and unhandled exception count |
| `GET /system/throughput` | `from`, `to`, `groupBy` | RPS, agent runs/min, Blossoms/hour |
| `GET /system/eventbus` | **none** (`from`/`to` are accepted but ignored) | Published, delivered, failed, publish latency — an instantaneous counter snapshot |
| `GET /system/alerts` | `status?`, `severity?`, `ruleId?`, `page`, `pageSize` | `SystemAlertPage` |
| `POST /system/alerts/{alertId:guid}/acknowledge` | body `{ "note": string? }` | Updated alert; **404** when unknown, **409 `{ message }`** when the alert is already `Resolved` (**A9 B3** — a resolved alert is terminal) |

> **`GET /system/eventbus` is windowless (M-11).** The handler binds only the
> statistics service and the cancellation token, so the documented `from`/`to`
> parameters are **ignored**, not honoured: the response is a direct
> `EventBusMetrics.Snapshot()` of cumulative counters. Honouring a time window would
> need new storage; the deviation is deliberate and tracked in
> [backend/README.md](../backend/README.md#deferred-medium-findings-issue-242).

> **Deferred (not implemented).** The five billing-analysis endpoints —
> `GET /billing/profitability` (`from`, `to`, `groupBy`), `GET /billing/org-usage`
> (`window`, `planTier?`, `page`, `pageSize`), `GET /billing/adjustments`
> (`from`, `to`, `entryType?`, `actorUserId?`), `GET /billing/plan-changes`
> (`from`, `to`) and `GET /billing/downgrades` (`from`, `to`) — have no route and
> return **404**. They are listed, with their intended shapes, in the fenced
> **Appendix P — Planned / not yet implemented** at the end of this document and are
> **not** part of the normative contract.

**`GET /system/overview` response:**

```json
{
  "version": { "gitSha": "902f27f", "buildTime": "2026-09-11T06:00:00Z", "assemblyVersion": "1.0.0", "environment": "Production" },
  "readiness": { "status": "Healthy", "checks": [
    { "name": "database", "status": "Healthy", "durationMs": 4, "message": null },
    { "name": "redis", "status": "Healthy", "durationMs": 2, "message": null },
    { "name": "agent-service", "status": "Healthy", "durationMs": 31, "message": null },
    { "name": "clerk-jwks", "status": "Degraded", "durationMs": 1204, "message": "JWKS served from cache" }
  ]},
  "uptimeSeconds": 412300,
  "alerts": { "critical": 0, "warning": 1, "top": [
    { "id": "0198f3c2-...", "severity": "Warning", "title": "agent.failure.rate above threshold", "firedAt": "2026-09-11T08:40:00Z", "observedValue": 0.78, "threshold": 0.8 }
  ]},
  "throughput": { "requestsPerSecond": 34.2, "agentRunsPerMinute": 2.1, "blossomsPerHour": 41.6 },
  "errors": { "errorRate": 0.0071, "unhandledExceptionsLast15m": 2 },
  "queues": { "telemetryChannelDepth": 12, "eventBusBacklog": 0, "notificationBacklog": 0, "inboundMessageBacklog": 3, "agentRunsRunning": 2 },
  "generatedAt": "2026-09-11T09:30:00Z"
}
```

**`GET /system/alerts` item:**

```json
{
  "id": "0198f3c2-...",
  "ruleId": "0198f3c2-...",
  "ruleName": "agent.failure.rate",
  "organizationId": null,
  "metricName": "aveline.agent.success_rate",
  "severity": "Warning",
  "status": "Firing",
  "title": "agent.failure.rate above threshold",
  "detail": "Success rate 78% over the last 15 minutes (threshold 80%).",
  "observedValue": 0.78,
  "threshold": 0.80,
  "occurrenceCount": 4,
  "firedAt": "2026-09-11T08:40:00Z",
  "lastObservedAt": "2026-09-11T09:29:00Z",
  "acknowledgedAt": null,
  "resolvedAt": null
}
```

**Errors:** `401`, `403`, `404` for alert acknowledge.

---

### C.9 Health and metrics (Phase 0)

> **Status: implemented** on `feature/admin-backend-api` (issues #180 and #181).
> `/health/live`, `/health/ready`, `/health`, and the authenticated `/metrics`
> endpoint are live.

#### `GET /health/live`

**Auth:** anonymous. Always `200 { "status": "Healthy" }` if the process answers.
Never checks a dependency.

#### `GET /health/ready` (and `/health`)

**Auth:** anonymous. **Response `200`** when all critical checks pass,
**`503`** otherwise.

```json
{
  "status": "Healthy",
  "version": { "gitSha": "902f27f", "buildTime": "2026-09-11T06:00:00Z", "assemblyVersion": "1.0.0", "environment": "Production" },
  "totalDurationMs": 41,
  "checks": [
    { "name": "database", "status": "Healthy", "durationMs": 4, "message": null },
    { "name": "redis", "status": "Healthy", "durationMs": 2, "message": null },
    { "name": "agent-service", "status": "Healthy", "durationMs": 31, "message": null },
    { "name": "clerk-jwks", "status": "Healthy", "durationMs": 3, "message": null }
  ]
}
```

`status` ∈ `Healthy` \| `Degraded` \| `Unhealthy`. **`Degraded` still returns
`200`.** `message` never contains a connection string, hostname, credential, or
stack trace.

> **The `version` block is environment-dependent (M-3, #242).** It is included in
> Development/Staging and **omitted in Production**, where `HealthCheckResponseWriter`
> withholds the git SHA, build time and environment so the anonymous probe cannot
> fingerprint the release. The example above is a non-Production payload.

#### `GET /metrics`

**Auth:** the `MetricsPolicy`, which accepts **either** of two credential schemes:

- **Internal service token** — `X-Internal-Token: <AgentService:InternalToken>`
  (scheme `InternalToken`, role `InternalService`); or
- **Scrape token** — `Authorization: Bearer <Metrics:ScrapeToken>` (scheme
  `ScrapeToken`). The scrape scheme yields no result when `Metrics:ScrapeToken` is
  unset, so it can never authenticate by accident.

**The scrape token has no committed default (Slice 1, R-1).** It is empty in
`.env.example`, `docker compose` declares it as `${METRICS_SCRAPE_TOKEN:?...}` so an unset
value fails the stack, and `MetricsSecurityGuard.EnsureScrapeTokenForProduction` refuses to
boot the API in Production without it. Before Slice 1 the only working credential was the
internal service token, whose compose default is a literal in this repository; Prometheus is
now a dedicated, scrape-only caller and must not reuse it. Generate one with
`openssl rand -hex 32`.

**Response `200`** `text/plain; version=0.0.4` (Prometheus exposition format).
`401` without credentials.

**Metric names are translated, not passed through.** The pinned exporter
(`OpenTelemetry.Exporter.Prometheus.AspNetCore 1.18.0-beta.1`) defaults to
`UnderscoreEscapingWithSuffixes`: it replaces `.` with `_`, maps and appends the instrument's
unit unless the sanitised name already ends with it, and appends `_total` to counters. The
dotted name is the internal key (the Postgres `SystemMetricSamples.MetricName` value and the
key the seeded alert-rule guard watches); the **Prometheus series name is what a PromQL
expression must use**. `MetricsCatalog` in `Aveline.Api/Configurations/MetricsConfiguration.cs`
is the one place these names are authored and `MetricsNamingTests` asserts every one of them
against a live scrape.

| Dotted name (internal key) | Prometheus series |
| --- | --- |
| `aveline.api.error_rate` | `aveline_api_error_rate_ratio` |
| `aveline.api.latency_p95` | `aveline_api_latency_p95_milliseconds` |
| `aveline.process.cpu_seconds` | `aveline_process_cpu_seconds_total` |
| `aveline.blossom.balance` | `aveline_blossom_balance_count` |
| `aveline.events.published` (counter) | `aveline_events_published_events_total` |
| `aveline.events.publish_latency_ms` (histogram) | `aveline_events_publish_latency_ms_milliseconds_{bucket,sum,count}` |

The full table is the 23 entries of `MetricsCatalog.All`. Note the two asymmetries the
translation produces: `aveline.process.thread_count` gains nothing because the sanitised name
already ends in the unit `count`, while `aveline.process.threadpool_queue_length` gains
`_count`; and a histogram publishes one `# TYPE` line with three sample families. Do not copy
an expression from a dashboard found online — it targets a different exporter version and a
different translation strategy.

**Meters registered (Slice 2).** `AddMeter` covers `Aveline.Api` (the bridged business gauges),
`Aveline.Api.Eventing` (the event-bus counters and publish-latency histogram) and `Npgsql`
(the eleven `db.client.*` pool and query instruments). Before Slice 2 only `Aveline.Api` was
registered and no instrument used it, so fifteen instruments were produced and silently
dropped.


---

### C.10 Home focus surface (Phase 7 — Home tab)

The Home tab's deck is served by a **derived** feed and one write route. There is
no task table: every docket points at a fact that already exists.

#### `GET /api/v1/orgs/{organizationId}/stats/home`

**Auth:** the `BoutiqueAccess` policy — an active membership for the organization
in the route whose role grants `catalog:view`, which every staff role holds.

**Response `200`:**

| Field | Meaning |
| --- | --- |
| `generatedAt` | Server time the feed was derived. |
| `window.localDate` / `window.timeZone` | The organization-local day actually used, from `Organization.TimeZone`. |
| `dataQuality.*` | Which sources were readable and non-empty (`wardrobeAvailable`, `patronAvailable`, `commerceAvailable`, `logisticsAvailable`). An absent domain is explained, not measured as zero. |
| `items[]` | Dockets: `id`, `sourceKey`, `domain`, `title`, `detail`, `dueAtUtc`, optional `timeLabel`, `actionLabel`, `doneMessage`, `contentHash`, `caps`. |
| `counts` | `total`, `overdue`, `byDomain` — authoritative for the same filter set the items came from. |

`commerce` dockets are generated only for a caller whose role holds
`stats:view:agent`; `logistics` has no writer and is always reported unavailable.

`401` without a token; `403` for a non-member.

#### `POST /api/v1/orgs/{organizationId}/focus/dismissals`

**Auth:** `BoutiqueAccess`, plus a required `Idempotency-Key`.

```json
{ "sourceKey": "<fact id>", "domain": "wardrobe",
  "decision": "signOff", "contentHash": "<echo>", "note": null }
```

**Response `200`:** `{ dismissalId, sourceKey, domain, decision, dismissedAtUtc, counts }`.
The `counts` are the caller's refreshed feed counts, so the client does not have
to trust its own optimistic removal.

`404` when the docket is not in the caller's feed (a docket from another
organization is indistinguishable from one that does not exist); `403` for a
non-member; `503` when the idempotency lease store is unreachable.

The dismissal is keyed to `sourceKey` and bound to the server's own
`contentHash`, so a dismissal of one version of a fact does not suppress a later,
different docket for the same underlying row.

---

### C.11 Tenant customer surface (Phase 7 — Home and the client book)

> **Status: shipped, and now documented in Part B.** The four routes below were specified here as
> planned and landed with the tenant-dashboard slice's T2. They previously appeared only in this
> Part C entry, which is why the read-by-id route could be missing while the write route already
> advertised its URL in a `Location` header. See **B.20 Tenant customer surface** for the shipped
> contract, including the two semantics this entry never stated: the **409 phone conflict** (on
> create as well as update) and the **idempotent soft delete**.

All routes are under the named `BoutiqueCustomerAccess` policy
(`OrganizationScopeRequirement(customers:view)`), which every boutique role
holds, except the two writes, which take `BoutiqueCustomerManage` (`customers:manage`). They are
deliberately **not** in `/internal/customers`.

---

## Part D — Frontend integration notes

### D.1 Order of adoption

| Order | Endpoints | Why first |
| --- | --- | --- |
| 1 | `GET /orgs/{id}/usage` (exists), then `GET /orgs/{id}/blossoms/balance` (Phase 2) | The Blossom widget is the highest-value visible surface |
| 2 | `GET /orgs/{id}/entitlements` + `/usage` (Phase 2) | Replaces all hardcoded plan gating |
| 3 | `GET /orgs/{id}/statistics/billing/burn-rate` (Phase 2) | Drives the upgrade prompt |
| 4 | `GET /orgs/{id}/statistics/api/**` (Phase 5) | The API-consumption dashboard |
| 5 | ~~`GET /orgs/{id}/statistics/agents/**` (Phase 4)~~ — **removed**; the team-only `/admin/statistics/agents/**` subset remains | A boutique reads its usage in Blossoms, not in runs/tokens/cost |
| 6 | `GET /admin/statistics/system/**` (Phase 6) | Internal operations view |

### D.2 Instances where the backend is currently wrong

Three live issues a frontend **will** encounter and should not work around:

| Issue | Symptom | Fix |
| --- | --- | --- |
| Plan limit ignored on lazy ledger creation | `GET /orgs/{id}/usage` returns `monthlyBlossomLimit: 150` for a Bloom/Orchid/Rose org | Phase 2 |
| Plan limit not updated on upgrade | After a plan change, `monthlyBlossomLimit` stays at the old value | Phase 2 |
| Error body inconsistency | `/internal/*` returns `{ error }`, everything else `{ message }` | Frontends never call `/internal/*`; no action needed |

Do **not** patch these by hardcoding a plan-to-Blossom map in the frontend. That
is exactly the drift this plan removes, and it would break on the next plan change.

### D.3 Response-caching guidance

| Endpoint | Cache | Rationale |
| --- | --- | --- |
| `GET .../blossoms/balance` | **never** | Correctness-critical |
| `GET .../usage`, `.../statement` | 30 s | Cheap and safe |
| `GET .../entitlements` | 5 min | Changes only on a plan change |
| `GET .../statistics/**` | 60 s | Rollup-backed |
| `GET /admin/statistics/system/overview` | `stats:system:overview` | **15 s server-side**, in-process (`SystemStatisticsService` caches the composite read; `IMemoryCache` is registered by `BillingModule`/`SystemHealthModule`). A client should therefore use `staleTime: 15_000` for this query. Originally documented as 15 s, written out of the docs as "reversed" at #243, and **restored here in Slice 8 (D7 = A) because the code is authoritative** (`SystemStatisticsService.cs:49-61`). Every other statistics family keeps its own rule; the Blossom balance stays uncached |
| `GET /health/**` | never | Liveness |

### D.4 Versioning and deprecation

- **No endpoint has ever been deprecated.** The only `/api/v1` contract change
  introduced by this plan is **additive**: new response fields and new endpoints.
- Additive changes **do not** bump a version. Clients must ignore unknown fields.
- A breaking change would introduce `/api/v2` as a new literal route group and run
  both groups for at least one release.
- `GET /health` remains an alias of `/health/ready` indefinitely.
- `POST /internal/usage/record` is **not** deprecated; `POST /internal/agent-runs`
  is additive alongside it.

### D.5 Open questions that affect the frontend contract

| # | Question | Frontend impact |
| --- | --- | --- |
| [OQ-7](../backend/assumptions-and-open-questions.md) | 402 vs 403 vs 429 for exceeding a plan quota | Determines which status code triggers the upgrade prompt |
| [OQ-1](../backend/assumptions-and-open-questions.md) | Whether the LKR price book is in scope | Determines whether a pricing page has data behind it |
| [OQ-5](../backend/assumptions-and-open-questions.md) | Whether Blossom exhaustion blocks AI requests | Determines whether the UI needs a hard-stop state |

---

## Appendix P — Planned / not yet implemented

> **Not part of the normative contract.** Every endpoint in this appendix has **no
> route** in the shipped API, so it returns **404**. It is listed here only so the
> intended contract is not lost, and it must not be reported as "documented but
> missing". `docs/api/openapi.yaml` carries the same set in commented
> "Planned / not yet implemented" sections. See
> [§1.3](#13-reconciliation-with-docsopenapi).

### Organization billing statistics

#### `GET /api/v1/orgs/{organizationId:guid}/statistics/billing/burn-rate`

**Auth:** `billing:view`. **Params:** `window` (`7d` \| `14d` \| `30d`).
**Intended `200`:**

```json
{
  "window": "14d",
  "burnRatePerDay": 22.4,
  "availableBlossoms": 617.6,
  "projectedExhaustionAt": "2026-10-19T00:00:00Z",
  "confidence": "medium",
  "sampleDays": 10
}
```

`projectedExhaustionAt` is `null` when `burnRatePerDay` is 0.
**Related statistics:** [S-4](../backend/statistics-catalog.md).

#### `GET /api/v1/orgs/{organizationId:guid}/statistics/customers/active` and `.../staff/seats`

**Auth:** `billing:view`. **Related statistics:** [S-11](../backend/statistics-catalog.md), [S-12](../backend/statistics-catalog.md).

### Admin billing statistics

Base `/api/v1/admin/statistics/billing`. **Auth:** `stats:system` (Aveline team only).

| Endpoint | Params | Intended response |
| --- | --- | --- |
| `GET /profitability` | `from`, `to`, `groupBy` (`planTier` \| `organizationId` \| `provider` \| `model` \| `day`) | Cost-versus-charged series; `dataQuality.costIsEstimated` is `true` while the agent service reports no actual cost |
| `GET /org-usage` | `window` (`7d` \| `30d` \| `month`), `planTier?`, `page`, `pageSize` | Organizations ranked by Blossom consumption |
| `GET /adjustments` | `from`, `to`, `entryType?`, `actorUserId?` | Blossom adjustment activity; the primary abuse-detection signal |
| `GET /plan-changes` | `from`, `to` | Plan change history |
| `GET /downgrades` | `from`, `to` | Blocked downgrade statistics |
