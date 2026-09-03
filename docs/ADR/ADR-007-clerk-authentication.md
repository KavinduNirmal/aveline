# ADR-007: Clerk Authentication & JWT Validation Strategy

## Status

Accepted

## Context

Aveline needs authentication and authorization across four surfaces: a Flutter mobile app (associates), a React/Vite web dashboard (owners/managers), an ASP.NET Core Web API, and an internal Python/LangGraph agent service. The SE3090 assignment requires:

- JWT-based authentication using **Clerk**.
- Role-based access control distinguishing Aveline team roles from per-store owner/staff roles.
- A mandatory cross-platform workflow where clients call the API, and the API calls the internal agent service.
- The agent service must **not** be directly callable by clients — it needs separate service-to-service authentication.

Two decisions are captured here: **(1)** choosing Clerk as the identity provider, and **(2)** the JWT validation strategy in the .NET backend.

## Options Considered

### 1. Identity provider: Clerk vs alternatives

**Clerk**
- Pros: Already mandated by the assignment; unified JWT across web/mobile; managed sessions, social login, organizations; JWT templates allow custom claims (`user_role`, `org_role`); free tier sufficient for a demo.
- Cons: Third-party dependency; a few dashboard-driven configs (origins, templates).

**Self-hosted (ASP.NET Core Identity / Keycloak)**
- Pros: Full control; no third-party dependency.
- Cons: Requires building/managing user stores, session flows, MFA, and admin UI; does not satisfy the assignment's Clerk requirement; significant extra scope for a 3-person demo.

**Alternative BaaS (Auth0 / Firebase / Supabase Auth)**
- Pros: Similar feature set.
- Cons: Not the mandated provider; would require reworking client SDKs and docs; no advantage for this project.

**Decision 1:** Use **Clerk** as the identity provider (assignment requirement + free tier + JWT templates).

### 2. .NET JWT validation: JwtBearer + JWKS vs Clerk .NET SDK vs manual verification

**JwtBearer + Clerk JWKS endpoint**
- Pros: Minimal dependency (`Microsoft.AspNetCore.Authentication.JwtBearer`); standard ASP.NET Core auth pipeline (`HttpContext.User`, `[Authorize]`); validates signature via Clerk's JWKS, plus issuer/audience/lifetime; easy to unit test with crafted JWTs; maps custom claims into `ClaimTypes`.
- Cons: We wire claim-to-role mapping ourselves (small, explicit code).

**Clerk .NET backend SDK (e.g., `Clerk.BackendAPI`)**
- Pros: Clerk-native helpers for token verification and Backend API calls.
- Cons: Heavier dependency; less direct integration with ASP.NET Core's `[Authorize]` / policies; overkill for pure JWT validation in a demo.

**Manual JWT verification (custom middleware)**
- Pros: No SDK at all.
- Cons: Reimplements JwtBearer; more security-sensitive code to own and test.

**Decision 2:** Use **`Microsoft.AspNetCore.Authentication.JwtBearer`** configured against the Clerk **JWKS endpoint** (`https://inspired-warthog-8208.clerk.accounts.dev/.well-known/jwks.json`), with the `jwt-aveline-v1` template's claims (`user_role`, `org_role`, `org_id`, `org_slug`) mapped into the principal for authorization policies.

### 3. Service-to-service auth (.NET → Python)

**Shared-secret header (`X-Internal-Token`)**
- Pros: Simple, adequate for a demo; easy to test; no extra crypto.
- Cons: Not ideal at scale (rotating/long-lived secret) — acceptable for the demo phase.

**Internal HMAC-signed JWT (short-lived)**
- Pros: Time-limited, no long-lived secret exposure.
- Cons: More moving parts for the demo.

**Decision 3 (deferred to issue #18):** Shared-secret `X-Internal-Token` header for the demo, upgraded to a short-lived HMAC JWT if needed before production.

## Decision

Adopt **Clerk** as the identity provider, with the **`jwt-aveline-v1`** JWT template exposing:

| Claim | Source |
|---|---|
| `user_role` | `{{user.public_metadata.role}}` (Aveline team role) |
| `org_role` | `{{org.role}}` (per-store owner/staff role) |
| `org_id`, `org_slug` | current organization |

Backend validation uses **`JwtBearer` against Clerk's JWKS**, mapping claims into `HttpContext.User`, and enforcing access via role-based **authorization policies**. The Python agent service is protected separately by an **internal service token** and is never exposed to clients.

## Consequences

**Positive:**

- Standard ASP.NET Core auth surface (`AddAuthentication`/`AddAuthorization`/`[Authorize]`), easy to test.
- Role claims are centrally defined in the Clerk template and consumed uniformly by API, Flutter, and React.
- Clients never talk to the agent service; internal auth is isolated.

**Trade-offs:**

- A Dashboard/CLI configuration change is required to adjust claims (JWT template) or origins.
- Claims-to-role mapping is our responsibility in .NET (documented in the auth flow doc).
- Requires `CLERK_SECRET_KEY`/`CLERK_PUBLISHABLE_KEY`/JWKS URL in configuration, never in source control.

**Follow-on work:** Issues #14–#22 (backend validation, policies, endpoints, CORS, tests), #8–#13 (Flutter/React SDKs + interceptors), #18–#19 (service-to-service auth + Python middleware).
