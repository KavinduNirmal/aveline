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

## Permission matrix

| Role | Granted permissions |
| --- | --- |
| `staff` | `catalog:view` |
| `customer_relations` | `catalog:view`, `customers:view` |
| `moderator` | `catalog:view`, `customers:view`, `approvals:approve` |
| `admin`, `owner` | All current permissions |
| `org:boutique_staff` | `catalog:view`, `customers:view` |
| `org:boutique_manager` | `catalog:view`, `customers:view`, `catalog:manage`, `reports:view` |
| `org:boutique_supervisor` | Boutique-manager permissions plus `approvals:approve` |
| `org:boutique_owner` | All current permissions |

The current permission catalog is `catalog:view`, `customers:view`,
`catalog:manage`, `approvals:approve`, `payments:refund`, `reports:view`, and
`settings:manage`.

## Compatibility

The legacy values `associate`, `manager`, `org:associate`, `org:manager`,
`org:owner`, `org:admin`, and `org:member` receive no authorization grants.
Before deployment, backfill the persisted display values and update Clerk public
metadata/membership roles; then revoke or refresh every existing Clerk session.
This fail-closed rollout prevents a stale legacy role from retaining access.
