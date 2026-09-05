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
implemented with the organization membership work tracked in issue #52.

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
