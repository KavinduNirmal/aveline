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
- `org_role` = `{{org.role}}` (per-boutique role: `org:boutique_staff`,
  `org:boutique_manager`, `org:boutique_supervisor`, or `org:boutique_owner`)
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

### Audience validation (added 2026-10-03)

Audience (`aud`) is **not** enforced by default. Clerk session tokens did not carry a stable `aud`,
and the instance signing key (`kid` on the JWKS) already binds a token to this Clerk instance, so
`ValidateAudience=false` was chosen and the security review (SEC-M1) accepted it.

This is a **latent** gap rather than a live one: `iss` is pinned to our instance and
`ValidateIssuerSigningKey` requires that instance's JWKS, so only Clerk can mint a token bound to
`https://clerk.aveline.gravora.dev`. The moment a **second application is added to the same Clerk
instance**, however, tokens minted for it pass every check here, because nothing else distinguishes
the intended audience.

**Revisit trigger:** adding any second application, environment, or tenant to this Clerk instance.
Enforcement is already wired and opt-in — set `Clerk:Audience` (`Clerk__Audience`) and emit a
matching `aud` claim from the `jwt-aveline-v1` template. `BuildTokenValidationParameters` then sets
`ValidateAudience=true` with that value, while leaving the key unset preserves the current behaviour
so no deployment breaks on this change. Security assessment finding F-2.3.

