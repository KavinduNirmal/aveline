# Authentication Security Review — Aveline

> **Issue:** #28 · **Date:** 2026-09-01 · **Scope:** Clerk JWT validation, role
> authorization, internal service-to-service auth, and the auth API surface
> (`Aveline.Api` + `agnet-service`).

## Methodology

- **Manual code review** of the token validation pipeline, authorization
  policies, internal-token handling, CORS, and error responses.
- **Automated scans** (wired into CI, see `.github/workflows/ci.yml`):
  - `dotnet list package --vulnerable` (NuGet advisories)
  - `bun audit --audit-level high` (npm/pnpm advisories for the web dashboard)
  - Trivy filesystem scan (CRITICAL/HIGH, SARIF → GitHub Code Scanning)
  - Dependabot security alerts for all ecosystems
  - OWASP ZAP baseline scan against the API (best-effort job)
- **Integration tests** exercising the real JwtBearer + JWKS pipeline
  (`Aveline.Api.Tests/FullAuthFlowIntegrationTests.cs`).

## Scope Reviewed

| Component | Surface |
|---|---|
| `Aveline.Api` | JwtBearer (issuer/lifetime/signing-key validation), role claim promotion, role + permission policies, CORS allow-list, internal token outbound handler, `/auth/claims`, `/policies/*`, `/agents/ping` |
| `agnet-service` | `X-Internal-Token` dependency check, health endpoint |
| Clients | Flutter + React request interceptors (Bearer attachment, 401/403 handling) |

## Findings

### High / Critical

| ID | Finding | Status |
|----|---------|--------|
| SEC-H1 | **Microsoft.OpenApi 2.0.0 (GHSA-v5pm-xwqc-g5wc, High)** — vulnerable transitive dependency flagged as NU1903. | **Fixed.** `Microsoft.AspNetCore.OpenApi` bumped 10.0.10 → 10.0.11, which pins the patched `Microsoft.OpenApi ≥ 2.7.5`. Verified clean via `dotnet list package --vulnerable`. |

No other high or critical issues were identified.

### Medium (accepted trade-offs)

| ID | Finding | Rationale / Mitigation |
|----|---------|------------------------|
| SEC-M1 | JWT **audience is not validated** (`ValidateAudience = false`). | Clerk tokens (incl. `jwt-aveline-v1`) do not carry a stable `aud`. The instance signing key (`kid` in the Clerk JWKS) already scopes tokens to this Clerk instance, and the issuer is validated. Revisit if tokens begin carrying a per-app audience. |
| SEC-M2 | **No rate limiting** on API auth endpoints. | Client authentication is rate-limited at the IdP (Clerk). The API is a thin validator; add `AddRateLimiter` throttling before production traffic if the API is exposed directly. |

### Low (documented)

| ID | Finding | Mitigation |
|----|---------|------------|
| SEC-L1 | `/auth/claims` returns the caller's own claim set (incl. email/PII). | Only reachable with a valid token (`RequireAuthorization`); it is the user's own data. No change. |
| SEC-L2 | Dev-only `Clerk:RequireHttpsMetadata=false` escape hatch. | Defaults to `true`; only used by integration tests against an in-process HTTP authority. Never enabled in production config. |
| SEC-L3 | Internal service token is a shared secret sent as a header. | Compared with `hmac.compare_digest` (constant time) in the agent; requests without it are rejected (fail-closed). Transport is HTTPS in deployment (Container Apps). |

## Mitigations Applied During This Review

1. **Security headers middleware** (`UseAvelineSecurityHeaders`): `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer` on every response (tested in `FullAuthFlowIntegrationTests`).
2. **Structured auth logging** (Issue #29): authentication success/failure events, 401/403 audit trail with `userId`, and JSON console output — enables detection of scanning/credential-stuffing attempts.
3. **Automated gates**: OWASP ZAP baseline scan in CI (best effort) and the Trivy SARIF report in Code Scanning.

## Verification

- `dotnet test` (Release): **63 passed**, including the real JWKS validation flow and the security-header check.
- `dotnet list package --vulnerable`: clean for `Aveline.Api` and `Aveline.Api.Tests`.
- Agent service: internal-token failures (unconfigured/invalid) and valid pings are logged; hmac compare is constant-time.

## Residual Risk

- Security at the IdP boundary (Clerk) is out of scope; org roles are granted by
  boutique owners in the Clerk Dashboard.
- The ZAP baseline job runs against the locally-booted API and is **best effort**
  (non-blocking) because every API route requires authentication.

## Recommendation

Review SEC-M1/SEC-M2 before public production deployment; otherwise no
high-risk vulnerabilities remain.
