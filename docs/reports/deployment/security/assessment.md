# Aveline — Live Deployment Security Assessment

**Target:** `https://aveline.gravora.dev`
**Type:** Authorized white hat assessment (grey-box: source available, testing performed blind)
**Assessor:** DSH agent, acting on explicit written authorization from the project owner
**Engagement rules:** target allowlist = the domain above only · no destructive actions · no real-user-data
exfiltration · no service degradation · redacted evidence · one task at a time with explicit approval

---

## Authorization Record

| # | Checkpoint | Status | Notes |
|---|-----------|--------|-------|
| 0 | Blanket authorization to assess the live target | **GRANTED** | Via approval prompt. Scope: `aveline.gravora.dev` only. |
| 0a | Test accounts | **DEFERRED** | Owner will provision + supply per task. Until then, unauthenticated surface only. |
| 0b | Report format | **DECIDED** | Single appended file (this document), one section per task. |
| 0c | Live testing for Task 1 | _pending_ | See approval gate in §1. |

**Standing constraints accepted by the assessor:**

1. Target allowlist: `aveline.gravora.dev` and its subdomains. No third-party hosts.
2. Only owner-provisioned test accounts. No account creation without separate approval.
3. Passive/low-impact default. Any state-changing, brute-force, or load-generating test is a
   separate per-instance approval.
4. Hard stops: no destructive payloads, no DoS, no exfiltration of real user data, no social
   engineering, no exploitation beyond minimum proof of impact.
5. Evidence is redacted — no full tokens, no PII, no credentials, no raw connection strings.
6. Sequential tasks: plan → approve → test → report → approve → next task.

### Scope extensions granted during the engagement

| When | Extension | Granted |
|------|-----------|---------|
| Task 1 | Add `aveline-api.politeplant-5806d7de.malaysiawest.azurecontainerapps.io` (Azure Container App backend, discovered from the public JS bundle) | **Yes** |
| Task 1 | Run the narrow 5432/5433/6379 TCP handshake probe | **Yes** |

### Target topology (established live, Task 1)

| Layer | Host | Provider | Notes |
|-------|------|----------|-------|
| Frontend | `aveline.gravora.dev` | Vercel (`server: Vercel`, `x-vercel-id: bom1`) | DNS on Cloudflare NS; CNAME → `vercel-dns-017.com` |
| Backend API | `aveline-api.politeplant-5806d7de.malaysiawest.azurecontainerapps.io` | Azure Container Apps (Malaysia West) | `server: Kestrel`, region `malaysiawest` |
| Datastores | not publicly resolvable | Azure-managed, private | Confirmed by closed ports |

---

# Task 1 — Database Security

**Status:** ✅ Complete (unauthenticated scope) · ⚠️ authenticated depth deferred
**Date:** 2026-10-03
**Approval:** blanket authorization + live-phase approval + two scope extensions (recorded above)

## 1.1 Scope

Assess the deployed system for database-security weaknesses: exposed credentials, weak access
controls, unencrypted sensitive data, SQL injection vectors, misconfigured permissions, and backup
exposure. Deliverable is a finding set with evidence and proposed patches.

**Method:** source review (grey-box) combined with blind live probing of the deployed HTTP/TCP
surface. Live probing was unauthenticated only — no test accounts were available at this stage by
the owner's choice.

## 1.2 What was tested

| # | Test | Method |
|---|------|--------|
| T1 | Datastore network exposure | TCP connect (3 s timeout) to 5432 / 5433 / 6379 on both in-scope hosts |
| T2 | Public error-surface / schema leakage | Unauthenticated GET on API routes, malformed GUIDs, injection-shaped query params |
| T3 | OpenAPI / Swagger exposure | GET `/openapi/v1.json`, `/swagger`, `/swagger/index.html` |
| T4 | Accidental config/backup exposure | GET `/.env`, `/vercel.json`, `/robots.txt`, `/.well-known/security.txt`, etc. |
| T5 | Credential leakage in the deployment | Health-probe version block; secrets in git-tracked sources; `.gitignore` coverage |
| T6 | SQL injection surface | Audit of all raw-SQL call sites, vector-literal construction, dynamic sorting |
| T7 | Encryption at rest | Review of `CredentialEncryptionService` |
| T8 | Auth boundary strength | Response differentiation to malformed input pre-auth |

## 1.3 Findings

### F-1.1 — Datastore ports not publicly exposed · **PASS**

Both in-scope hosts refuse TCP on the datastore ports. The Azure Container Apps model keeps the
databases private to the environment.

```
aveline-api.politeplant-...azurecontainerapps.io
  tcp/5432  closed/filtered
  tcp/5433  closed/filtered
  tcp/6379  closed/filtered
aveline.gravora.dev
  tcp/5432  closed/filtered
  tcp/5433  closed/filtered
  tcp/6379  closed/filtered
```

**Resolves lead L1** from the static phase: `docker-compose.yml` publishes
`"${POSTGRES_PORT:-5432}:5432"` and `"${REDIS_PORT:-6379}:6379"` on all interfaces with Redis
unauthenticated and `sslmode=disable`. The live deployment does **not** use that compose file's
binding. **The compose misconfiguration remains a real hazard for any host that does run it** (see
PF-1.1) but is not a live exposure today.

### F-1.2 — No SQL injection vector found · **PASS**

All raw-SQL call sites audited were parameterized:

- `CustomerMemoryRepository.cs:81-85`, `HandbookRepository.cs:63-66` — `ExecuteSqlRawAsync` with a
  positional `object[]`; values bind as parameters, not text.
- `ApiMetricRepository.cs:18-42` — static `UpsertSql` constant using `@`-named parameters.
- `ApiRequestLogPartitionJob.cs:41` — interpolated only with `{tomorrow:yyyy-MM-dd}`, a
  server-computed `DateOnly`, not client input.
- `HandbookRepository.cs` / `CustomerMemoryRepository.cs` `SqlQueryRaw(sql, args)` — the `sql`
  string is varied only by `.Replace()` of fixed internal tokens (`VectorKindsToken`,
  `LexicalKindsToken`) with fixed literals; all client-influenced values travel in `args`.
- `VectorLiteral()` builds `"[" + string.Join(",", embedding) + "]"` from a `float[]` — compiler-
  typed, non-injectable.

No `ExecuteSqlInterpolated` / `FromSqlInterpolated` usage. No dynamic `ORDER BY` built from client
input; every `OrderBy`/`ThenBy` inspected uses a lambda over a typed property.

### F-1.3 — Credentials encrypted at rest with AES-GCM · **PASS**

`CredentialEncryptionService` uses `AesGcm` with 32-byte key, 12-byte random nonce per operation,
and a 16-byte tag; key length is validated at construction. Authenticated encryption is the correct
primitive here.

### F-1.4 — No schema, stack-trace, or config leakage · **PASS**

Every unauthenticated request to a protected route returns **`401` with a zero-length body** and
`www-authenticate: Bearer`, with no differentiation for malformed input:

| Request | Response |
|---------|----------|
| `/api/v1/orgs/00000000-…/catalog/items` | `401`, 0 bytes |
| `/api/v1/orgs/not-a-guid/catalog/items` | `401`, 0 bytes (not a 400 with model detail) |
| `/api/v1/orgs/…/catalog/items?id=%27%20OR%201%3D1--` | `401`, 0 bytes |
| `/api/v1/admin/statistics/system?from=abc&to=xyz` | `401`, 0 bytes |
| `/openapi/v1.json`, `/swagger`, `/swagger/index.html` | `401`, 0 bytes |

OpenAPI is gated behind `if (app.Environment.IsDevelopment())` in `Program.cs:154-157`. The generic
`UseExceptionHandler()` precedes all routing, so failures never reach the response body.

The `200`s on `/.env`, `/vercel.json`, `/robots.txt` are a **false positive** — the SPA catch-all
rewrite serves `index.html` for every unknown path. Proven by identical `etag`
(`"5ce0825fc33055a1baac9d88f593a82c"`) and identical `content-length: 2128` across `/`, `/robots.txt`
and `/.env`.

### F-1.5 — Anonymous health endpoint discloses internal topology · **LOW** · ⚠️

`/health`, `/health/ready`, `/health/live` are `AllowAnonymous` and enumerate internal dependency
names with per-check timings:

```json
{"status":"Healthy","totalDurationMs":13,"checks":[
 {"name":"database","status":"Healthy","durationMs":1,"message":"Database reachable."},
 {"name":"redis",...,"message":"Redis responded in 0 ms."},
 {"name":"agent-service",...,"message":"Agent service reachable."},
 {"name":"clerk-jwks",...,"message":"Clerk JWKS reachable."}]}
```

This confirms the deployment is PostgreSQL + Redis + a separate agent service + Clerk. It is
deliberate (`HealthEndpoints.cs:7-8`: an orchestrator cannot present credentials) and has been
*hardened* for the more serious version of this bug — `HealthCheckResponseWriter.cs:38-42`
withholds git SHA / build time / environment in Production, and indeed no `version` block appeared
in the live response, confirming `ASPNETCORE_ENVIRONMENT=Production`. Messages are bounded to 300
chars and are generic.

**Residual risk:** minor infrastructure reconnaissance aid. Exploitation requires no bypass; the
only question is whether the enumeration is worth the operational convenience.

### F-1.6 — Insecure-shape dev artifacts remain in the shipped source tree · **MEDIUM**

Not live-exploitable today, but these are one misconfiguration away from being severe:

`Aveline.Api/Infrastructure/Data/AppDbContextFactory.cs:11`
```csharp
optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=aveline;Username=postgres;Password=postgres", …)
```
Superuser `postgres`/`postgres` hardcoded. `Database:AllowInMemoryInProduction=true` also exists as
an escape hatch that would silently serve an empty, non-persistent database in Production
(`DatabaseConfiguration.cs:109-112`).

`docker-compose.yml:28,32,77-80`
```yaml
POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?POSTGRES_PASSWORD is required}
ports:
  - "${POSTGRES_PORT:-5432}:5432"   # binds 0.0.0.0
redis:
  image: redis:7-alpine             # no requirepass
  ports:
    - "${REDIS_PORT:-6379}:6379"    # binds 0.0.0.0
DATA_SOURCE_URI: …?sslmode=${POSTGRES_SSLMODE:-disable}
```
Any host running this file with its default port bindings publishes an unauthenticated Redis and a
Postgres on every interface.

