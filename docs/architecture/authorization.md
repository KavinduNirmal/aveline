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
| `moderator` | `catalog:view`, `customers:view`, `approvals:approve`, `conversations:view`, `billing:view`, `stats:view`, `stats:view:agent`, `admin:orgs:read`, `analytics:business:read` |
| `admin` | Every permission **except** `pricing:backdate` |
| `owner` | Every permission |
| `org:boutique_staff` | `catalog:view`, `customers:view`, `conversations:view` |
| `org:boutique_manager` | `catalog:view`, `customers:view`, `catalog:manage`, `reports:view`, `conversations:view`, `billing:view`, `pricing:view`, `stats:view` |
| `org:boutique_supervisor` | `catalog:view`, `customers:view`, `catalog:manage`, `approvals:approve`, `reports:view`, `conversations:view`, `stats:view` |
| `org:boutique_owner` | `catalog:view`, `customers:view`, `catalog:manage`, `approvals:approve`, `payments:refund`, `reports:view`, `settings:manage`, `conversations:view`, `billing:view`, `billing:manage`, `pricing:view`, `apikeys:view`, `apikeys:manage`, `stats:view`, `stats:view:agent` |

Boutique roles deliberately never hold money-shaped permissions. `pricing:manage`,
`pricing:backdate`, `billing:adjust`, `stats:system`, `admin:*` and `audit:view` are
granted only to Aveline team roles, and `pricing:backdate` is owner-only.

The current permission catalog is `catalog:view`, `customers:view`, `catalog:manage`,
`approvals:approve`, `payments:refund`, `reports:view`, `settings:manage`,
`conversations:view`, `billing:view`, `billing:manage`, `billing:adjust`,
`pricing:view`, `pricing:manage`, `pricing:backdate`, `apikeys:view`, `apikeys:manage`,
`stats:view`, `stats:view:agent`, `stats:system`, `analytics:business:read`,
`admin:users:read`, `admin:users:manage`, `admin:orgs:read`, and `audit:view`.

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
