# ADR-008: JWT Strategy — Clerk Custom Template `jwt-aveline-v1`

## Status
Accepted

## Context
Flutter (associates) and React (owners/managers) must authenticate against the
ASP.NET Core API with the same identity. The API needs role claims
(`user_role`, `org_role`) to authorize endpoints. Clerk is the IdP (ADR-007).

## Options Considered
1. **Clerk custom JWT template (`jwt-aveline-v1`)** (chosen) — Clerk mints tokens
   with Aveline-specific claims from user/org metadata.
2. **Clerk default session token** — Pros: zero config. Cons: lacks `user_role`/`org_role`;
   the API would need to call the Clerk Backend API per request to resolve roles.
3. **Self-issued JWTs** — Pros: full control. Cons: re-implements an IdP and session management.

## Decision
Use a Clerk **custom JWT template** (`jwt-aveline-v1`) minting:
- `user_role` = `{{user.public_metadata.role}}` (team role)
- `org_role` = `{{org.role}}` (per-store role: `org:owner`, `org:manager`, `org:admin`, …)
- `org_id` / `org_slug` = current organization context

The API validates tokens with JwtBearer against the Clerk **JWKS** endpoint:
issuer, lifetime, and signing key are enforced; **audience is not enforced**
(Clerk tokens lack a stable `aud`; the instance `kid` already scopes tokens — see
security review SEC-M1). `user_role`/`org_role` are promoted to `ClaimTypes.Role`
at authentication time so `[Authorize(Roles=…)]` and permission policies work.

## Consequences
- Role changes flow from Clerk metadata without API redeploys.
- Template changes require editing the template in the Clerk Dashboard.
- Clients request the template token via `getToken({ template: 'jwt-aveline-v1' })`.