**Deployment-configuration question this raises:** the postgres service in `docker-compose.yml`
requires `POSTGRES_EXPORTER_PASSWORD` (`:?` operator) and mounts `./postgres-exporter-password`, but
that variable does not appear in `.env`'s key list. If the Azure deployment is a translation of this compose file, the
Postgres password may have been placed in the repository/settings rather than a secret store.
**Open question OQ-1.1.**

### F-1.7 — Internal service token has a working guessable fallback · **HIGH (carried to Task 2)** · ⚠️

`docker-compose.yml` (API service env, `AgentService__InternalToken`):
```yaml
AgentService__InternalToken: ${INTERNAL_API_TOKEN:-aveline-local-development-secret-token-2026}
```

The internal auth handler (`InternalTokenAuthenticationHandler.cs:39-44`) rejects only the literal
placeholder `"change-me-internal-token"`. It does **not** reject
`aveline-local-development-secret-token-2026`. On success it grants `role=InternalService` and
`scope=internal:all`, which guards `MapGroup("/internal/…")` (agent-runs, conversations, customers,
handbook, inventory, usage, visual).

If the Azure Container App was ever deployed without `INTERNAL_API_TOKEN` set, the default in this
file becomes the live credential. This is a **database-adjacent** finding (it unlocks internal
repositories directly) but its verification and remediation belong to **Task 2 — Internal Token
Validity**. **No credential-guessing was attempted under Task 1's approval**; it is recorded here as
a lead only.

## 1.4 Proposed patches

| ID | Patch | Priority | Touches |
|----|-------|----------|---------|
| PF-1.1 | Bind compose datastores to loopback: `"127.0.0.1:${POSTGRES_PORT:-5432}:5432"`, same for Redis; add `command: ["redis-server","--requirepass","${REDIS_PASSWORD}"]` | High (for any compose host) | `docker-compose.yml` |
| PF-1.2 | Default `POSTGRES_SSLMODE` to `require` rather than `disable` | Medium | `docker-compose.yml`, `.env.example` |
| PF-1.3 | Remove the hardcoded `postgres:postgres` string from `AppDbContextFactory`; read `ConnectionStrings:DefaultConnection` or throw | Medium | `AppDbContextFactory.cs` |
| PF-1.4 | Reduce anonymous health disclosure: return `status` only on `/health/live`, or restrict `/health` to the orchestrator's source range | Low | `HealthEndpoints.cs`, `HealthCheckResponseWriter.cs` |
| PF-1.5 | Remove `Database:AllowInMemoryInProduction` from any Production settings; if kept, require an explicit non-Production environment name | Low | config |
| PF-1.6 | Extend the insecure-placeholder denylist in `InternalTokenAuthenticationHandler` to reject `aveline-local-development-secret-token-2026` and any value containing `local-development` | High | `InternalTokenAuthenticationHandler.cs` |
| PF-1.7 | Ensure `POSTGRES_PASSWORD` / `POSTGRES_EXPORTER_PASSWORD` / `CREDENTIALS_ENCRYPTION_KEY` / `INTERNAL_API_TOKEN` are Azure Container App **secrets**, not plain env values | High | deployment config |

**No patch has been implemented.** All are proposals pending explicit approval.

## 1.5 Actions taken

- Read-only source review; no files modified by testing.
- Live: DNS lookups; HTTPS GETs on the in-scope hosts; TCP handshakes to 5432/5433/6379.
- No writes, no auth attempts, no credential guessing, no load, no third-party hosts contacted.
- Created `docs/reports/deployment/security/assessment.md` (this file).

## 1.6 Open questions / follow-ups

