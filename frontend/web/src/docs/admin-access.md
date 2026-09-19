# Admin Access & Tenant Governance

Aveline provides multi-tenant administration tools to oversee boutique accounts, inspect system
diagnostics, and maintain compliance standards.

> **Scope, corrected.** This page is the in-app documentation for the administrator console. An
> earlier revision of it promised three capabilities the platform does not have — support
> impersonation, enforced multi-factor authentication and idle session expiry. They are called out
> explicitly below as **not implemented**, so nobody plans around them. The console is the Aveline
> team's internal tool; boutique tenants have their own dashboard at `app/b/{slug}`.

---

## Requesting Administrator Privileges

Administrator access confers cross-boutique diagnostic capabilities and is governed by strict review:

1. **Submit Admin Registration:** Prospective administrators submit credentials through the dedicated
   admin verification portal (`/sign-up/admin`).
2. **Review & Verification:** An existing `owner` or `admin` reviews the request in the console's
   Access Requests queue (`/admin/{userId}/requests`) and approves or rejects it.
3. **Activation & Auditing:** Approval writes the `admin` role to Clerk public metadata
   (`ClerkAdminClient`). The new administrator then reaches the console, and every privileged action
   is recorded in the audit log.

A request cannot be approved by its own author: the console keys self-approval on the caller's Clerk
subject (`sub`) against the request's `clerkUserId`, and `AdminApprovalService` enforces the same
rule server-side.

---

## Who may open the console

The console admits exactly the **`owner`** and **`admin`** roles. Everyone else is refused at the
door with a stated reason. In particular:

- `moderator` holds no administrator surface of its own (its only admin-scoped permission is
  `admin:orgs:read`), so it is refused rather than shown sections that could only return `403`.
- `org:boutique_*` roles are tenant roles and never reach `/admin/*`.

Within the console, each section declares a **gate**: either a permission the caller must hold, or a
role policy the server enforces. A section the caller cannot open is not rendered at all.

---

## Privileged Operations

Administrators have access to:

- **Tenant Health & Quotas:** Blossom consumption, plan entitlements and overrides, and API request
  statistics.
- **Access Requests:** Approving or rejecting administrator access requests.
- **Audit Trails:** An append-only `AuditLogEntries` table recording role elevations, authorization
  grants, ledger operations and account-state transitions.
- **Ledger Operations:** Credit, debit and revoke, each idempotency-keyed so a retry cannot
  double-apply.
- **Pricing:** Price books, temporal rule windows, and recompute (which requires the
  `pricing:backdate` grant — the `admin` role does not hold it).

---

## Security Policies

Enforced today:

- **Server-side authorization is authoritative.** The console's permission mirror is for presentation
  and navigation only; every route is authorized by the API. Role-guarded surfaces
  (`audit:view`, `stats:system`, `pricing:view`) carry both their role requirement and their
  permission requirement.
- **No fabricated identity or data.** A signed-out caller reaches nothing: the console issues no
  admin requests and shows no sections. A failed API call renders its failure rather than a
  substitute system.
- **Audit logging.** Privileged operations are recorded in Postgres with actor, entity and reason.

**Not implemented — do not rely on these:**

- **Support impersonation.** There is no impersonation endpoint, token or UI. An administrator cannot
  act as a boutique user.
- **Enforced multi-factor authentication.** MFA is a Clerk account setting; this application neither
  requires nor verifies it.
- **Idle session expiry.** The console has no idle timeout of its own; session lifetime is whatever
  Clerk issues.
- **Cryptographic append-only logs.** The audit trail is an ordinary database table with an
  application-level append-only convention, not a cryptographic ledger.
