# Authorization Model

## Role claims

Clerk's `jwt-aveline-v1` template emits two role claims. The API promotes both
to standard ASP.NET role claims and evaluates named permissions server-side.

| Claim | Scope | Canonical values |
| --- | --- | --- |
| `user_role` | Aveline team | `staff`, `customer_relations`, `moderator`, `admin`, `owner` |
| `org_role` | Current boutique membership | `org:boutique_staff`, `org:boutique_manager`, `org:boutique_supervisor`, `org:boutique_owner` |

Team roles and boutique roles are separate namespaces. A team role does not
grant access to another boutique's resources; tenant-bound enforcement is
handled by the canonical organization-membership authorization described below.

## Enforcement model

Server-side enforcement sits on top of the role claims:

- **Fallback policy.** `FallbackPolicy = DefaultPolicy` (authentication), so any
  new endpoint requires a valid bearer token unless it is deliberately marked
  anonymous (currently only the dev OpenAPI document).
- **Account-state gate.** `OnboardingMiddleware` rejects `Suspended` accounts with
  `403 account-suspended` before endpoint execution, and limits
  `OnboardingPending` accounts to profile/organization/invitation endpoints. The
  decision uses the locally synchronized read model, not the JWT alone.
- **Organization scope.** Endpoints that are tenant-bound are declared with the
  `BoutiqueAccess`-style policy. `OrganizationScopeAuthorizationHandler` resolves
  the caller from the `sub` claim, the target organization from the
  `organizationId` route value, and requires an `Active` row in
  `OrganizationMembership` whose boutique role grants the required permission.
  A still-valid JWT carrying stale org claims cannot cross organizations.
- **Membership-change invalidation.** Creating an organization or accepting an
  invitation invalidates the affected user's cached authorization snapshot so the
  next request re-syncs from the canonical tables instead of a 24-hour-old cache.
- **Membership management.** Boutique owners manage members via
  `POST /orgs/{organizationId}/members/{userId}/suspend|activate` and
  `DELETE /orgs/{organizationId}/members/{userId}`, guarded by the
  `settings:manage` org-scoped policy. Suspending or removing a membership
  invalidates the member's cached state and immediately revokes org-scoped
  access; the organization owner's own membership is protected server-side.
- **`/auth/claims`.** Returns the raw Clerk claims plus the authoritative
  `AccountState`, `UserRole`, and `OrganizationRole` resolved by the middleware
  read model, for debugging without trusting client-side checks.
- **Operational endpoints.** `/health`, `/health/live`, and `/health/ready` are
  anonymous so an orchestrator can probe them without credentials. `/metrics`
  (Prometheus) is guarded by the `Metrics` policy and accepts either the internal
  service token (`X-Internal-Token`) or, when configured, a
  `Authorization: Bearer <Metrics:ScrapeToken>` scraper token.

## Permission matrix

> Generated from `Aveline.Api/Authorization/Permissions.cs` (the source of truth).
> `PermissionsCatalogTests` enforces that every permission has a registered policy and
> at least one role grant.

| Role | Granted permissions |
| --- | --- |
| `staff` | `catalog:view`, `conversations:view` |
| `customer_relations` | `catalog:view`, `customers:view`, `conversations:view` |
| `moderator` | `catalog:view`, `customers:view`, `approvals:approve`, `conversations:view`, `billing:view`, `stats:view`, `stats:view:agent`, `admin:orgs:read`, `analytics:business:read`, `revenue:read` |
| `admin` | Every permission **except** `pricing:backdate` and `revenue:refund` |
| `owner` | Every permission |
| `org:boutique_staff` | `catalog:view`, `customers:view`, `conversations:view`, `billing:view:self`, `approvals:approve` |
| `org:boutique_manager` | `catalog:view`, `customers:view`, `catalog:manage`, `customers:manage`, `team:manage`, `orders:manage`, `reports:view`, `conversations:view`, `billing:view`, `billing:view:self`, `pricing:view`, `stats:view` |
| `org:boutique_supervisor` | `catalog:view`, `customers:view`, `catalog:manage`, `customers:manage`, `team:manage`, `orders:manage`, `approvals:approve`, `reports:view`, `conversations:view`, `billing:view:self`, `stats:view` |
| `org:boutique_owner` | `catalog:view`, `customers:view`, `catalog:manage`, `customers:manage`, `team:manage`, `orders:manage`, `approvals:approve`, `payments:refund`, `reports:view`, `settings:manage`, `conversations:view`, `billing:view`, `billing:view:self`, `billing:manage`, `pricing:view`, `apikeys:view`, `apikeys:manage`, `stats:view` |