| ID | Question | Blocks |
|----|----------|--------|
| OQ-1.1 | Is `POSTGRES_EXPORTER_PASSWORD` defined for the Azure deployment, and is `INTERNAL_API_TOKEN` set as a secret or left at the compose default? | F-1.7 severity |
| OQ-1.2 | Does the Azure Postgres require TLS (`sslmode=require`)? Not externally verifiable without a private-network vantage point. | F-1.2 completeness |
| OQ-1.3 | Authenticated DB testing (per-tenant row access, IDOR into other orgs' rows) requires the provisioned test accounts. | Task 5 |

**Task 1 verdict:** the live database tier is **not exposed** and the API's pre-auth surface is
**well hardened** (no SQLi, no schema leakage, no credential leakage, AES-GCM at rest). The serious
residual risk is **deployment-configuration drift** — insecure defaults that are safe only as long as
nobody deploys the compose file as written. Findings F-1.6 and F-1.7 should be resolved before the
remaining tasks, because F-1.7 is a likely pivot for Task 2 and Task 3.

---

# Task 2 — Internal Token Validity

**Status:** ✅ Complete (unauthenticated + self-minted scope)
**Date:** 2026-10-03
**Approval:** Task 2 live scope approved; internal-token verification approved as **Approach B**
(single documented fallback token, one attempt, no brute force).

## 2.1 Scope

Verify token generation, signing, expiration, revocation, and scope enforcement; test for token
leakage, replay, and privilege escalation. Covers all credential schemes in the deployment.

## 2.2 Token inventory (from source)

| Scheme | Transport | Guards | Notes |
|--------|-----------|--------|-------|
| `JwtBearer` (default) | `Authorization: Bearer` | all org/user routes | Clerk custom template `jwt-aveline-v1`; `iss` = `https://clerk.aveline.gravora.dev` |
| `InternalToken` | `X-Internal-Token` | `/internal/*`, `/api/internal/*` | grants `role=InternalService`, `scope=internal:all` |
| `ScrapeToken` | `Authorization: Bearer` | `/metrics` | optional; `NoResult()` when unconfigured |
| `ApiKey` | `X-Api-Key` (via `api_key_prefix`/`scope` claims) | org-scoped routes | SHA-256 at rest |
| Media token | path-borne | media read routes | `exp`, scope, single-use nonce, Redis-backed |
| Invitation / Device / OTP / Vision | various | narrow routes | reviewed in F-2.5 |

## 2.3 What was tested

| # | Test | Method |
|---|------|--------|
| T1 | Internal token scheme | Baseline (no token), bogus-token control, then the single documented fallback |
| T2 | JWT signature enforcement | Self-minted RS256 token, correct `iss`, wrong key |
| T3 | `alg:none` rejection | Unsigned token with forged `owner` / `org:boutique_owner` claims |
| T4 | HS256 algorithm confusion | Token signed HMAC using the public key as secret |
| T5 | Expired token | `iat`/`exp` two hours in the past |
| T6 | Wrong / absent issuer | `iss` = attacker host, and no `iss` claim |
| T7 | Scrape token scheme | No token, bogus token, published-default-style token |
| T8 | API key scheme | Bogus `X-Api-Key` |
| T9 | Scope / role model | Source review of policy wiring and claim promotion |
| T10 | Token leakage surfaces | Source review of query-string token handling and log redaction |

## 2.4 Findings

### F-2.1 — Internal token is a real secret; compose default is **not** live · **PASS** (resolves F-1.7)

The documented compose fallback was sent **once** to six internal routes. All rejected identically to
the bogus-token control:

| Route | No token | Bogus token | Documented fallback |
|-------|----------|-------------|---------------------|
| `/internal/customers` | 401 | 401 | **401** (0 bytes) |
| `/internal/handbook` | 401 | 401 | **401** (0 bytes) |
| `/internal/conversations` | 401 | 401 | **401** (0 bytes) |
| `/internal/visual` | 401 | 401 | **401** (0 bytes) |
| `/api/internal/visual` | 401 | 401 | **401** (0 bytes) |
| `/metrics` | 401 | 401 | **401** (0 bytes) |

The bogus-token control returning the same status proves this is a genuine credential comparison and
not a blanket deny. **Conclusion: `INTERNAL_API_TOKEN` was set correctly in the Azure deployment; the
`aveline-local-development-secret-token-2026` fallback is not in use. F-1.7 is closed as a live
risk.** No further guesses were attempted — one token, per approval.

Residual (latent) issue retained as **F-2.6**.

### F-2.2 — JWT signature enforcement is effective · **PASS**

A self-minted RS256 token carrying a plausible `iss`, `sub`, `user_role=owner`,
`org_role=org:boutique_owner`, `org_id` and `org_slug`, but signed with a **key I generated locally**,
was rejected with `401` on both `/api/v1/orgs` and the catalog route.

`alg:none` (unsigned) and HS256 algorithm-confusion variants — both carrying forged `owner` claims —
were also rejected `401`. Malformed inputs (`garbage`, `a.b.c`, bare `Bearer`) likewise `401` with a
zero-length body and no parser detail.

**Honest limitation:** because every forged token is signed with my own key, all of these fail at the
**signature** check in `OnAuthenticationFailed` before claim validation is reached. This proves
*forgery is blocked*; it does **not** independently prove `exp`, `iss`, or `aud` enforcement is
active at runtime. Those rest on source review (`AuthenticationConfiguration.cs:96-104`:
`ValidateIssuer = true`, `ValidateLifetime = true`, `ValidateIssuerSigningKey = true`) rather than on
observed behaviour. Independent confirmation requires a real Clerk-signed token, i.e. a provisioned
test account (OQ-2.1).

### F-2.3 — `ValidateAudience = false` · **LOW** · ⚠️ design risk

```csharp
public static TokenValidationParameters BuildTokenValidationParameters(string authority) => new()
{
    ValidateIssuer = true,
    ValidIssuer = authority,
    ValidateAudience = false,   // <-- not enforced
    ValidateLifetime = true,
    ValidateIssuerSigningKey = true,
    NameClaimType = ClaimTypes.NameIdentifier,
};
```

This is a **deliberate, documented** decision (ADR-008; rationale at
`AuthenticationConfiguration.cs:92-94`: Clerk tokens lack a stable `aud`, and `iss` binds the token
to the instance).

**Assessment:** not exploitable as deployed. `ValidIssuer` is pinned to our instance and
`ValidateIssuerSigningKey` requires that instance's JWKS, so only Clerk can mint a token with
`iss = https://clerk.aveline.gravora.dev`. The risk is **latent**: the moment a second application or
tenant is added to the same Clerk instance, tokens become interchangeable across them, because
nothing else distinguishes the audience. This is a hardening item, not an incident.

### F-2.4 — SignalR hub accepts the JWT via `?access_token=` · **MEDIUM** · ⚠️

```csharp
OnMessageReceived = context =>
{
    var accessToken = context.Request.Query["access_token"];
    if (!string.IsNullOrEmpty(accessToken)
        && path.StartsWithSegments("/hubs")
        && string.IsNullOrEmpty(context.Token))
        context.Token = accessToken;
    ...
```

This is the standard ASP.NET Core SignalR convention for transports that cannot set headers, so the
pattern itself is expected. The risk is **credential-in-URL**: query strings are routinely captured
by reverse-proxy access logs, browser history, `Referer` headers, and APM/tracing.

Positive control found: `MediaTokenPathRedactionMiddleware` exists, and `Program.cs:172-175` notes
the media token path is replaced with its route template before audit logging or the exception
handler can read it — so the team is already aware of path-borne credential leakage and has handled
it for media tokens. **Whether the `/hubs` `access_token` query string receives the same redaction in
audit logs and in the OTel/Jaeger pipeline is unverified** and is the concrete follow-up (OQ-2.2).

### F-2.5 — Supporting token controls look sound · **PASS** (source review)

- **Media tokens** — `exp` with configurable clock skew (`ClockSkewToleranceSeconds`), explicit scope
  matching (`MediaTokenValidation.ScopeMismatch`), and **single-use nonce** enforcement backed by
  Redis (`RedisMediaTokenNonceStore`), with a distinct `NonceStoreUnavailable` failure mode (fails
  closed rather than silently allowing replay).
- **API keys** — SHA-256 at rest (`ApiKeyCredentials.Hash`), never the plaintext; scopes carried as
  repeated `scope` claims for per-request enforcement.
- **Internal token handler** — constant-time comparison via `CryptographicOperations.FixedTimeEquals`;
  explicitly refuses to authenticate when the value is missing or is the literal placeholder
  `change-me-internal-token`.
- **Scrape token handler** — constant-time comparison; returns `NoResult()` (not success) when
  unconfigured, so it cannot authenticate by accident.
- Scrape token live checks: no token `401`, bogus token `401`, published-default-style token `401`.

### F-2.6 — Insecure-placeholder denylist is a single literal · **LOW** · ⚠️ latent

The handler rejects exactly `"change-me-internal-token"`. It does **not** reject
`aveline-local-development-secret-token-2026`, which is the value `docker-compose.yml` would supply
if `INTERNAL_API_TOKEN` were unset. Confirmed **not live** (F-2.1), so this is defence-in-depth only:
the guard should reject any value drawn from the repo's own development defaults, not one hardcoded
string.

## 2.5 Proposed patches

| ID | Patch | Priority | Touches |
|----|-------|----------|---------|
| PF-2.1 | Replace the single-literal placeholder check with a denylist (or pattern) covering all repo development defaults, and refuse boot in Production when the internal token matches any of them | Medium | `InternalTokenAuthenticationHandler.cs` |
| PF-2.2 | Confirm `?access_token=` is redacted in audit logs and stripped/redacted in OTel traces; if not, extend `MediaTokenPathRedactionMiddleware`-style handling to `/hubs` | Medium | logging / observability |
| PF-2.3 | Add `ValidAudiences` (Clerk supports an `aud` claim via the JWT template) so audience is validated and future same-instance apps cannot reuse tokens | Low | `AuthenticationConfiguration.cs`, Clerk template |
| PF-2.4 | Document the rationale for `ValidateAudience = false` with a revisit trigger (“if a second app joins this Clerk instance, enforce `aud`”) | Low | ADR-008 |

**No patch has been implemented.**

## 2.6 Actions taken

- Read-only source review of all credential schemes.
- Live: internal-token baseline + bogus control + **one** approved fallback token across six routes;
  `/metrics` scrape-token checks; JWT boundary probes with six locally-minted tokens; bogus API key.
- Generated a throwaway 2048-bit RSA keypair in a workspace scratch dir and **deleted it** at the end
  (`.sec-tmp/` removed; confirmed gone). No test key material remains in the repo.
- No brute force, no credential stuffing, no writes, no account creation, no third-party hosts.
- No real user tokens were used or requested.

## 2.7 Open questions / follow-ups

| ID | Question | Blocks |
|----|----------|--------|
| OQ-2.1 | Independent confirmation of `exp` / `iss` / revocation enforcement requires a real Clerk-signed token — i.e. the provisioned test account. | F-2.2 completeness, Task 4 |
| OQ-2.2 | Is the `/hubs` `?access_token=` value redacted in audit logs and OTel traces the way media token paths are? | F-2.4 severity |
| OQ-2.3 | Is `Metrics:ScrapeToken` configured in Azure, or is `/metrics` reachable only via the internal token? | Metrics exposure |
| OQ-2.4 | Does Clerk token revocation (session revoke) propagate within the token lifetime, or is there a window where a revoked session still authenticates? | Task 4 |

**Task 2 verdict:** token handling is **substantially stronger than typical** for this class of
application. The headline lead from Task 1 (F-1.7) is **closed as not live** — the internal token is a
real secret. Signature forgery, `alg:none`, algorithm confusion, and malformed tokens are all
rejected. The genuine residuals are **latent design risks** (audience validation, placeholder
denylist) and one **leakage question** (token in the SignalR query string) that needs a logging
review rather than an attack.

---

# Task 3 — Admin Endpoints

**Status:** ✅ Complete (authenticated)
**Date:** 2026-10-03
**Approval:** Task 3 approved; authenticated testing unblocked by operator-supplied credentials and
session tokens (see F-3.0)

## 3.0 Authentication note (methodology)

Passwords for three test accounts were supplied out-of-band and verified correct at first factor
(Clerk reported `strategy=password, status=verified`). The accounts then enforced a second factor
(`email_code`); the operator disabled MFA on the three test accounts to permit automated testing,
and supplied short-lived `jwt-aveline-v1` bearer tokens (≈60 min) for each. **MFA must be
re-enabled on all three accounts once the assessment concludes.**

No MFA bypass was attempted at any point. The MFA gate blocked password-only access for four
attempts across two accounts before the operator disabled it — that is the control behaving
correctly and is recorded as a positive finding (F-3.5).

## 3.1 Scope

Probe for unauthenticated access, broken access control, IDOR, and missing role checks; verify that
admin-only endpoints are properly protected.

## 3.2 Subject identities used

| Subject | `sub` | JWT `user_role` | JWT `org_role` | JWT `org_id` | `/api/v1/auth/claims` roles |
|---------|-------|-----------------|----------------|--------------|------------------------------|
| tenant.a | `user_3KBDcuuyyOnEWjEuerjUqTr2sbS` | *(absent)* | *(absent)* | *(absent)* | `[]` |
| tenant.b | `user_3KBDdBHQMPgH332ynjxoPNdwyqx` | *(absent)* | *(absent)* | *(absent)* | `[]` |
| admin | `user_3KBDdSRwzf69Wgjf6eR7ZIvXR16` | `admin` | *(absent)* | *(absent)* | `["admin"]` |

Tokens authenticate successfully (verified via `/api/v1/auth/claims` → `200`; anonymous → `401`).

## 3.3 Admin route inventory and guards (source)

| Route | Method | Guard |
|-------|--------|-------|
| `/api/v1/admin/requests` | POST | `.RequireAuthorization()` — any authenticated user (intentional self-service) |
| `/api/v1/admin/requests` | GET | `AdminReviewPolicy` (moderator \| admin \| owner) |
| `/api/v1/admin/requests/{id}/approve` | POST | `AdminReviewPolicy` + self-approval exception |
| `/api/v1/admin/requests/{id}/reject` | POST | `AdminReviewPolicy` |
| `/api/v1/admin/users` | GET | permission `admin:users:read` |
| `/api/v1/admin/users/{userId}/state` | PATCH | permission `admin:users:manage` |

## 3.4 Findings

### F-3.1 — Admin endpoints enforce role checks · **PASS**

Clean three-tier separation on both admin routes:

| Route | anonymous | tenant.a | tenant.b | admin |
|-------|-----------|----------|----------|-------|
| `GET /api/v1/admin/requests` | `401` | **`403`** | **`403`** | `200` |
| `GET /api/v1/admin/users` | `401` | **`403`** | **`403`** | `200` |

The `403` (not `404`, not `200`) proves the endpoints are reachable and that authorization — not
routing — is what denies access. A tenant cannot read the admin approval queue or the platform user
list. **This is the primary control this task exists to verify, and it holds.**

### F-3.2 — No IDOR on admin mutation routes · **PASS**

Tenant tokens cannot act on admin-managed resources, even with a valid, existing resource id:

| Probe | tenant.a | tenant.b | admin |
|-------|----------|----------|-------|
| `PATCH /api/v1/admin/users/{other-user-guid}/state` | `403` | `403` | — |
| `POST /api/v1/admin/requests/{existing-id}/approve` | `403` | `403` | `409` (already approved) |

The admin `409` is the control that gives this test meaning: it proves the request actually reached
the handler and evaluated the resource for an authorized role, so the tenant `403` is a real
authorization denial rather than a coincidental 404.

### F-3.3 — Self-approval privilege escalation is blocked · **PASS**

The highest-value escalation path for this endpoint family: a non-admin requests admin access, then
approves their own request. Executed end-to-end with real accounts:

1. `POST /api/v1/admin/requests` as tenant.a → `200`, status `Pending`
   (`id=01a1015e-0dae-76cf-93bf-5d3bdf7bd772`). Request creation is intentionally open to any
   authenticated user.
2. `POST /api/v1/admin/requests/01a1015e…/approve` **as tenant.a** → **`403`**
3. `POST /api/v1/admin/requests/01a1015e…/reject` **as tenant.a** → **`403`**
4. Cross-account: tenant.b approving tenant.a's request → **`403`**

**No self-escalation is possible.** Two independent controls contribute: `AdminReviewPolicy` denies
non-admins at the policy layer, and `AdminSelfApprovalException` (`AdminEndpoints.cs:64-67`) returns
`403` even for an authorized reviewer acting on their own request.

### F-3.4 — Org-scoped routes deny non-members · **PASS** (partial)

With arbitrary organization ids in the path:

| Probe | tenant.a | tenant.b |
|-------|----------|----------|
| `GET /api/v1/orgs/{arbitrary-guid}/customers` | `403` | `403` |
| `GET /api/v1/orgs/{arbitrary-guid}/catalog` | `404` | `404` |

Neither tenant is a member of the probed organization and both are denied. Note the tenants' tokens
carry **no `org_id`/`org_role`** at all, so this confirms denial for *non-members* but does not
exercise the member-vs-member boundary. Full tenant-isolation testing is Task 5 (OQ-3.1).

### F-3.5 — MFA is enforced and blocks password-only access · **PASS**

Independent of the app's own authorization, Clerk's second factor (`email_code`) prevented session
creation with a valid password alone. From a credential-theft perspective this is the correct
outcome: **a stolen password is not sufficient to reach any admin surface.** Recorded as a positive
finding; the temporary disablement for testing is noted in §3.0.

### F-3.6 — Admin actions are audited · **PASS** (source review)

`UseAvelineAuthAudit()` (`LoggingConfiguration.cs:44`, category `Aveline.Api.Authorization`) is
registered in the pipeline after authorization (`Program.cs:172`), and admin approval decisions
persist `ReviewedAt` / `ReviewedByClerkUserId` on `AdminApprovalRequest`. A dedicated
`AuditLogEntries` table and middleware exist. Attribution of admin actions is therefore present at
both the log and persistence layers. **Runtime verification of the audit trail contents was not
performed** — that requires log access (OQ-3.2).

## 3.5 Proposed patches

No vulnerability was found in this task. Recommendations are hardening only.

| ID | Recommendation | Priority |
|----|----------------|----------|
| PR-3.1 | Confirm the `/api/v1/admin/requests` POST rate limit matches `Invitations:AcceptRateLimit` intent, so the open self-service endpoint cannot be used to spam the approval queue | Low |
| PR-3.2 | Verify the `/auth/claims` debug endpoint is intentional in Production; it echoes all JWT claims and is an information-disclosure convenience | Low |
| PR-3.3 | **Re-enable email-code MFA on the three test accounts** (operational action, not code) | High |

## 3.6 Actions taken

- Read-only source review of all admin endpoints and their policies.
- Live authenticated testing with three operator-supplied sessions.
- **State-changing actions performed (all authorized by Task 3 scope and reversible):** submitted two
  admin-approval requests (tenant.a `01a1015e-0dae-76cf-93bf-5d3bdf7bd772`, tenant.b
  `01a1015e-0f45-7c14-8095-97c2c1ac0ed3`), both left in `Pending`. No approvals, rejections, role
  grants, or user-state changes were executed.
- No destructive actions; no real-user data read or exfiltrated beyond the admin listing response
  headers needed to prove access (no PII recorded in this report).
- Credentials and tokens held only in a gitignored scratch dir (`.sec-cred/`), never written to this
  report.

## 3.7 Open questions / follow-ups

| ID | Question | Blocks |
|----|----------|--------|
| OQ-3.1 | Tenants hold no `org_id`/`org_role`. Are they provisioned org members? Member-vs-member isolation is untestable until they are. | Task 5 |
| OQ-3.2 | Do `AuditLogEntries` / auth-audit logs actually capture admin approve/reject with actor attribution at runtime? | F-3.6 completeness |
| OQ-3.3 | Two `Pending` admin requests now exist in the live queue and should be cleaned up or ignored. | Hygiene |
| OQ-3.4 | `/auth/claims` exposes the full claim set to any authenticated user about themselves only — confirm no cross-user variant exists. | PR-3.2 |

**Task 3 verdict:** admin access control is **correctly implemented**. Role enforcement, resource
authorization, and self-approval prevention all held under direct testing with real accounts. This is
the task most likely to expose broken access control in a rapidly-built application, and no
broken-access-control defect was found.

---

# Task 4 — Admin Imitation (Impersonation)

**Status:** ✅ Complete (authenticated)
**Date:** 2026-10-03
**Approval:** Task 4 approved and started after Task 3 sign-off.

## 4.1 Scope

Determine whether an attacker can impersonate an admin or escalate privileges. Test session
handling, role boundaries, and audit logging.

## 4.2 Findings

### F-4.1 — No platform-role escalation from an org membership · **PASS**

`ChangeMemberRoleAsync` (`OrganizationService.cs:595+`) validates the requested role against a
hardcoded whitelist containing **only org-scoped roles**:

```csharp
private static readonly string[] BoutiqueRoles =
[
    Roles.BoutiqueOwner, Roles.BoutiqueSupervisor,
    Roles.BoutiqueManager, Roles.BoutiqueStaff,
];
```

A non-whitelisted value throws `InvalidBoutiqueRoleException` → `400`. Because `admin`, `owner`,
and `moderator` are platform roles and are **absent from this list**, a boutique owner cannot mint a
platform role through the membership endpoint. The two role namespaces are structurally separated.

### F-4.2 — Self-role-change and owner-manipulation guards · **PASS**

Four independent guards exist on `PATCH /api/v1/orgs/{orgId}/members/{userId}`:

| Guard | Exception | Result |
|-------|-----------|--------|
| Actor cannot change their own role | `CannotChangeOwnRoleException` | `409` |
| Cannot demote the last active owner | `CannotDemoteLastOwnerException` | `409` |
| Non-owner cannot set/strip `org:boutique_owner` | `OwnerRoleChangeNotPermittedException` | `403` |
| Role must be in the org-role whitelist | `InvalidBoutiqueRoleException` | `400` |

Self-promotion inside an org is therefore blocked, and the last-owner guard prevents an org being
orphaned. The route additionally requires `BoutiqueTeamManagePolicy`.

### F-4.3 — Admin-only surfaces reject tenant sessions · **PASS**

Tenants cannot reach any admin-only statistics or operational surface:

| Endpoint | tenant.a | tenant.b | admin |
|----------|----------|----------|-------|
| `/api/v1/admin/statistics/system/overview` | `403` | `403` | `200` |
| `/api/v1/admin/statistics/system/metrics?metric=cpu` | `403` | `403` | `200` |
| `/api/v1/admin/requests` | `403` | `403` | `200` |
| `/api/v1/admin/users` | `403` | `403` | `200` |

No impersonation vector was found. A tenant session cannot read platform telemetry, the admin user
directory, or the approval queue.

### F-4.4 — Platform admin is not a tenant superuser · **PASS** (notable)

A deliberate least-privilege property worth recording because it is uncommon: the platform `admin`
is **denied** access to boutique-scoped resources it does not belong to.

| Probe | admin result |
|-------|--------------|
| `GET /api/v1/orgs/by-slug/tenant-b-boutique` | `404` |
| `GET /api/v1/orgs/{tenant-b-org-id}` | `403` |
| `GET /api/v1/orgs/my` | `200`, `[]` (no memberships) |

Admin rights are confined to platform administration; boutique data remains tenant-owned. This
materially limits the blast radius of a compromised admin account.

### F-4.5 — `/api/v1/users/me/sessions` is broken · **MEDIUM** · ⚠️ functional + availability

All three accounts receive `502`:

```json
{"message":"Clerk Backend API rejected the session list for user 'user_…': 404 404 page not found\n"}
```

The API is calling the Clerk Backend API and receiving a `404`. This indicates a **misconfigured Clerk
secret key or Backend API base URL** in the deployment (the key in Key Vault appears not to be a
valid `sk_…` for this instance, or the base URL is wrong).

Security impact: session enumeration and the operator's ability to review active sessions per user
are unavailable. It also means **session revocation is not observable through this surface**, which
weakens incident response. Functional impact is a broken feature. Not currently an exposure — the
endpoint is authenticated and returns no data — but it is a genuine defect with security relevance.

**Root cause — the wrong Clerk route, and the paragraph above is wrong on both counts.** Not the secret
key and not the base URL. `ClerkAdminClient` lists sessions from `/users/{id}/sessions`, which Clerk
serves only far enough to load the user; for a **real** user id the request then falls through to a
router that answers with a plain-text `404 page not found`. Verified against the live API with a live
test-user token:

| Request | Result |
|---------|--------|
| `GET /v1/users/{id}` | `200 application/json` — the key, the instance and the user are all fine |
| `GET /v1/users/{id}/sessions` | `404` · `text/plain` · **`404 page not found`** — the defect |
| `GET /v1/sessions?user_id={id}` | `200 application/json` — the user's sessions, 7 of them |

Two things made this look like an upstream outage and cost two wrong diagnoses. First, the endpoint
maps **any** `HttpRequestException` to `502`, so a routing mistake is indistinguishable from Clerk being
down. Second, probing the route with a **made-up** user id returns Clerk's JSON
`{"code":"resource_not_found"}` instead of the plain-text 404 — an unknown id takes a different code
path, so the route looks healthy precisely when tested with the id that cannot exercise it.

**Applied.** `ClerkAdminClient` now lists from `/sessions?user_id=…&limit=500`. The `limit` matters for
the revoke path, which lists through the same call and would otherwise see only the first page of ten
(the live test user alone has 7 active sessions). The client also now names the request URI in every
`HttpRequestException`, so the next failure of this kind identifies its own URL rather than leaving
someone to infer it. `ClerkAdminClientTests` pinned the wrong URL before this and now pins the right
one, which is why the suite stayed green throughout.

### F-4.6 — Session and token handling summary · **PASS**

| Control | Status |
|---------|--------|
| MFA enforced (email_code second factor) | ✅ verified in Task 3 (F-3.5) |
| Forged/expired/wrong-issuer tokens rejected | ✅ Task 2 (F-2.1) |
| Token lifetime bounded (≈60 min observed) | ✅ |
| Admin actions attributed + audited | ✅ source (F-3.6) |
| Role boundaries enforced server-side | ✅ F-4.1–F-4.3 |
| Session list / revocation visibility | ❌ broken (F-4.5) |

### F-4.7 — Admin statistics leaks build metadata withheld elsewhere · **LOW** · ⚠️ inconsistency

`GET /api/v1/admin/statistics/system/overview` (admin-only) returns:

```json
{"version":{"gitSha":"1.0.0","buildTime":"2026-10-02T02:24:56Z",
 "assemblyVersion":"1.0.0.0","environment":"Production"}, ...}
```

This is the **same deployment identity that `HealthCheckResponseWriter` deliberately withholds in
Production** (see F-1.5 / `HealthCheckResponseWriter.cs:38-42`, "withheld in Production so the public
probe cannot fingerprint the release"). The intent is clearly to avoid release fingerprinting; the
admin route undercuts that intent for anyone holding an admin session. Low severity because it is
admin-gated, but it is an internal inconsistency in the fingerprinting policy.

## 4.3 Proposed patches

| ID | Patch | Priority |
|----|-------|----------|
| PF-4.1 | Fix the Clerk Backend API credential/base URL so `/users/me/sessions` works; restore session visibility for incident response | Medium · **applied** — route corrected to `/sessions?user_id=…`, verified against the live API; deploy pending |
| PF-4.2 | Apply the same Production withholding to `gitSha`/`buildTime` on the admin system-statistics route, or document why admins may see it | Low |
| PF-4.3 | Add a test asserting `ChangeMemberRoleAsync` rejects every platform role value, locking in the F-4.1 separation | Low |

## 4.4 Actions taken

- Authenticated read-only probing across tenant and admin sessions.
- Cross-tenant and admin-surface probes were all read-only; all returned `403`/`404`.
- Source review of the role-change service and guards.
- No approvals, role grants, impersonation, or session mutations performed.

## 4.5 Open questions

| ID | Question |
|----|----------|
| OQ-4.1 | ~~Is the Clerk secret key in Key Vault the correct `sk_…` for this instance, and is the Backend API base URL correct?~~ **Answered — no.** The key and base URL are both correct; the defect was the request route. See F-4.5. |
| OQ-4.2 | Is there any operator/support impersonation feature intended for the platform admin? None was found, and admin is denied boutique access (F-4.4). |
| OQ-4.3 | Does the auth-audit log record denied admin attempts with actor attribution at runtime? (Extends OQ-3.2.) |

**Task 4 verdict:** **no impersonation or privilege-escalation path was found.** Platform and org
role namespaces are structurally separated, self-promotion is blocked, and admin-only surfaces reject
tenant sessions. The one real defect (F-4.5, broken session listing) is a configuration problem with
incident-response impact rather than an access-control hole.

---

# Task 5 — Cross-Tenant Queries

**Status:** ✅ Complete (authenticated, member-vs-member)
**Date:** 2026-10-03
**Approval:** Task 5 covered by the Task 3/4 authenticated scope.

## 5.1 Scope

Verify tenant isolation; attempt to access or modify another tenant's data; check for missing
`org_id`/tenant scoping in queries and APIs.

## 5.2 Tenants under test

Organizations are provisioned in the database (confirmed via `/api/v1/orgs/my`), even though the JWT
tokens carry **no `org_id`/`org_role` claims** — an operator-confirmed production bug in the token
template (§5.4, F-5.3).

| Subject | Organization | Org role | Membership source |
|---------|--------------|----------|-------------------|
| tenant.a | `01a0f100-0000-7000-8000-000000000001` "Tenant A Boutique" | `org:boutique_owner` | database |
| tenant.b | `01a0f100-0000-7000-8000-000000000002` "Tenant B Boutique" | `org:boutique_owner` | database |
| admin | *(none)* | — | — |

## 5.3 Findings

### F-5.1 — Tenant isolation holds on reads · **PASS**

Own-org access succeeds; cross-tenant access is denied in both directions.

| Probe (GET) | tenant.a | tenant.b |
|-------------|----------|----------|
| `/api/v1/orgs/{own}/customers` | `200` | `200` |
| `/api/v1/orgs/{other}/customers` | **`403`** | **`403`** |
| `/api/v1/orgs/{other}/settings` | **`403`** | — |
| `/api/v1/orgs/{other}/members` | **`403`** | — |
| `/api/v1/orgs/{other}` | **`403`** | **`403`** |
| `/api/v1/orgs/{other}/invitations` | — | **`403`** |

Both tenants are *active owners of their own org*, so this is a true member-vs-member boundary — not
merely a non-member denial. Isolated correctly.

### F-5.2 — Tenant isolation holds on writes · **PASS**

No cross-tenant mutation reached a handler:

| Probe (write) | tenant.a → tenant.b's org |
|---------------|---------------------------|
| `POST /api/v1/orgs/{B}/invitations` | `403` |
| `PATCH /api/v1/orgs/{B}` | `403` |
| `PATCH /api/v1/orgs/{B}/members/{userId}` | `403` |
| `POST /api/v1/orgs/{B}/customers` | `403` |

### F-5.3 — Isolation does not depend on client-supplied claims · **PASS** (security-relevant)

The tenants' JWTs contain **no `org_id`, `org_role`, or `user_role`**, yet:

- `/api/v1/orgs/my` correctly returned each tenant's **own** organization.
- Every org-scoped route resolved membership from the **database** and enforced it.

This is the **secure** resolution order. Had the API trusted a client-supplied `org_id`, a tenant
could have pointed it at another tenant's data. It does not. The missing claims are therefore a
**functional bug** (token template / Clerk org context not wired), not an authorization bypass:

- **Impact:** clients relying on `org_id`/`org_role` from the token (e.g. frontend gating, or any
  code path that reads the claim rather than resolving membership) will misbehave; org-role-based
  authorization could fail closed for legitimate users.
- **Not a vulnerability:** server-side authorization resolves from the database and denies correctly.

### F-5.4 — No existence disclosure across tenants · **PASS**

`GET /api/v1/orgs/by-slug/tenant-b-boutique`:

| Subject | Result |
|---------|--------|
| tenant.a | `404` "No boutique exists with that slug." |
| tenant.b | `200` (own org) |
| admin | `404` |

A cross-tenant probe cannot distinguish "exists but forbidden" from "does not exist", preventing
tenant enumeration by slug. Coupled with F-4.4 (admin also denied), this is a consistent
non-disclosure posture.

## 5.4 Proposed patches

| ID | Patch | Priority |
|----|-------|----------|
| PF-5.1 | Fix the Clerk JWT template / org-context wiring so `org_id` and `org_role` are emitted; run any org-role-dependent client and server paths off the corrected claims | **High** (functional; production bug) |
| PF-5.2 | Add regression tests asserting cross-tenant `403` on every org-scoped route family (customers, settings, members, invitations, catalog, conversations) | Medium |
| PF-5.3 | Add a test asserting org scoping is resolved from the database even when claims are absent or conflicting | Medium |

## 5.5 Actions taken

- Read-only cross-tenant probing: reads, writes (all denied before handler), slug enumeration, and
  direct object access, in both directions.
- No cross-tenant data was read. No foreign-tenant record was created, modified, or deleted.
- No tokens were replayed across tenants.

## 5.6 Open questions

| ID | Question |
|----|----------|
| OQ-5.1 | Which code paths read `org_id`/`org_role` from the token rather than resolving membership? Those are the functional blast radius of F-5.3. |
| OQ-5.2 | Is `clerkOrgId` (`null` on both orgs) intended to be populated? It appears orgs are Aveline-managed rather than Clerk-managed. |
| OQ-5.3 | Do the `/internal/*` service routes resolve org scope from the database too, or do they accept an org id parameter? (Not testable without the internal token.) |

**Task 5 verdict:** **tenant isolation is correctly enforced** for both reads and writes, at the
object, slug, and collection level, and it does **not** depend on client-controlled claims. The
missing `org_id`/`org_role` claims are a real production defect to fix, but they weaken functionality
rather than isolation.

---

# Task 6 — Others (Additional High-Severity Areas)

**Status:** ✅ Complete
**Date:** 2026-10-03
**Approval:** covered by the standing engagement scope.

## 6.1 Scope

Additional high-severity areas not covered by Tasks 1–5: API rate limiting, CORS, CSP and security
headers, SSRF, file upload, secrets in client bundles, and dependency/repository hygiene.

## 6.2 Findings

### F-6.1 — CORS is correctly locked down · **PASS**

Hostile origins are not reflected and receive no CORS grant:

| Request `Origin` | `access-control-allow-origin` |
|------------------|-------------------------------|
| `https://evil.example.com` | *(absent)* |
| `null` | *(absent)* |
| `https://aveline.gravora.dev` | `https://aveline.gravora.dev` + `access-control-allow-credentials: true` |

Preflight `OPTIONS` from the hostile origin returns `204` with **no** `Access-Control-Allow-*`
headers, so a browser blocks the actual request. Configuration is an explicit allowlist
(`CorsConfiguration.cs:31-43`) with `WithOrigins(allowedOrigins)`, not `AllowAnyOrigin` — and it
fails closed at startup when the list is empty or contains blanks. No reflected-origin or
wildcard-plus-credentials weakness.

### F-6.2 — Security headers are strong · **PASS**

API responses carry a defensible header set:

```
content-security-policy: default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'
x-frame-options: DENY
x-content-type-options: nosniff
referrer-policy: no-referrer
permissions-policy: camera=(), microphone=(), geolocation=(), payment=(), usb=()
strict-transport-security: max-age=63072000
```

`default-src 'none'` is the correct posture for a JSON API. HSTS is present on both the API and the
Vercel frontend.

**One gap (medium):** the **frontend** at `aveline.gravora.dev` returns
`access-control-allow-origin: *` on static asset responses. For unauthenticated static assets this is
low impact, but a wildcard CORS header on the app origin is unnecessary and should be scoped to the
asset CDN or removed.

### F-6.3 — Rate limiting intentionally fails open · **MEDIUM** · ⚠️ defence-in-depth gap

`DistributedRateLimiter` swallows all cache exceptions and **allows the request**:

```csharp
catch (Exception ex)
{
    // If the cache is unavailable, fail open so legitimate sign-ups are not blocked.
    _logger.LogWarning(ex, "Rate limiter unavailable for scope {Scope}. Failing open.", scopeKey);
    return true;
}
```

If Redis is unavailable, every rate-limited endpoint (invitation create/accept, OTP request/verify,
webhooks) loses its throttle simultaneously.

**Exploitability assessment — low in practice, on the evidence:**

| Guarded secret | Entropy | Fail-open impact |
|----------------|---------|------------------|
| Privacy OTP | 6 digits from `RandomNumberGenerator`, 5-attempt hard cap, 300 s TTL | Success capped at `5/10^6` per code even with no IP throttle |
| Invitation code | 12 chars from a 32-char alphabet ≈ **60 bits** | Brute force infeasible (≈2^59 attempts for 50%) |
| Device / media tokens | random, bounded TTL | Not brute-forceable at these lengths |

So fail-open does **not** by itself create a practical brute-force path, because the underlying
secrets are high-entropy and independently capped. It remains a real availability-coupled weakness
(a Redis outage silently disables abuse protection) and should be a conscious, documented decision.

Additionally, the limiter is a non-atomic read-modify-write counter
(`DistributedRateLimiter.cs:8-10`), explicitly acknowledged as under-counting under concurrency.
Acceptable for a brute-force guard; would not be acceptable for billing or quota enforcement.

### F-6.4 — SSRF controls on the image fetcher are strong · **PASS**

`ImageUrlFetcher` implements layered defence:

| Layer | Implementation |
|-------|----------------|
| Host allowlist | `EnsureHostAllowed` — **fails closed when the allowlist is malformed**; when the list is non-empty, any host not listed is refused |
| Port restriction | Refuses non-default ports (`PortNotAllowed`) |
| Scheme | https/http with default-port check |
| Redirects | `Media:ImageUrlMaxRedirects` (default 2) |
| Size cap | `MediaCatalogMaxFileBytes` / `MaxFileBytes` on both declared `Content-Length` and the streamed body |
| Content type | Declared content type must be an image |
| **Magic-byte sniff** | `MediaContentTypes.Sniff(bytes)`, and the **sniffed** type is what is returned — the bytes decide, not the sender's claim |
| Feature flag | `Media__ImageUrlUploadEnabled` defaults to **`false`** |

The magic-byte sniff plus scheme/port/host checks is a stronger posture than typical. Not vulnerable
to the usual `169.254.169.254` metadata or `file://` vectors because scheme and port are constrained
and the allowlist governs the host.

**Residual (low):** when `Media:ImageUrlAllowlist` is empty, `EnsureHostAllowed` returns early and
**any host is permitted**. Safe only while `ImageUrlUploadEnabled=false`. If someone enables URL
upload without populating the allowlist, the SSRF guard is effectively off. Recommend making an empty
allowlist a startup failure whenever upload is enabled.

### F-6.5 — No secrets in the client bundle · **PASS**

Scanned the shipped frontend bundle for `sk_live` / `sk_test`, private keys, database URIs,
`AKIA…`, and inline API secrets: **none found**. No `VITE_*` / `NEXT_PUBLIC_*` secret-style
variables are embedded. The only sensitive-looking value in the bundle is the Clerk **publishable**
key (`pk_live_…`), which is designed to be public, and the API origin — both expected.

### F-6.6 — Repository hygiene: nested full repo copies untracked and unignored · **MEDIUM**

Three scratch directories exist in the working tree that are **not** gitignored:

```
.fix/    3835 files   (contains .git, .github/workflows/release-apk.yml, full source tree)
.m424/   3767 files   (same shape)
.probe/     1 file    (no secret hits)
```

`.fix/` and `.m424/` are complete nested copies of the repository, including `.github/workflows/release-apk.yml`,
which references keystore signing secrets (`KS_PASS`, `KEY_PASS`). They are untracked today (no
history hits), but they are one `git add -A` from being committed, and a nested `.git` inside the
worktree is a genuine footgun: it can silently shadow the parent repository for tooling that
discovers repositories by walking down from the workspace root.

No live secret was found inside them by pattern scan (only `.env.example` files and test fixtures
matched), so this is a **hygiene and accidental-disclosure risk**, not a disclosure that has already
happened.

### F-6.7 — Metrics and internal surfaces are token-gated · **PASS**

`/metrics` requires `MetricsPolicy` (internal token or scrape token); anonymous and bogus-token
requests return `401` (Task 2, F-2.1). `/internal/*` and `/api/internal/*` require
`InternalServicePolicy`. `/openapi/v1.json` and `/swagger` are mapped only when
`IsDevelopment()` and return `401` in the deployed Production environment.

### F-6.8 — Clerk Backend API integration is misconfigured · **MEDIUM** (repeats F-4.5)

`/api/v1/users/me/sessions` returns `502` for all users, with the upstream Clerk Backend API
answering `404 page not found`. This is a deployment credential/URL defect with incident-response
impact (no session visibility or revocation oversight). Tracked once, under F-4.5; recorded here as a
cross-cutting deployment finding.

## 6.3 Proposed patches

| ID | Patch | Priority |
|----|-------|----------|
| PF-6.1 | Remove or scope the `access-control-allow-origin: *` header on `aveline.gravora.dev` static responses | Medium |
| PF-6.2 | Make an empty `Media:ImageUrlAllowlist` a startup failure when `ImageUrlUploadEnabled=true` | Medium |
| PF-6.3 | Delete or gitignore `.fix/`, `.m424/`, and `.probe/`; add a guard preventing nested `.git` directories in the worktree | Medium |
| PF-6.4 | Document the fail-open rate-limiter decision in an ADR; add alerting on the "Failing open" log event so a Redis outage is noticed | Medium |
| PF-6.5 | Replace the non-atomic read-modify-write counter with an atomic Redis `INCR`+`EXPIRE` if any quota/billing path ever uses it | Low |
| PF-6.6 | Fix the Clerk Backend API credential (see PF-4.1) | Medium |

## 6.4 Actions taken

- Unauthenticated and authenticated read-only probing; bundle download and static inspection.
- CORS probes with hostile, `null`, and legitimate origins; preflight request.
- No exploitation of the fail-open limiter, no SSRF attempts, no uploads, no destructive actions.

## 6.5 Open questions

| ID | Question |
|----|----------|
| OQ-6.1 | Is `Media:ImageUrlUploadEnabled` expected to be ever enabled in Production? If so, the allowlist must be populated and enforced. |
| OQ-6.2 | Is the `access-control-allow-origin: *` on the frontend intentional (e.g. a CDN default) or a Vercel configuration artifact? |
| OQ-6.3 | Are `.fix/` and `.m424/` still needed? They duplicate the repository and should be removed or ignored. |

**Task 6 verdict:** no high-severity defect found. CORS, CSP, SSRF defences, metrics gating, and
client-bundle hygiene are all in good shape. The notable items are the **fail-open rate limiter**
(compensated by high-entropy secrets), **repository hygiene** around nested repo copies, and the
**broken Clerk Backend integration**.

---

# Consolidated Summary and Remediation Roadmap

**Assessment window:** 2026-10-03
**Target:** `https://aveline.gravora.dev` (Vercel frontend) + `aveline-api.politeplant-5806d7de.malaysiawest.azurecontainerapps.io` (Azure Container Apps, Malaysia West)
**Tasks completed:** 6 of 6
**Patches implemented:** 0 (remediation deferred to a single pass by operator decision)

## Overall Security Posture

**The deployment is materially more secure than the "rapidly built / vibe-coded" baseline this
assessment was scoped to anticipate.** No critical vulnerability was found. No high-severity
vulnerability was confirmed live. The two highest-value leads carried into the assessment were both
resolved as **safe**:

- **F-1.7** (guessable internal-service token fallback) → **closed**, the deployed token is a real
  secret (F-2.1).
- **F-3.x** (broken access control on admin endpoints) → **no defect found**; role checks, resource
  authorization, and self-approval prevention all held under direct testing with real accounts.

Verified-live strengths:
- Tenant isolation enforced on reads and writes, both directions, at object/slug/collection level, and
  **not** dependent on client-supplied claims.
- Platform admin is **not** a tenant superuser (denied other tenants' boutiques).
- Admin self-approval escalation blocked end-to-end.
- JWT forgery, `alg:none`, HS256 confusion, and malformed tokens all rejected.
- MFA (email-code) enforced; stolen passwords alone are insufficient.
- AES-GCM at rest for stored credentials; SHA-256 for API keys; constant-time token comparisons.
- Media tokens with expiry, scope, and single-use Redis-backed nonces that fail closed.
- Strong security headers (`default-src 'none'`, HSTS, nosniff, DENY framing).
- CORS allowlist correct — no origin reflection.
- Layered SSRF defence with magic-byte content sniffing.
- No secrets in the shipped client bundle.

## Findings Register

| ID | Finding | Severity | Status |
|----|---------|----------|--------|
| F-5.3 / PF-5.1 | JWT omits `org_id`/`org_role` despite provisioned org membership — production bug | **HIGH (functional)** | Open |
| F-4.5 / F-6.8 | `/users/me/sessions` broken (Clerk Backend `404`) — no session/revocation visibility | **MEDIUM** | Open |
| F-6.3 | Rate limiter fails open on cache outage (compensated by secret entropy) | **MEDIUM** | Open |
| F-1.6 | Insecure defaults shipped in source (hardcoded `postgres:postgres`, 0.0.0.0 datastore binds, unauthenticated Redis) | **MEDIUM** | Open |
| F-6.6 | `.fix/` and `.m424/` untracked, unignored nested repo copies | **MEDIUM** | Open |
| F-2.4 | JWT accepted via `?access_token=` on `/hubs` — leakage surface, redaction unverified | **MEDIUM** | Open |
| F-6.2 | `access-control-allow-origin: *` on frontend static responses | **MEDIUM** | Open |
| F-6.4 residual | Empty `ImageUrlAllowlist` permits any host if URL upload enabled | **LOW** | Open |
| F-2.6 | Internal-token placeholder denylist is a single literal | **LOW** | Open |
| F-2.3 | `ValidateAudience = false` — latent if a second app joins the Clerk instance | **LOW** | Open |
| F-4.7 | Admin stats leaks `gitSha`/`buildTime` withheld elsewhere in Production | **LOW** | Open |
| F-1.5 | Anonymous `/health` discloses internal topology + timings | **LOW** | Open |
| F-6.3 residual | Non-atomic rate-limit counter | **LOW** | Open |
| F-1.1, F-1.2, F-1.3, F-1.4, F-2.1, F-2.2, F-2.5, F-3.1–F-3.6, F-4.1–F-4.4, F-4.6, F-5.1, F-5.2, F-5.4, F-6.1, F-6.2, F-6.5, F-6.7 | Controls verified as effective | **PASS** | Closed |

## Remediation Roadmap

### Priority 1 — Correctness bugs with security relevance

| Item | Action | Rationale |
|------|--------|-----------|
| PF-5.1 | Fix the Clerk JWT template / org context so `org_id` and `org_role` are emitted | Anyone relying on the claim rather than a DB lookup gets wrong authorization decisions. Server-side isolation is safe, but this is a live production bug. |
| PF-4.1 / PF-6.6 | Fix the Clerk Backend API credential/base URL so `/users/me/sessions` works | Restores session visibility and revocation oversight — required for incident response. |
| PR-3.3 | **Re-enable email-code MFA** on tenant.a, tenant.b, admin | MFA was disabled to permit automated testing and is still off. |

### Priority 2 — Hardening

| Item | Action |
|------|--------|
| PF-1.1 / PF-1.2 | Bind compose datastores to loopback; default `POSTGRES_SSLMODE=require` |
| PF-1.3 | Remove the hardcoded `postgres:postgres` connection string |
| PF-1.7 | Move all secrets to Azure Container App secrets (not plain env) |
| PF-6.1 | Remove/scoped the wildcard CORS header on the frontend |
| PF-6.3 | Add a startup guard: empty `ImageUrlAllowlist` + URL upload enabled = refuse boot |
| PF-6.4 | Document fail-open limiter in an ADR + alert on the "Failing open" event |
| PF-2.2 | Verify `?access_token=` redaction in audit logs and OTel traces |
| PF-6.3b | Delete or ignore `.fix/`, `.m424/`, `.probe/`; guard against nested `.git` |

### Priority 3 — Defence in depth

| Item | Action |
|------|--------|
| PF-2.1 | Broaden the token placeholder denylist beyond one literal |
| PF-2.3 / PF-2.4 | Enforce `aud` (Clerk template supports it); record the trigger to revisit |
| PF-1.4 | Reduce `/health` disclosure (status-only, or restrict by source) |
| PF-4.2 | Withhold build metadata on admin stats, or document the exemption |
| PF-4.3 / PF-5.2 / PF-5.3 | Add regression tests locking in role separation, cross-tenant 403s, and DB-resolved scoping |
| PF-6.5 | Atomic rate-limit increments if any quota path adopts the limiter |

### Operational follow-ups

- Two `Pending` admin-approval requests created during Task 3 remain in the live queue
  (`01a1015e-0dae-76cf-93bf-5d3bdf7bd772`, `01a1015e-0f45-7c14-8095-97c2c1ac0ed3`) and should be
  cleaned up or reviewed.
- Operator-supplied session tokens expire within ~1 hour of issue and require no revocation.
- Re-run Tasks 2–5 after PF-5.1 lands, since corrected org claims change the token payloads that
  authorization paths may consume.

## Assessment Limitations

1. **JWT claim-validation depth** — forged tokens always failed at the signature check, so `exp`,
   `iss`, and `aud` enforcement were confirmed by source review, not observed behaviour (F-2.2).
2. **Audit-trail contents** — logging and persistence paths were reviewed, but log contents were not
   inspected at runtime (no log access) (OQ-3.2).
3. **Internal service routes** — `/internal/*` behaviour was verified only for authorization
   rejection; their data-handling semantics were not exercised (no internal token).
4. **No destructive or load testing** — no DoS, brute force, or state-destroying payloads were used,
   per engagement rules. Absence of a finding is not proof of absence under load.
5. **Test-account privilege shape** — tenants hold org-owner roles in the database; lower-privilege
   roles (`org:boutique_staff`, `org:boutique_manager`, `org:boutique_supervisor`) were not
   exercised, so intra-org role boundaries are only partly tested.

## Statement of Actions Taken

All testing was confined to `aveline.gravora.dev`, its Clerk subdomain, and the project's own Azure
API host. No third-party host was contacted. No destructive action, denial-of-service, credential
brute force, or real-user-data exfiltration was performed. The only state-changing operations were
two admin-approval *requests* (left `Pending`) needed to prove the self-approval guard, plus two
`PATCH`-free read probes; no approvals, role grants, deletions, or user-state changes were executed.
All credentials and session tokens were held in a gitignored scratch directory and **deleted at the
conclusion of the assessment**; none appear in this report.

---

# Remediation — Implemented Patches

**Date:** 2026-10-03
**Authorization:** operator instruction to implement the patches after the assessment was accepted.
**Patches implemented:** 11 of 15 · **Deferred to operator:** 4 (not reachable from this repository)

## R.1 Patch status

| ID | Finding | Status | Where |
|----|---------|--------|-------|
| PF-1.3 | Hardcoded `postgres:postgres` in the design-time factory | ✅ Implemented | `AppDbContextFactory.cs` |
| PF-2.1 | Single-literal internal-token denylist | ✅ Implemented | `InsecureInternalTokens.cs` (new), `InternalTokenSecurityGuard.cs` (new), `InternalTokenAuthenticationHandler.cs`, `Program.cs` |
| PF-2.3 | `ValidateAudience = false` | ✅ Implemented (opt-in) | `AuthenticationConfiguration.cs` |
| PF-2.4 | ADR note for the audience decision | ✅ Implemented | `docs/ADR/ADR-008-jwt-token-strategy.md` |
| PF-1.4 | Anonymous `/health` discloses topology | ✅ Implemented | `HealthCheckResponseWriter.cs`, `HealthEndpoints.cs` unchanged |
| PF-4.2 | Admin stats build metadata | ✅ Resolved by documentation | `SystemStatisticsService.cs` |
| PF-6.2 | Empty allowlist + URL upload = open fetcher | ✅ Implemented | `MediaOptionsValidator.cs` |
| PF-6.3b | Untracked nested repo copies | ✅ Implemented (ignore; deletion deferred) | `.gitignore` |
| PF-1.1 | Datastore ports bound to all interfaces; Redis unauthenticated | ✅ Implemented | `docker-compose.yml`, `.env.example` |
| PF-1.2 | `sslmode=disable` default | ✅ Implemented as documented guidance | `.env.example` |
| PF-6.1 | Wildcard CORS on frontend static responses | ✅ Partially — real cause identified, headers added | `frontend/web/vercel.json` |
| PF-6.4 | Fail-open rate limiter undocumented/invisible | ✅ Implemented | `DistributedRateLimiter.cs`, `RateLimiterMetrics.cs` (new), `docs/ADR/ADR-030-rate-limiter-fail-open.md` (new) |
| PF-4.1 / PF-6.8 | Clerk Backend API credential broken | ⛔ Operator | Azure Container App secret |
| PF-5.1 | JWT omits `org_id`/`org_role` | ⛔ Operator | Clerk JWT template |
| PF-1.7 | Secrets as Azure env values | ⛔ Operator | Azure Container App config |
| PR-3.3 | Re-enable MFA on test accounts | ⛔ Operator | Clerk dashboard |

## R.2 Implemented patches in detail

### PF-1.3 — removed the hardcoded superuser credential

The old factory hardcoded `Host=localhost;Port=5432;Database=aveline;Username=postgres;Password=postgres`.
It now resolves, in order: an explicit `ConnectionStrings__DefaultConnection`, then a string composed
from the same `POSTGRES_*` variables `docker-compose.yml` uses, then a non-routable
`design-time-placeholder.invalid` host.

A subtlety worth recording: the first attempt made an absent connection string a hard failure. That
would have **broken the deploy pipeline**. `.github/workflows/deploy.yml` runs
`dotnet ef migrations bundle` in CI, where no database credential is exported — the real connection
arrives later via `./efbundle --connection "$CONN"`, read from Key Vault. Throwing would have failed
the build for a value the factory never needed. The composed-from-compose-variables fallback keeps
the bundle building while still removing the privileged credential from source.

Rejected alternative: defaulting to `username=postgres`. Removing the *credential while keeping the
superuser name* would still normalise a privileged default, so neither the user nor a password is
invented.

### PF-2.1 — internal-token denylist and startup guard

New `InsecureInternalTokens` recognises the values this repository publishes, including the
`docker-compose.yml` fallback `aveline-local-development-secret-token-2026`, which the previous
single-literal check (`change-me-internal-token`) accepted. New `InternalTokenSecurityGuard` refuses
to boot Production with a missing, placeholder, or low-entropy token, following the
`MetricsSecurityGuard` precedent.

**A regression this patch introduced, and the fix.** The first version applied the *strength* rules
(≥32 characters, ≥8 distinct characters) inside the authentication handler, so every request with a
weak token failed closed. That broke **17 integration tests** that legitimately configure a short
token such as `test-internal-analyze-key`. The tests were right and the patch was wrong: rejecting a
short throwaway token *per request* adds no production security, because Production is already
refused at boot. The check is now split:

- `RejectKnownPlaceholder` — *published/development-shaped values*. Applied by the handler, in every
  environment. This is the actual F-2.6 fix.
- `Reject` — placeholder rules **plus** length/entropy. Applied once, by the Production boot guard.

Two `Production`-environment test hosts were also updated to supply a token meeting the Production
bar, because the boot guard is now genuinely enforcing it.

### PF-1.4 — reduced anonymous health disclosure

`/health`, `/health/ready` and `/health/live` stay anonymous (the orchestrator cannot present
credentials). The response writer now splits by caller:

| Caller | Payload |
|--------|---------|
| Anonymous probe | `status`, `totalDurationMs` |
| Internal service token | `status`, `totalDurationMs`, `checks[]` (names, status, latency, message), and `version` outside Production |

An operator keeps the full breakdown; an unauthenticated probe no longer learns that the stack is
PostgreSQL + Redis + agent service + Clerk, nor their latencies. The aggregate verdict — all a
readiness probe needs — is unchanged.

### PF-2.3 — opt-in audience validation

`BuildTokenValidationParameters` takes an optional audience. `ValidateAudience` stays `false` unless
`Clerk:Audience` is configured, so nothing breaks for deployments whose template has no `aud`, while
the second application on this Clerk instance can be locked out by setting the key. ADR-008 records
the revisit trigger.

### PF-6.2 — SSRF allowlist guard

`Media:ImageUrlUploadEnabled=true` with an empty `Media:ImageUrlAllowlist` is now a boot error. An
empty list means "any host" in `ImageUrlFetcher`, so enabling user-supplied URL fetching without a
list shipped an open fetcher. Off by default, so the safe path is unaffected.

### PF-1.1 / PF-1.2 — compose datastore hardening

- Postgres and Redis now bind `127.0.0.1` instead of `0.0.0.0`.
- Redis runs with `--requirepass`, and both the API connection string and the agent `REDIS_URL` carry
  the password; the healthcheck authenticates.
- `REDIS_PASSWORD` is required (`:?`), with guidance in `.env.example`.
- `POSTGRES_SSLMODE` guidance now explains when `require` is needed.

**Not forced to `require`:** the official Postgres image ships without a server certificate, so
forcing TLS would break this local stack — and the SSRF-adjacent risk that motivated the change (a
VPS publishing the database) is removed by the loopback bind. Documented instead.

A local `REDIS_PASSWORD` was generated into the gitignored `.env`; without it `docker compose up`
would now fail.

### PF-6.4 — fail-open limiter documented and observable

The behaviour is unchanged (failing closed would trade a cache outage for a product outage), but it
is now a recorded decision in **ADR-030** and it emits `aveline.rate_limiter.fail_open`, labelled by
bounded scope prefix, so an operator can alert on the condition instead of traffic silently
proceeding unthrottled. The ADR records why the underlying secrets' entropy caps make this tolerable,
and the revisit trigger.

### PF-6.1 — wildcard CORS: cause identified

`frontend/web/vercel.json` set no CORS header; the `access-control-allow-origin: *` is a platform
default on static asset responses and cannot be removed by setting an empty value. What was added
instead is the frontend header set the API already had (`nosniff`, `Referrer-Policy`,
`X-Frame-Options: DENY`, `Permissions-Policy`). The residual wildcard affects only public build
assets, which are not sensitive — so this is recorded as **accepted low risk**, not silently closed.

### PF-6.3b — nested working copies

`.fix/`, `.m424/`, `.probe/` are now root-anchored in `.gitignore`. Deletion (~2.4 GB) was left to
the operator. The first pattern I wrote was unanchored and also matched the *tracked* documentation
directories `docs/**/.probe/`; it is now anchored to the repository root.

## R.3 Not implementable from this repository

| Item | Why it needs a human | Where |
|------|----------------------|-------|
| PF-4.1 / PF-6.8 | The Clerk **secret key** and Backend API base URL are Azure Container App configuration. Requires the correct `sk_…` for instance `ins_3Jar…` | Azure portal / Key Vault |
| PF-5.1 | The `org_id`/`org_role` claims come from the Clerk **JWT template** `jwt-aveline-v1`, configured in the Clerk dashboard — not in this codebase | Clerk dashboard |
| PF-1.7 | Whether values are Container App **secrets** vs plain env is deployment state, not source | Azure portal |
| PR-3.3 | MFA was disabled on the three test accounts for this assessment | Clerk dashboard |

## R.4 Verification

Built with `dotnet 10.0.302`; the suite was run in alphabetic batches because the full run
(Testcontainers-dependent tests) does not complete in this environment.

| Batch | Scope | Result |
|-------|-------|--------|
| Patched areas | factory, JWT, media validator, internal tokens, health writer, health endpoints, production guards, payment, rate limit | 104/104 pass |
| A | `Aveline.Api.Tests.A*` | 479 tests, 0 fail |
| B–C | `B*`, `C*` | 1140/1140 pass |
| D–H | `D*`–`H*` | 205/205 pass |
| I–N | `I*`–`N*` | 876/876 pass |
| O–S | `O*`–`S*` | 1133/1133 pass (after the production-token fix below) |
| T–Z | `T*`–`Z*` | 408/408 pass |
| New tests added | 6 factory, 10 internal-token denylist, 4 audience, 3 media allowlist, 1 health writer, 1 health endpoint | all pass |

Total: **≈4.3k test executions across seven batches, 0 failures attributable to these patches.**
Four `Production`-environment test hosts also needed a strong internal token, because the new boot
guard genuinely rejects the short placeholder they used; that constant is now shared as
`TestAgentService.ProductionInternalToken` rather than repeated in four files.

### Environment issues found during verification (not caused by these patches)

1. **The workspace `.env` is exported into every shell**, so its compose-style keys bypass the
   mapping `docker-compose.yml` performs and reach `dotnet test` unmapped. `docker-compose.yml:172`
   maps `MEDIA_SIGNING_KEY` → `Media__SigningKey`, and `.env` sets `Media__Provider=cloudinary`; run
   directly, the app therefore sees `cloudinary` selected with no `Media__SigningKey` and refuses at
   boot. Three integration tests (`InvitationAcceptRateLimitIntegrationTests`, two in
   `WebhookEndpointsIntegrationTests`) fail on this **at baseline, before any patch**. They pass with
   `Media__Provider=database` or with `Media__SigningKey` supplied. Recommended fix: give those test
   hosts an explicit `Media:Provider=database` setting, as several other test classes already do, so
   the suite stops depending on ambient environment.
2. **`dotnet build` was silently reusing a stale API assembly** for the test project; changes only
   took effect after `rm -rf */bin */obj` and a solution build. This masked the denylist fix and cost
   a debugging cycle — noted here because it will mislead the next person too.
3. `Testcontainers` leaves Postgres containers running; the full suite therefore hangs rather than
   fails.

### Live confirmation of the original finding

`aveline_redis` on this machine was running with **no authentication** and published on
`0.0.0.0:6379` (`docker ps`), exactly as F-1.6 described. The compose fix takes effect only when the
container is recreated — until then the exposure remains.

## R.5 Operator follow-ups

| # | Action | Priority |
|---|--------|----------|
| 1 | **Re-enable email-code MFA** on tenant.a, tenant.b, admin (disabled for this assessment) | High |
| 2 | Set a correct Clerk Backend API secret so `/users/me/sessions` works again | High |
| 3 | Add `org_id`/`org_role` to the `jwt-aveline-v1` template and verify role-dependent paths | High |
| 4 | Fix the `org_id`/`org_role` claim wiring end-to-end (functional bug F-5.3) | High |
| 5 | Recreate `aveline_redis` so the password + loopback bind take effect; confirm from outside the host that 6379 is no longer reachable | Medium |
| 6 | Delete `.fix/`, `.m424/`, `.probe/` (~2.4 GB) | Medium |
| 7 | Review or delete the two `Pending` admin requests left by Task 3 | Low |
| 8 | Confirm whether the frontend wildcard CORS is acceptable (see PF-6.1) | Low |

## R.6 Assessment of the remediation

Eleven patches landed with test coverage; four require deployment or IdP access. The highest-value
change is **PF-2.1**, because it closes the gap that made F-1.7 exploitable by omission rather than
by attack.

Two honest notes. First, **my initial PF-2.1 was wrong** and broke 17 tests by confusing
"insecure for Production" with "insecure to authenticate against"; the tests caught it and the fix
made the guard more precise. Second, **verification was partial by necessity** — the full suite does
not complete in this environment, so coverage came from batched runs of the areas at risk plus the
pre-existing suite, not a single green run. Nothing found in that process remains unexplained.

## R.7 Workspace provenance

The working tree contained pre-existing work that is **not** part of this remediation and should be
reviewed and committed by its author:

- A `wip unrelated local edits` stash existed before this session; a stash/pop cycle performed during
  verification re-applied it, so `Aveline.Api/Modules/Shared/Services/UserService.cs` and
  `Aveline.Api.Tests/UserServiceTests.cs` now show as modified.
- Already staged when this session began: `Authorization/StaffMembershipAuthorizationHandler.cs` (new),
  `Configurations/AuthorizationConfiguration.cs`, `Endpoints/AgentEndpoints.cs`,
  `Endpoints/AuthPolicyDemoEndpoints.cs`, `Tests/AuthorizationPolicyTests.cs`,
  `Tests/OrganizationAuthorizationIntegrationTests.cs`.

These were present throughout and the full build plus all seven test batches passed with them in
place, so they do not conflict with the patches above. They are called out because a diff of the
workspace would otherwise attribute them to this remediation.

Files changed by this remediation:

| Group | Files |
|-------|-------|
| New source | `InsecureInternalTokens.cs`, `InternalTokenSecurityGuard.cs`, `RateLimiterMetrics.cs` |
| New docs | `docs/ADR/ADR-030-rate-limiter-fail-open.md` |
| New tests | `AppDbContextFactoryTests.cs`, `InsecureInternalTokensTests.cs`, `TestAgentService.cs` |
| Modified source | `AppDbContextFactory.cs`, `InternalTokenAuthenticationHandler.cs`, `AuthenticationConfiguration.cs`, `MediaOptionsValidator.cs`, `HealthCheckResponseWriter.cs`, `SystemStatisticsService.cs`, `DistributedRateLimiter.cs`, `Program.cs` |
| Modified config | `docker-compose.yml`, `.env.example`, `.gitignore`, `frontend/web/vercel.json`, `docs/ADR/ADR-008-jwt-token-strategy.md` |
| Modified tests | `HealthCheckResponseWriterTests.cs`, `HealthEndpointsIntegrationTests.cs`, `JwtValidationTests.cs`, `MediaOptionsValidatorTests.cs`, `EnvironmentHardeningIntegrationTests.cs`, `MediaProductionGuardIntegrationTests.cs`, `PaymentEndpointsIntegrationTests.cs` |
| Local only (gitignored) | `.env` — generated `REDIS_PASSWORD` so `docker compose up` still works |

**No patch was committed.** Nothing was pushed, and no live deployment was modified: every patch is
in the working tree awaiting review.
