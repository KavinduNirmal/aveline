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
| `BoutiqueMembershipManage` | same, grants `settings:manage` |
| `BoutiqueConversationAccess` | same, grants `conversations:view` |
| `BoutiqueCustomerAccess` | same, grants `customers:view` (the tenant customer surface: the client book, a client profile and Home's client highlights) |
| `InternalServicePolicy` | `X-Internal-Token` + role `InternalService` |
| *one per permission* | policy name **is** the permission string, e.g. `catalog:view` |

**Layer 2 — organization scope.** For any route containing
`{organizationId:guid}`, `OrganizationScopeAuthorizationHandler` resolves the
caller from `sub`, loads the `OrganizationMembership`, requires
`Status = Active`, and checks that the membership's `BoutiqueRole` grants the
required permission. A still-valid JWT carrying stale org claims **cannot** cross
organizations (`Authorization/OrganizationScopeAuthorizationHandler.cs:38-74`).

**Permission catalog (current, 8):**
`catalog:view`, `customers:view`, `catalog:manage`, `approvals:approve`,
`payments:refund`, `reports:view`, `settings:manage`, `conversations:view`.

**Role → permission grants (current, from `Authorization/Permissions.cs:29-43`):**

| Role | Permissions |
| --- | --- |
| `staff` | `catalog:view`, `conversations:view` |
| `customer_relations` | `catalog:view`, `customers:view`, `conversations:view` |
| `moderator` | `catalog:view`, `customers:view`, `approvals:approve`, `conversations:view` |
| `admin`, `owner` | all |
| `org:boutique_staff` | `catalog:view`, `customers:view`, `conversations:view` |
| `org:boutique_manager` | `catalog:view`, `customers:view`, `catalog:manage`, `reports:view`, `conversations:view` |
| `org:boutique_supervisor` | manager grants + `approvals:approve` |
| `org:boutique_owner` | all |

> **Documentation drift warning.** `docs/architecture/authorization.md:51-62`
> omits `conversations:view` from two rows and from its catalog list. **The code
> above is authoritative.** Phase 0 regenerates that document.

**Permissions added by this plan (14, Phase 0–3):** `billing:view`,
`billing:manage`, `billing:adjust`, `pricing:view`, `pricing:manage`,
`pricing:backdate`, `apikeys:view`, `apikeys:manage`, `stats:view`,
`stats:view:agent`, `stats:system`, `admin:users:read`, `admin:users:manage`,
`admin:orgs:read`, `audit:view`.

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

Grouped sections also use a relative shorthand: `#### GET /runs` inside the
**Agent statistics** section means
`GET /api/v1/orgs/{organizationId:guid}/statistics/agents/runs`. Each group states
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
| `GET` | `/api/v1/auth/claims` | authenticated | Raw Clerk claims plus the resolved `AccountState`, `UserRole`, `OrganizationRole`. Source: `Endpoints/AuthEndpoints.cs:16`. |

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
| `POST` | `/api/v1/orgs/{organizationId:guid}/members/{userId:guid}/suspend` | `BoutiqueMembershipManage` | — | `{ organizationId, userId, boutiqueRole, status }` |
| `POST` | `/api/v1/orgs/{organizationId:guid}/members/{userId:guid}/activate` | `BoutiqueMembershipManage` | — | same |
| `DELETE` | `/api/v1/orgs/{organizationId:guid}/members/{userId:guid}` | `BoutiqueMembershipManage` | — | `204` |

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
| `POST` | `/api/v1/orgs/{organizationId:guid}/invitations` | `BoutiqueMembershipManage` | `{ boutiqueRole, recipientEmail? }` | `CreateInvitationResponse { invitationId, code, link, mobileLink, boutiqueRole, recipientEmail, expiresAt }` |
| `GET` | `/api/v1/orgs/{organizationId:guid}/invitations` | `BoutiqueMembershipManage` | — | `PendingInvitationDto[]` |
| `POST` | `/api/v1/orgs/{organizationId:guid}/invitations/{invitationId:guid}/revoke` | `BoutiqueMembershipManage` | — | `204` |
| `POST` | `/api/v1/invitations/accept` | authenticated + IP rate limit | `{ code }` | `{ organizationId, userId, boutiqueRole, clerkOrgId, accountState }` |

**Invitable roles are exactly:** `org:boutique_supervisor`, `org:boutique_manager`,
`org:boutique_staff`. The owner role is **not** invitable.
**Errors:** `400` not-invitable role / not acceptable / recipient mismatch;
`404` invitation or user not found; `409` membership already exists;
`429` too many attempts; `503` invitation code store unavailable.
**Rate limit:** `POST /invitations/accept`, per client IP, default 10/min.
**Source:** `Endpoints/OrganizationEndpoints.cs:36-144,349-420`.

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

### B.14 Authorization policy demo endpoints (fixtures)

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

Permissions: the read routes are enforced by the team-only `PricingAdminRead` policy
(Admin or Owner, #237); writes need `pricing:manage`, and a past effective date
additionally needs `pricing:backdate`. `pricing:view` remains in the catalog but no
longer gates a route. **Never available to boutique roles.**

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

**Purpose:** full statement of account with a reconciliation check.
**Params:** `from`, `to`, `page`, `pageSize`, `entryType?` (`all` \| `entitlement` \| `consumption`).
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
      "reason": "Agent workflow",
      "sourceKind": null,
      "sourceRef": "wf_01J8...",
      "expiresAt": null,
      "createdByUserId": null
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
  "generatedAt": "2026-09-11T09:30:00Z"
}
```

**Errors:** `400` (bad window), `401`, `403`.
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
| `GET` | `/api/v1/admin/orgs/{organizationId:guid}/blossoms/statement` | query: paging | same shape as the org statement |

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
> was previously write-only, so `audit:view` and `AuditViewPolicy` were dead.


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
| `GET` | `/api/v1/orgs/{organizationId:guid}/members` | `settings:manage` | query: `page`, `pageSize`, `status?`, `role?`, `q?` | `MemberPage` |
| `PATCH` | `/api/v1/orgs/{organizationId:guid}/members/{userId:guid}` | `settings:manage` | `{ boutiqueRole }` | `{ organizationId, userId, boutiqueRole, status }` |
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

Base: `/api/v1/admin/statistics`. **Auth:** `stats:system` (Aveline team only).

| Endpoint | Params | Returns |
| --- | --- | --- |
| `GET /system/overview` | — | Composite: readiness, uptime, version, top alerts, throughput, error rate, queue depths, running agent runs |
| `GET /system/metrics` | `metric`, `from`, `to`, `windowSize?` | Time series for one named metric |
| `GET /system/queues` | — | Queue depths, including running agent runs |
| `GET /system/errors` | `from`, `to`, `groupBy` | Error rate and unhandled exception count |
| `GET /system/throughput` | `from`, `to`, `groupBy` | RPS, agent runs/min, Blossoms/hour |
| `GET /system/eventbus` | **none** (`from`/`to` are accepted but ignored) | Published, delivered, failed, publish latency — an instantaneous counter snapshot |
| `GET /system/alerts` | `status?`, `severity?`, `ruleId?`, `page`, `pageSize` | `SystemAlertPage` |
| `POST /system/alerts/{alertId:guid}/acknowledge` | body `{ "note": string? }` | Updated alert |

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

**Response `200`** `text/plain; version=0.0.4` (Prometheus exposition format).
`401` without credentials. Metric names use the `aveline.` prefix.

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

All three routes are under the named `BoutiqueCustomerAccess` policy
(`OrganizationScopeRequirement(customers:view)`), which every boutique role
holds. They are deliberately **not** in `/internal/customers`.

#### `GET /api/v1/orgs/{organizationId}/customers`

The client book, in one call: the alphabet index has to reach every letter, so
`pageSize` defaults to 200. `level` is nullable — the server stores no grade
until the shop sets one.

#### `GET /api/v1/orgs/{organizationId}/customers/highlights`

Home's `Direct client link` row, the `See all` sheet and the status ticker. Each
item carries `customerId`, `name`, nullable `level`, `activity` (generated from a
real `Customer_Interactions` row) and `lastActivityAtUtc`. There is **no**
`hasNewActivity`: no read marker exists in the schema, and a dot that can never
clear is worse than no dot.

#### `POST /api/v1/orgs/{organizationId}/customers`

Walk-in creation. `{ "fullName": "...", "source": "counter_walkin" }` plus a
required `Idempotency-Key`. A name is enough; `phoneNumber` is optional and its
absence is reported honestly.

`201` with `{ customerId, fullName, level, status, consentStatus, createdAtUtc,
duplicateOfCustomerId }`. A duplicate answers `200` with
`duplicateOfCustomerId` set rather than creating a second client.

#### `POST /api/v1/orgs/{organizationId}/customers/{customerId}/interactions`

The write path behind Home's `Log a visit`. `{ occurredAtUtc, channel, direction,
note, purchaseTotal }` plus a required `Idempotency-Key`.

Every call records an interaction; the customer's counters move only for an
**inbound in-person** interaction. `201` with `{ visitId, customerId,
occurredAtUtc, channel, countedAsVisit, visitCountAfter, lastVisitAtUtcAfter,
tierAfter, blossomsCharged }`. `blossomsCharged` is **always `0`** — a visit is
not billable. `404` when the client is not in this boutique.

---

## Part D — Frontend integration notes

### D.1 Order of adoption

| Order | Endpoints | Why first |
| --- | --- | --- |
| 1 | `GET /orgs/{id}/usage` (exists), then `GET /orgs/{id}/blossoms/balance` (Phase 2) | The Blossom widget is the highest-value visible surface |
| 2 | `GET /orgs/{id}/entitlements` + `/usage` (Phase 2) | Replaces all hardcoded plan gating |
| 3 | `GET /orgs/{id}/statistics/billing/burn-rate` (Phase 2) | Drives the upgrade prompt |
| 4 | `GET /orgs/{id}/statistics/api/**` (Phase 5) | The API-consumption dashboard |
| 5 | `GET /orgs/{id}/statistics/agents/**` (Phase 4) | Gate behind `stats:view:agent` and only show to owners |
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
| `GET /admin/statistics/system/overview` | client-side only | **Not cached server-side** despite the earlier 15 s claim (M-10); the composite read is assembled live on every call, so a client may cache it briefly |
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