Boutique roles deliberately never hold money-shaped permissions. `pricing:manage`,
`pricing:backdate`, `billing:adjust`, `stats:system`, `admin:*` and `audit:view` are
granted only to Aveline team roles, `pricing:backdate` and `revenue:refund` are
owner-only, and no boutique role holds `revenue:read`/`revenue:manage`/`revenue:refund`
at all. **Reading the shop's own takings is not a money-shaped permission**: it is the
existing `reports:view`, which the grant map already gave manager, supervisor and owner
before any route enforced it.

`stats:view:agent` is team-only for a different reason: the agentic statistics it names
(runs, tokens, provider cost) are agent internals, and a boutique reads its usage in
Blossoms. No boutique role holds it, and the org-scoped `/statistics/agents/**` routes are
not mounted at all.

The current permission catalog is **31** entries: `catalog:view`, `customers:view`,
`catalog:manage`, `approvals:approve`, `payments:refund`, `reports:view`,
`settings:manage`, `conversations:view`, `customers:manage`, `team:manage`,
`orders:manage`, `billing:view`, `billing:view:self`, `billing:manage`, `billing:adjust`,
`pricing:view`, `pricing:manage`, `pricing:backdate`, `apikeys:view`, `apikeys:manage`,
`stats:view`, `stats:view:agent`, `stats:system`, `analytics:business:read`,
`admin:users:read`, `admin:users:manage`, `admin:orgs:read`, `audit:view`,
`revenue:read`, `revenue:manage`, and `revenue:refund`.

## Org-scoped policies for the tenant dashboard (T0a)

Every tenant route names an **org-scoped** policy rather than a bare permission string:
`OrganizationScopeAuthorizationHandler` resolves the caller from the `sub` claim, the target
organization from the `organizationId` route value, and requires an `Active` membership whose
boutique role grants the policy's permission. A bare permission policy would authorise on the
JWT's possibly-stale role claims and never consult the route value, which defeats tenant
isolation.

The tenant-dashboard slice adds the following, and names each one because the defect it closes
is precisely "nobody wrote down which permission a route meant":

| Policy | Requirement | Used by |
| --- | --- | --- |
| `BoutiqueMember` | `Active` membership, **no permission** | The catalogue's operational writes (scan, label, photograph, compose) and the order reads. Exists so a route that means "an active member" stops borrowing `catalog:view` as a proxy. |
| `BoutiqueCatalogManage` | `catalog:manage` | The four catalogue writes: create, edit, publish/unpublish, delete. |
| `BoutiqueCustomerManage` | `customers:manage` | Editing and soft-deleting a client record. |
| `BoutiqueTeamManage` | `team:manage` | The eight staff routes: invitations ×3 and members ×5. |
| `BoutiqueOrderManage` | `orders:manage` | Order update, status change, cancel, recalculate, and business-rule writes. |
| `BoutiqueReportsView` | `reports:view` | The shop's KPIs, revenue series and income ledger. |

**Why `team:manage` is separate from `settings:manage`.** `BoutiqueMembershipManage` is
`settings:manage`, and `settings:manage` also reaches Integrations, where `IntegrationCredential`
holds the WhatsApp and payment-gateway secrets (ADR-011). Granting a manager `settings:manage` to
answer "may a manager manage staff" would have handed them the gateway credentials as well. The
eight team routes therefore move to `BoutiqueTeamManage`; the two organisation settings routes
(`PATCH /orgs/{organizationId}` and `GET /orgs/{organizationId}/settings`) stay on
`BoutiqueMembershipManage`.

**Why staff now hold `approvals:approve`.** A staff member may approve a customer order. The
approval controller puts one policy on four verbs, so the grant alone would also let them
`reject` (which **cancels** the order) and `revise` (which **rewrites** discount, total and
margin). The verb split — `/approve` and `/decision` at `approvals:approve`, `/reject` and
`/revise` at `BoutiqueOrderManage` — is what makes the grant mean only what it says.

### Two blocking isolation defects closed (T1)

**F-1 — `OrdersController` and `BusinessRulesController` had no authorization at all.** Neither
controller carried an `[Authorize]` attribute, and both used the route token `{orgId:guid}`. The
organization-scope handler reads `RouteValues["organizationId"]` and nothing else, so even adding a
policy would not have bound. The fallback policy is the default one, which requires authentication
only. The combined effect was that **any authenticated caller — including another boutique's staff —
could read and write any organization's orders and business rules**. Both controllers now use
`{organizationId:guid}` and name a policy; the split is:

| Route class | Policy |
| --- | --- |
| Order reads (`GET`) and `POST /orders` | `BoutiqueMember` — any active member |
| Order `PUT`, `PATCH /status`, `/cancel`, `/recalculate` | `BoutiqueOrderManage` (`orders:manage`) |
| Every business-rule route, including the reads and `/evaluate` | `BoutiqueOrderManage` |

`BoutiqueAccess` (= `catalog:view`) is deliberately **not** used: it is held by every boutique role,
so it would have let staff cancel orders and rewrite the rules that gate discounts.

**F-2 — every catalog write inherited `catalog:view`.** The group carried one policy and all
fourteen write routes inherited it, while `catalog:manage` was enforced on **zero** routes in the
codebase. The fix is a per-route attribute on top of the unchanged group policy (house style, as
`ConversationEndpoints` composes them): **four managed** routes (create, edit, publish, delete) take
`BoutiqueCatalogManage`, and the **ten operational** ones (search, QR label, scan, vision analysis,
VIP matches, outfit compose, sourcing, image upload) take the permission-free `BoutiqueMember`. A
blanket `catalog:manage` was rejected because it would have taken the camera, the label printer and
the AI analysis away from `org:boutique_staff`.

Two further corrections rode along: `GET …/usage` moved from `BoutiqueAccess` to
`BoutiqueBillingSelfView` (a usage read is a billing read; every org role holds
`billing:view:self`, so access is unchanged), and the approval actor now resolves through
`IUserRepository.GetByClerkIdAsync` instead of `Guid.TryParse` on the Clerk `sub` — a subject such as
`user_2abc…` never parsed, so every decision recorded a null actor.



## Team-only role policies carry their permission requirement (A9)

`StatsSystem`, `AuditView` and `PricingAdminRead` used to be pure role guards
(`RequireRole(owner, admin)`), while the `stats:system`, `audit:view` and
`pricing:view` permission policies registered for every name in the catalogue were
referenced by no route. The catalogue and the wire therefore disagreed: the three
permission strings looked like the authority for those surfaces, but a route only
checked the role.

Each of the three named policies now carries its `PermissionRequirement` **alongside**
the role requirement:

| Policy | Requirement |
| --- | --- |
| `StatsSystem` | role `owner`/`admin` **and** `stats:system` |
| `AuditView` | role `owner`/`admin` **and** `audit:view` |
| `PricingAdminRead` | role `owner`/`admin` **and** `pricing:view` |

The addition is **additive and inert for the current role set**: both `admin` and
`owner` already hold all three permissions (`Permissions.cs`; `admin` holds every
permission except `pricing:backdate`), so every principal the role guard admitted still
passes. `moderator` and every boutique role hold none of the three and were already
refused by the role guard, so they are still refused. The effect is that the permission
catalogue now describes what the routes enforce, which is what lets the console's
`Gate` role overlay be a belt-and-braces check rather than the only correct check.

## `analytics:business:read` (Business KPIs)

`analytics:business:read` guards the six administrator business-KPI reads under
`/api/v1/admin/statistics/business/*` (growth, active users, plan mix, subscription trend,
usage, and the organization ranking). It is a **separate permission from `stats:system`**
on purpose: `stats:system` is registered as a `RequireRole(owner, admin)` policy and is
about system health, while growth data is a different subject with a wider legitimate
audience. A `moderator` already holds `admin:orgs:read` and `billing:view`, so reading which
boutiques exist and what they consume is inside their remit without granting them
system-health access.

| Role | `analytics:business:read` |
| --- | --- |
| `moderator` | granted |
| `admin` | granted (every permission except `pricing:backdate`) |
| `owner` | granted (every permission) |
| `staff`, `customer_relations`, every `org:boutique_*` | denied |

The routes are **bearer-only**: the group does not call `AllowBearerOrApiKey`, so an API key
can never reach them, matching the other team-only statistics families. Two of the six reads
(`usage?organizationId=…` and the organization ranking) additionally require
`admin:orgs:read`, because they enumerate tenant identity; that check is performed in the
handler rather than in the policy.

Granting the permission to `moderator` is **inert today** — `AdminRouteGuard` admits only
`owner` and `admin` to the console — and exists so the catalog is semantically correct rather
than because it changes live behaviour.

## Compatibility

The legacy values `associate`, `manager`, `org:associate`, `org:manager`,
`org:owner`, `org:admin`, and `org:member` receive no authorization grants.
Before deployment, backfill the persisted display values and update Clerk public
metadata/membership roles; then revoke or refresh every existing Clerk session.
This fail-closed rollout prevents a stale legacy role from retaining access.
