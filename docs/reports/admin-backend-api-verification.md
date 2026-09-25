# Admin Backend API — Implementation Verification & Security Review

**Subject:** `feature/admin-backend-api` @ `e5a8f34` (working tree clean except untracked frontend/scratch files)
**Verification against:** `docs/backend/**` (README, backend-requirements, domain-model, statistics-catalog, implementation-plan, assumptions-and-open-questions) and `docs/api/README.md` + `docs/api/openapi.yaml`
**Method:** read every admin/statistics/billing/auth source file listed in *Scope*; built and ran the full suite; ran two standalone C# repros for arithmetic claims; diffed `docs/api/openapi.yaml` against the implemented route table with a script. Four independent workstreams were delegated (endpoint-vs-doc mapping, authn/authz, tenant isolation/injection, billing correctness); every load-bearing claim below was re-read in this session and is cited to `path:line`.
**Date:** 2026-09-13

---

## Answer first

The admin backend is **substantially built and genuinely well-engineered in places, but it does not reflect `docs/backend/`**. Four admin surfaces the documents describe as shipped do not exist at all, the entire pricing subsystem is inert under the committed configuration, and one privilege-escalation path (a Moderator approving their own admin request) is reachable.

Concretely, and with confidence:

| # | Finding | Confidence |
|---|---|---|
| **C-1** | `GET /api/v1/admin/audit`, `GET /api/v1/admin/orgs`, `PATCH /api/v1/admin/orgs/{id}/entitlement-overrides` and the five `/admin/statistics/billing/*` endpoints are **not implemented**. The audit subsystem is write-only — the repository exposes no read method at all, so `docs/api/openapi.yaml`'s `/api/v1/admin/audit` cannot be served. | Confirmed |
| **C-2** | **Privilege escalation:** `Roles.Moderator` holds `AdminReviewPolicy`, any authenticated user may submit an admin request, and `ApproveAsync` never compares reviewer to requester. A Moderator can self-approve and be granted the `admin` role in Clerk (`public_metadata.role = "admin"`), which holds every permission except `pricing:backdate`. | Confirmed (code path read end-to-end) |
| **C-3** | **Blossom under-charge:** the legacy formula sums three `int` token counts before widening, so a workflow reporting ≈2.1 B tokens is billed the **0.1-Blossom minimum** instead of ~3.1 M. The newer rule path uses `long` and is correct, so the two paths disagree. Reproduced at runtime. | Confirmed (executed) |
| **C-4** | **The pricing engine is off by default.** `appsettings.json:18` ships `"Pricing:UseLegacyFormula": true`, so every admin pricing endpoint returns `200/201` while ingestion ignores the rules entirely and never snapshots them. Documented as intentional (implementation-plan.md:881), contradicted by `docs/api/README.md:747` ("Status: implemented"). | Confirmed |
| **C-5** | **9 of the 12 seeded alert rules can never fire.** They reference metric names nothing writes (e.g. `blossom.reconciliation.drift`, `api.error_rate`, `aveline.db.pool_in_use`). The only `SystemMetricSample` producer writes 12 `aveline.*` names. The documented Critical "ledger/projection drift" control is therefore non-functional. | Confirmed |
| **H-1** | Idempotency is optional when the docs say required; two concurrent identical money POSTs both execute, and the loser still returns `201`; a failed request is replayed for 24 h; the uniqueness index omits `HttpMethod`; `organizationId == null` escapes the constraint. | Confirmed |
| **H-2** | `GET /api/v1/admin/users` binds `state` not the documented `accountState`, has no `organizationId` filter, and pages at 20/100 against the documented 50/200 convention. | Confirmed |
| **H-3** | Rule **activation is not atomic** (two separate `SaveChanges`, no transaction) despite BR-1.8 and the docs; the documented `{effectiveFrom}` body, the `pricing.rule.activated` event, and cross-instance cache invalidation are all absent. | Confirmed |
| **H-4** | `/admin/pricing/**` reads are gated by `pricing:view`, which `org:boutique_manager` and `org:boutique_owner` hold, with no org-scope requirement; `price-book/{entryId}` has no org predicate, so one tenant can read another's negotiated price entry. | Confirmed |
| **H-5** | JWT audience validation is disabled and `azp` is never checked; clock skew is the 5-minute default. | Confirmed |
| **M** | Unsalted IP/UA hashing (`Telemetry:IpHashSalt` ships empty), committed default `AgentService:InternalToken`, anonymous `/health` leaking environment + git SHA, WhatsApp webhook replayable indefinitely, no app-level rate limiting, substring role mapping in the Clerk webhook, `/metrics` and `/health` doc drift, no global exception handler, no HSTS/CSP, a demo-policy endpoint mapped in every environment. | Confirmed unless noted |

**Counterweight — what is genuinely correct and should not be re-litigated:** the build is clean (`0 warnings, 0 errors`) and **1 042/1 042 tests pass, including 25 real Testcontainers-PostgreSQL tests**; defect **D-3 is genuinely fixed** (atomic `ExecuteUpdateAsync` on the consumption path, `xmin` optimistic token plus a 5-attempt retry with reload on the adjustment path, proven by `LedgerPostgresTests.TwentyParallelCredits_ProduceTheExactArithmeticSum`); D-1, D-2 and D-12 are fixed with a single entitlement catalog; **no SQL injection sink exists** (every raw-SQL site is parameterised or built only from server values); blob/tenant scoping on org routes is done properly against the membership table rather than JWT claims; API-key generation is a 190-bit CSPRNG secret with constant-time comparison; webhook HMACs are constant-time with a bounded replay window. See §7.

---

## Scope and method

**Inspected (read in full).** `Program.cs`; `Authorization/*`, `Configurations/Authentication*`, `Authorization*`, `SecurityConfiguration`; `Common/Middleware/{Onboarding,CorrelationId}Middleware`; `Endpoints/{Admin,AdminUser,AuthPolicyDemo}`; `Modules/Billing/{Endpoints,Domain,Services,Repositories,DTOs,Models}/*`; `Modules/Statistics/{Endpoints,Services,Repositories,Jobs,Telemetry,Models,Domain}/*`; `Modules/ApiAccess/*`; `Modules/Audit/*`; `Modules/SystemHealth/Endpoints/HealthEndpoints`; `appsettings.json`, `appsettings.Development.json`, `docker-compose.yml`.

**Ran.**
- `dotnet build Aveline.Api/Aveline.Api.csproj` → *Build succeeded, 0 Warning(s), 0 Error(s)*.
- `dotnet test Aveline.Api.Tests/Aveline.Api.Tests.csproj` → *Failed: 0, Passed: 1042, Skipped: 0, Total: 1042, Duration 1 m 33 s*. Docker was available; 25 Postgres-backed tests executed against Testcontainers (e.g. `LedgerPostgresTests.XminConcurrencyToken_RaisesOnStaleUpdate`, `TwentyParallelCredits_ProduceTheExactArithmeticSum`, `ApiConsumptionPostgresTests.*`).
- A route-table diff of `docs/api/openapi.yaml` (104 paths) against every `MapGroup`/`Map*` in `Aveline.Api` (script in §8.1).
- Two standalone `dotnet run` repros outside the repository for the C-3 arithmetic claim (§8.2).

**Not covered.** `agent-service/` (Python) and `frontend/` beyond noting they are out of the stated scope; the Clerk dashboard template `jwt-aveline-v1` (not in the repo, so the exact claim set could not be verified); runtime behaviour of `/metrics` exposition and the `HealthCheckOptions.ResultStatusCodes` default; the Postgres-only claims that the executed tests did **not** cover.

**What would change the answer.** (a) An actual deployment config that sets `Pricing:UseLegacyFormula=false` and a non-empty `Telemetry:IpHashSalt` — findings C-4 and M-1 would downgrade to "configuration risk". (b) A Clerk configuration proof that a Moderator cannot submit an admin request — that would not remove C-2, but a self-approval guard would. (c) Evidence that the four missing endpoints are deliberately deferred — but `docs/backend/README.md:64-65` explicitly claims them shipped.

---

## Findings

### C-1 · Documented admin endpoints are missing, and the audit log cannot be read at all

`docs/backend/README.md:64` and `:65` claim Phase 3 (user/organization administration) and Phase 6 (system statistics) are **implemented**. Four categories of endpoint those phases name do not exist:

| Documented | Documented at | Status |
|---|---|---|
| `GET /api/v1/admin/audit` (+ `/{entryId}`) | `docs/api/README.md:1348`, `:1392`; `implementation-plan.md:352`; `openapi.yaml:3227` | Absent |
| `GET /api/v1/admin/orgs` | `docs/api/README.md:1346`; `backend-requirements.md:488` (FR-4.8) | Absent |
| `PATCH /api/v1/admin/orgs/{id}/entitlement-overrides` | `docs/api/README.md:1347`; `backend-requirements.md:489` (FR-4.9); `openapi.yaml:1700` | Absent |
| `GET /admin/statistics/billing/{profitability,org-usage,adjustments,plan-changes,downgrades}` | `docs/api/README.md:1820-1824`; `implementation-plan.md:350`; `openapi.yaml:3104-3226` | Absent |
| `GET /orgs/{id}/statistics/billing/burn-rate`, `/customers/active`, `/staff/seats` | `docs/api/README.md:1771`, `:1790`; `implementation-plan.md:348-349` | Absent |

The audit gap is structural, not just a missing route. `IAuditRepository` declares exactly one method:

```csharp
public interface IAuditRepository
{
    Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);
}
```
— `Aveline.Api/Modules/Audit/Repositories/IAuditRepository.cs:9-12`

`AuditModule.cs:9-17` registers DI only; there is no `MapGroup` anywhere under `Modules/Audit` (verified: `grep -rn 'MapGet\|MapPost' Aveline.Api/Modules/Audit` → no matches). So audit entries are written and never readable through the API, `audit:view` (`Permissions.cs:48`) is granted to Admin/Owner but referenced by no endpoint, `AuditViewPolicy` (`AuthorizationConfiguration.cs:79`, `:189`) is registered and never applied, and `admin:orgs:read` (`Permissions.cs:47`, granted to `Roles.Moderator` at `:97`) is dead.

Two further documentation contradictions sit inside `docs/api/README.md` itself: line 1799 says "the **eight** endpoints below are live" above a table of **thirteen**; and line 748 says the status of §C.1 is "implemented" while lines 876-899 specify a full `200` response for `recompute` that the code answers with `501` (`PricingEndpoints.cs:160-164`).

**Also absent:** the two statistic families flagged in the Phase 5/6 deviation notes as deferred — `GET /orgs/{id}/statistics/billing/burn-rate`, `/customers/active`, `/staff/seats` (`docs/api/README.md:1771`, `:1790`) — and `GET /api/v1/admin/orgs` is missing from `docs/api/openapi.yaml` altogether though `docs/api/README.md:1346` lists it, so the two catalogue files disagree with each other as well as with the code. All 38 implemented `/admin` routes *are* documented in the API README; the documentary failure is in the other direction.

**Confidence:** Confirmed. Absence established by route enumeration (§8.1) plus the `MapGroup` grep above.

---

### C-2 · A Moderator can approve their own admin request and become Admin

This is the most serious security defect found. The full path:

1. `POST /api/v1/admin/requests` requires only `.RequireAuthorization()` — the default policy, i.e. *any authenticated user* — and derives the requester from the token's `sub`/`email` claims (`AdminEndpoints.cs:20-39`).
2. `POST /api/v1/admin/requests/{requestId}/approve` requires `AdminReviewPolicy`, which is `p.RequireRole(Roles.Moderator, Roles.Admin, Roles.Owner)` (`AuthorizationConfiguration.cs:126-127`).
3. `ApproveAsync` fetches the request, calls Clerk, and never compares the reviewer with the requester:
```csharp
var request = await RequirePendingAsync(requestId, cancellationToken);
// Grant first: if the Clerk Backend API call fails the request stays Pending.
await _clerkAdminClient.GrantAdminRoleAsync(request.ClerkUserId, cancellationToken);
request.Status = AdminApprovalStatus.Approved;
request.ReviewedAt = DateTime.UtcNow;
request.ReviewedByClerkUserId = reviewerClerkUserId;
```
— `Aveline.Api/Modules/Admin/Services/AdminApprovalService.cs:70-91`
4. The grant sets the Clerk user's `public_metadata.role` to `admin` (`ClerkAdminClient.cs:48`), which the JWT template surfaces as `user_role=admin`. `RoleClaimNormalizer.PromoteRoleClaims` copies it into `ClaimTypes.Role` (`RoleClaimNormalizer.cs:21-28`), and `PermissionAuthorizationHandler` intersects roles with the catalog (`PermissionAuthorizationHandler.cs:31-40`). `Roles.Admin` holds **every** permission except `pricing:backdate` (`Permissions.cs:98`).

So a `moderator` — a role whose explicit grant list deliberately excludes `admin:*` (`Permissions.cs:95-97`) — escalates to `admin:users:manage`, `billing:adjust`, `pricing:manage`, `stats:system` and (once C-1 is closed) `audit:view`.

The delivered tests cover a `staff` non-reviewer (`AdminApprovalFlowIntegrationTests.cs:121-132`) and an `admin` reviewer approving **someone else** (`:134-151`). There is no self-approval test.

**Confidence:** Confirmed that the code path has no guard. *Inferred, not verified:* that a real Clerk instance lets a Moderator submit (the submission endpoint is authenticated-only, which is the strongest reading of `AdminEndpoints.cs:39`).

---

### C-3 · `int` overflow in the legacy path under-charges Blossoms by six orders of magnitude

```csharp
public static decimal CalculateBlossomUnits(int inputTokens, int outputTokens, int cachedTokens)
{
    decimal totalTokens = inputTokens + outputTokens + cachedTokens;   // int arithmetic
    decimal raw = totalTokens / 1000m;
    decimal ceiled = Math.Ceiling(raw * 10m) / 10m;
    return Math.Max(ceiled, 0.1m);
}
```
— `Aveline.Api/Modules/Billing/Services/UsageTrackerService.cs:114-121`

The three operands are `int` (`IUsageTrackerService.cs:15-17`), validated only as non-negative (`UsageTrackerService.cs:187-188`), and the project does not enable `CheckForOverflowUnderflow` (verified against `Aveline.Api.csproj`). The addition wraps *before* the widening to `decimal`. Executed repro (§8.2):

```
true total = 2147483648 tokens
  legacy  (int wrap)  -> 0.1
  rule path (long)    -> 3147483.6
  check wrap: -1147483648
```

The rule path is correct because it widens first: `long normalizedUnits = (long)request.InputTokens + request.OutputTokens + request.CachedTokens;` (`UsageTrackerService.cs:141`). The same wrapping sum is repeated in the log line at `:70`, so the log under-reports too. `BR-1.11` (`backend-requirements.md:200`) requires the integer sum with no bound, so the spec does not license this.

**Exploitability, stated honestly:** the ingest endpoint is internal-token only (`UsageEndpoints.cs:18-20`), so this is reachable by the agent service or anything holding the shared token, not by a boutique user. It is a correctness defect with a bounded blast radius, not anonymously exploitable. It is rated high because the failure mode is silent, one-directional (always under-charge), and concentrated at the largest usage volumes.

**Confidence:** Confirmed by execution.

---

### C-4 · The pricing subsystem returns `200/201` while doing nothing

`appsettings.json:16-20` ships:

```json
"Pricing": {
    "//": "Escape hatch: true reproduces the legacy ceil-to-1dp formula and ignores rules.",
    "UseLegacyFormula": true,
    "RuleCacheTtlSeconds": 5
}
```

and ingestion honours it before ever consulting the pricing service:

```csharp
var useLegacyFormula = configuration.GetValue("Pricing:UseLegacyFormula", defaultValue: true);
if (useLegacyFormula || pricingService is null)
{
    return (CalculateBlossomUnits(request.InputTokens, request.OutputTokens, request.CachedTokens), null);
}
```
— `UsageTrackerService.cs:131-135`

Consequences while the flag is `true`:
- Every `POST/PATCH /admin/pricing/rules` and `price-book` succeeds and persists a rule that has **no effect** on any invoice.
- FR-1.4's pricing snapshot is never written (`PricingRuleId`, `UnitsPerBlossom`, `RoundingMode`, `RoundingDecimals`, `NormalizedUnits` stay `NULL`, `:54-62`), so historical Blossom values are not reproducible from the applied rule.
- BR-1.9's `pricing.rule.missing` warning can never fire.
- `docs/api/README.md:747` labels the section "Status: implemented" with no mention of the flag; `implementation-plan.md:881` documents it as intentional ("`true` on first deploy, flipped to `false` after the rule cache is verified warm"), and `docs/ai-usage/kavindu.md:1826` states plainly that "the new pricing engine is inert". The three documents disagree in prominence, not in fact.

**Confidence:** Confirmed. Whether this is *acceptable* is an operational decision, not a code fact — see §5, Recommendation 3.

---

### C-5 · Nine of twelve seeded alert rules reference metrics that are never produced

The only producer of `SystemMetricSample` in the API is `SystemMetricCollector.BuildSamples`. Verified by `grep -rn 'new SystemMetricSample' Aveline.Api` → hits only `SystemMetricCollector.cs:136,156`, and `grep -rn 'UpsertAsync' Aveline.Api` → the single call site is `SystemMetricCollector.cs:92`. It emits exactly twelve names (`SystemMetricCollector.cs:169-185`):

`aveline.process.{cpu_seconds, working_set_bytes, gc_heap_bytes, thread_count, threadpool_queue_length}`, `aveline.queue.telemetry_channel`, `aveline.api.telemetry.dropped`, `aveline.eventbus.{failed, backlog}`, `aveline.api.{requests_per_second, error_rate}`, `aveline.agent.runs_running`

The seeded rules (`SystemAlertRuleSeed.cs:22-59`, mirrored in `Migrations/20260913093451_AddSystemStatistics.cs:163-171`) reference these **never-written** names:

| Seeded rule | Metric referenced | Produced? |
|---|---|---|
| `blossom.balance.negative` | `blossom.balance` | No |
| `blossom.ledger.drift` (Critical) | `blossom.reconciliation.drift` | No |
| `blossom.runaway.org` | `blossom.consumed.rate` | No |
| `api.error.rate` (Critical) | `api.error_rate` | No — collector writes `aveline.api.error_rate` |
| `api.latency.p95` | `api.latency.p95` | No |
| `agent.failure.rate` | `agent.success_rate` | No |
| `agent.run.stuck` | `agent.paused.count` | No |
| `agent.step.runaway` | `agent.steps.per_run` | No |
| `db.pool.saturated` (Critical) | `aveline.db.pool_in_use` | No |
| `telemetry.dropped` | `aveline.api.telemetry.dropped` | **Yes** |
| `queue.telemetry.backlog` | `aveline.queue.telemetry_channel` | **Yes** |
| `eventbus.failed` (Critical) | `aveline.eventbus.failed` | **Yes** |

The drift metric is computed but never recorded: `BlossomService.GetStatementAsync` puts it in the response (`BlossomService.cs:306,319`) and nothing writes it as a sample. So `backend-requirements.md:397`'s "ledger and projection disagree → Critical `SystemAlert`" cannot fire, and neither can three other Critical rules.

The passing test proves nothing here: `AlertEvaluationTests.cs:31` hand-seeds the same never-produced name (`await SeedSampleAsync(harness, "blossom.reconciliation.drift", 0.25m)`), so the test exercises the evaluator, not the pipeline.

`docs/backend/README.md:301-304` acknowledges half of this ("the seeded rules from #227 still reference the short `api.*` / `agent.*` / `blossom.*` names for metrics the collector does not produce") without drawing the conclusion that the controls are dead. No alert exists for collector/writer failures.

**Confidence:** Confirmed.

---

### H-1 · Idempotency: optional where documented required, racy under concurrency, and it caches failures

Five endpoints attach `IdempotencyEndpointFilter` (`BlossomEndpoints.cs:131,170,202,233`; `SubscriptionEndpoints.cs:65`). Four defects:

**(a) The header is optional and its absence is silent.** The filter returns early when the key is blank:
```csharp
var key = http.Request.Headers[HeaderName].ToString();
if (string.IsNullOrWhiteSpace(key))
{
    return await next(context);
}
```
— `IdempotencyEndpointFilter.cs:26-29`. `docs/api/README.md:268` specifies "Missing header on a money-shaped POST → `400 {message, code: idempotency-key-required}`" and `:1082` says all four admin Blossom operations require it. No such `400` is ever produced. The service layer *also* skips dedup when the key is null (`BlossomService.cs:39-47`), so absent-header retries create duplicate ledger entries with no protection at either layer.

**(b) Lookup-then-insert is not atomic.** `TryReplayAsync` runs before the endpoint (`:56`) and `SaveAsync` after (`:109-134`). Two concurrent identical requests both miss, both execute the credit/debit, and the loser's conflict is swallowed:
```csharp
catch (DbUpdateException exception)
{
    // A concurrent first write already stored this key; the winner's response stands.
    logger.LogWarning(exception, "Idempotency record for key {Key} was stored concurrently.", key);
}
```
— `IdempotencyEndpointFilter.cs:128-133`. The loser nevertheless returns its own `201` to the caller, so the client cannot tell that its money-mutating request was actually applied twice.

**(c) Failed responses are replayed.** Nothing filters on status before persisting: `PersistAsync` stores `(short)http.Response.StatusCode` and the body unconditionally (`:121`), and the replay path restores both verbatim (`:68-75`). A `409 insufficient-balance` therefore keeps being returned for the full `Billing:IdempotencyRetentionHours` window (default 24, `IdempotencyService.cs:14`) even after the organisation is topped up. `docs/backend/backend-requirements.md:387-396` defines only the same-body and different-body cases, so there is no documented behaviour being violated — but the behaviour is surprising and not documented.

**(d) The uniqueness key omits `HttpMethod` and is nullable.** `LedgerConfigurations.cs:114-116` indexes `(OrganizationId, Endpoint, IdempotencyKey)` while `HttpMethod` is stored but not keyed (`:96-98`), giving a false `409` for the same key on a different verb to the same route. `OrganizationId` is nullable, and `ResolveOrganizationId` returns `null` for any route without an `organizationId` value (`:136-140`); `AreNullsDistinct(false)` makes those rows collide with each other across all org-less routes. Today every filtered route has an `organizationId`, so this is latent rather than live.

**Confidence:** Confirmed by reading the filter, service, and index definition. The concurrent-execution race is inferred from source order (lookup → endpoint → insert); it was not reproduced with two live processes.

---

### H-2 · `GET /api/v1/admin/users` does not match its own contract

| Aspect | Documented (`docs/api/README.md:1340`, `openapi.yaml:286-315`) | Implemented (`AdminUserEndpoints.cs:21-42`) |
|---|---|---|
| State filter | `accountState` | `state` (`:23`) |
| Org filter | `organizationId` | **absent** — not a parameter, and `IUserService.SearchUsersAsync` has no such argument (`UserService.cs:426-431`) |
| `pageSize` default | `50` (`docs/api/README.md:224`) | **20** (`UserService.cs:434`) |
| `pageSize` max | `200` | **100** (`MaxMemberPageSize = 100`, `UserService.cs:17`) |

Any client following the spec gets an unfiltered first page at the wrong size, and a silently ignored `organizationId`. `PATCH /admin/users/{userId}/state` additionally accepts only `{ accountState }` (`AdminUserEndpoints.cs:96`) while the spec and `openapi.yaml:317-360` document `{ accountState, reason }` — the `reason` is dropped with no `400`, so the audit entry carries no justification (`UserService.cs:407-413`).

**Confidence:** Confirmed.

---

### H-3 · Rule activation is not atomic, and three documented side effects do not exist

`ActivateRuleAsync` trims the predecessor and activates the successor as **two separate round-trips with no transaction**:

```csharp
var predecessor = await repository.FindPredecessorAsync(rule, cancellationToken);
if (predecessor is not null)
{
    predecessor.EffectiveTo = rule.EffectiveFrom;
    predecessor.Status = BlossomRuleStatus.Superseded;
    await repository.UpdateRuleAsync(predecessor, cancellationToken);   // SaveChanges #1
}
rule.Status = BlossomRuleStatus.Active;
await repository.UpdateRuleAsync(rule, cancellationToken);              // SaveChanges #2
```
— `PricingService.cs:144-155`. `PricingService` contains no `BeginTransactionAsync` (verified: the only two transaction sites in the API are `UsageRepository.cs:26` and `BlossomLedgerRepository.cs:50`). If write #2 fails — a concurrent activation tripping the GiST exclusion constraint, a validation error, a dropped connection — the predecessor is left `Superseded` with a trimmed `EffectiveTo` and **no active rule covers that scope**. BR-1.8 and `docs/api/README.md:862-863` both require "atomically, in one transaction".

Three documented effects are absent entirely:

- **`{ "effectiveFrom": "<iso8601>" }`** on activate (`docs/api/README.md:864`). `ActivatePricingRuleRequest` exists at `PricingDtos.cs:105` and is referenced nowhere; the handler hardcodes `effectiveFrom: null` (`PricingEndpoints.cs:132`) → `grep -rn ActivatePricingRuleRequest Aveline.Api Aveline.Api.Tests` returns only the declaration.
- **`pricing.rule.activated` event** (`docs/api/README.md:867`, `backend-requirements.md:226`). `grep -rn 'pricing.rule.activated' Aveline.Api` → no matches; `PricingService` publishes nothing.
- **Cross-instance cache invalidation.** `PricingRuleCache.Invalidate()` increments a process-local counter (`PricingRuleCache.cs:34`), and the cache is keyed `pricing:rule:g{generation}:…` (`:36-37`). A second instance keeps serving the superseded rule until its 5 s TTL lapses. `backend-requirements.md:232-234` requires "every API instance subscribes and clears its L1 cache. TTL is a 5-second safety net" — only the safety net exists. Additionally `PricingRuleCacheWarmer` reads only `page: 1, pageSize: 200` (per the delegated read of `PricingRuleCacheWarmer.cs:53-54`), so Active rules beyond the first 200 are never warmed.

Related, BR-1.7 is unimplemented: `UpdateRuleAsync` checks only `Status != Draft` (`PricingService.cs:97-101`), never whether the rule has priced usage, though `docs/api/README.md:857` documents "409 `rule-immutable` if the rule has already priced usage". And `GetNextVersionAsync` is a max-read-then-add with no unique index on `Version` (`PricingConfigurations.cs:82-87` has unique indexes on `(ScopeKind, Provider, Model, EffectiveFrom)` and the lookup path only), so concurrent creates for one scope can produce duplicate `Version` values.

**Confidence:** Confirmed for atomicity, the unused DTO, the missing event, and the cache mechanism. The duplicate-`Version` race is inferred from the absence of a constraint.

---

### H-4 · Admin pricing reads are reachable by tenant roles and are not org-scoped

`pricing:view` is granted to boutique roles:

```csharp
[Roles.BoutiqueManager] = Grant(
    CatalogView, CustomersView, CatalogManage, ReportsView, ConversationsView,
    BillingView, PricingView, StatsView),
[Roles.BoutiqueOwner] = Grant(..., PricingView, ApiKeysView, ApiKeysManage, ...),
```
— `Permissions.cs:102-112`

The `pricing:view` policy is a bare permission policy with no `OrganizationScopeRequirement` — it is created by the generic loop `options.AddPolicy(permission, p => p.Requirements.Add(new PermissionRequirement(permission)));` (`AuthorizationConfiguration.cs:192-195`) and evaluated against role claims only (`PermissionAuthorizationHandler.cs:31-40`). The four read routes are therefore reachable by any JWT carrying `org_role=org:boutique_manager|org:boutique_owner`: `GET /admin/pricing/rules` (`,44`), `/rules/{ruleId}` (`:51`), `/price-book` (`:181`), `/price-book/{entryId}` (`:188`).

Two of them leak across tenants:

1. `GET /admin/pricing/price-book` takes a caller-supplied `organizationId` and, when omitted, returns every organisation's entries — the repository filters only when the argument is present (`PricingRepository.cs:115-130`). The DTO echoes each `OrganizationId` (`PricingDtos.cs:56`), so a tenant can enumerate other tenants and read their negotiated contract pricing.
2. `GET /admin/pricing/price-book/{entryId}` has **no org predicate at all**: `db.BlossomPriceEntries.FirstOrDefaultAsync(entry => entry.Id == entryId, …)` (`PricingRepository.cs:98-100`), and `BlossomPriceEntry.OrganizationId` exists precisely for per-org overrides (`BlossomPriceEntry.cs:15`).

The documentation is internally contradictory about the permission model rather than simply wrong: `backend-requirements.md:210` deliberately grants `pricing:view` to boutique owner/manager ("Read the price book and rule history"), while `docs/api/README.md:751-752` asserts these permissions are "**Never available to boutique roles**". The code follows the requirements document; the API catalogue contradicts it; and the *scoping* problem — admin routes with no org scope — is a defect under either reading.

There is a related, lower-severity consequence: `POST /admin/orgs/{arbitrary-guid}/blossoms/credit` creates a Blossom account for an organisation that does not exist. `BlossomService.GetOrCreateAccountAsync` validates only non-empty (`:364-367`); `BlossomOrganizationNotFoundException` is thrown only by `SubscriptionService`. Reading a non-existent org silently mints a phantom `UsageAccount`.

**Confidence:** Confirmed for grants, policy construction, missing org predicates, and the absence of an org-existence check on the Blossom path.

---

### H-5 · JWT validation disables audience binding and never checks `azp`

```csharp
public static TokenValidationParameters BuildTokenValidationParameters(string authority) => new()
{
    ValidateIssuer = true,
    ValidIssuer = authority,
    ValidateAudience = false,
    ValidateLifetime = true,
    ValidateIssuerSigningKey = true,
    NameClaimType = ClaimTypes.NameIdentifier,
};
```
— `Aveline.Api/Configurations/AuthenticationConfiguration.cs:96-104`

The omission is documented and deliberate (`docs/ADR/ADR-008-jwt-token-strategy.md:26-28`; `docs/security/auth-security-review.md:42` records it as accepted risk SEC-M1), and a test pins it (`JwtValidationTests.cs:144-153` asserts a mismatched audience still validates). Two consequences worth restating:

- **No `azp` check.** Clerk's own guidance for manual verification includes validating `azp`, warning that omitting it "can open your application to CSRF attacks" ([Clerk — manual JWT verification](https://clerk.com/docs/guides/sessions/manual-jwt-verification)). `grep -rn 'azp' Aveline.Api` → no matches. Mitigating context: the API reads tokens only from the `Authorization` header or `?access_token=` on `/hubs` (`AuthenticationConfiguration.cs:54-65`), never from the `__session` cookie, and the CORS policy is an explicit origin allow-list with credentials (`CorsConfiguration.cs:31-43`), so the classic cookie-driven CSRF vector does not apply. The residual exposure is that a token minted for *any* other origin on the same Clerk instance validates here.
- **5-minute clock skew.** `ClockSkew` is not set, so `TokenValidationParameters`' 300-second default applies — a revoked-session token remains usable for up to five minutes beyond its claims. Acceptable only if paired with the short access-token lifetime Clerk uses; not verified here.

**Confidence:** Confirmed for the parameters and the absence of `azp`. The CSRF impact is *inferred* from Clerk's documentation and the absent cookie path.

---

### M · Medium and lower findings

| ID | Finding | Evidence |
|---|---|---|
| M-1 | **Unsalted IP/UA hashing.** `Telemetry:IpHashSalt` ships `""` (`appsettings.json:56`) and the hash is bare SHA-256 (`MetricDimensionHasher.cs:14,18`). An unsalted IPv4 space is brute-forced in seconds, so the "raw IP is never stored" claim (FR-5.9) is nominally true and practically defeated. | Confirmed |
| M-2 | **Committed default internal token.** `appsettings.Development.json:7` sets `AgentService:InternalToken: "change-me-internal-token"` (git-tracked) and `docker-compose.yml:80,134` default `INTERNAL_API_TOKEN` to the same value while `:64` sets `ASPNETCORE_ENVIRONMENT: Development`. That one secret yields cross-tenant read/write of customer PII through `/internal/customers/**` and `/internal/usage/**`. The handler itself is correct: constant-time compare and fail-closed when unset (`InternalTokenAuthenticationHandler.cs:39-66`). | Confirmed |
| M-3 | **Anonymous `/health` and `/health/ready` disclose environment and build identity.** `HealthCheckResponseWriter.cs:37-49` returns `gitSha`, `buildTime`, `assemblyVersion`, and `environment`, plus the check names `database, redis, agent-service, clerk-jwks`. No secrets or exception text are serialised (only `entry.Value.Description`, `:33`). | Confirmed |
| M-4 | **WhatsApp webhook has no replay window.** HMAC verification is constant-time over the raw body (`WebhookSignatureVerifier.cs:41-45`) and fails closed, but Meta supplies no timestamp and no nonce/tolerance check exists, so a captured valid request replays indefinitely — each replay writes an `InboundMessageLog` and publishes `message.received` (`WebhookEndpoints.cs:141-167`). The Clerk/Svix receiver, by contrast, bounds replays to 5 minutes (`ClerkWebhookVerifier.cs:40-45`). Also: the per-org rate limit is charged **before** signature verification (`:89-99`), so unauthenticated callers can exhaust a target's budget, and the GET verify-token compare is not constant-time (`WebhookEndpoints.cs:50`). | Confirmed |
| M-5 | **No application-level rate limiting.** `grep -rn 'AddRateLimiter\|UseRateLimiter\|RequireRateLimiting'` → zero matches. Only two in-handler limiters exist (WhatsApp 120/min, invitation accept 10/min). Health, OpenAPI (in Development), both webhook POSTs and invitation accept are unthrottled. Matches the project's own SEC-M2 (`docs/security/auth-security-review.md:43`). | Confirmed |
| M-6 | **Substring role mapping in the Clerk webhook.** `ClerkWebhookSyncService.MapBoutiqueRole` uses `Contains("owner"/"supervisor"/"manager")`, so a custom Clerk org role named e.g. `org:not_the_owner` maps to `org:boutique_owner`. Requires a Clerk admin action. | Confirmed from source; exploitability inferred |
| M-7 | **No global exception handler.** `grep -rn 'AddProblemDetails\|UseExceptionHandler\|IExceptionHandler' Aveline.Api` → no matches; `Common/Exceptions/` contains only a README. The `_ => throw exception;` arms of `MapProblem` (`PricingEndpoints.cs:310`, `BlossomEndpoints.cs:333`) therefore surface the framework default: an empty-bodied `500` in Production and a stack trace in Development. `docs/api/README.md:177-191` documents this state honestly. | Confirmed |
| M-8 | **`GET /admin/pricing/rules` and the price book read are unpaginated / non-conforming.** `GET /price-book` returns a bare array (`PricingEndpoints.cs:180`) although `docs/api/README.md:245-246` says new endpoints never return a bare array, and it has no `page`/`pageSize` at all. | Confirmed |
| M-9 | **Pricing request defaults are not applied.** `CreatePricingRuleRequest` (`PricingDtos.cs:84-94`) has no default values, so omitting `minimumChargeBlossoms` binds `0` (documented default `0.1`) and omitting `roundingDecimals` binds `0` (documented default `1`). `roundingMode` happens to land on `Ceiling` because it is the first enum member (`BlossomRoundingMode.cs:7`). `docs/api/README.md:811-813` promises the defaults. | Confirmed |
| M-10 | **`/admin/statistics/system/overview` is documented as cached and is not.** `docs/api/README.md:1952` claims "Deliberately cached server-side, 15 s"; `SystemStatisticsService` contains no cache use and the API registers no response caching (`grep ResponseCach|OutputCach|UseCach` → no matches). | Confirmed |
| M-11 | **`GET /system/eventbus` ignores `from`/`to`.** The handler binds only the service and `ct` (`SystemStatisticsEndpoints.cs:94-95`), while `docs/api/README.md:1817` documents both parameters. The response is instantaneous counters; the documented window is not applied. | Confirmed |
| M-12 | **`/health` documentation is stale and cites a non-existent line.** `docs/api/README.md:678` says `/health` returns `text/plain` "Healthy\|Degraded\|Unhealthy" and checks "Redis only", citing `Program.cs:93` — which is `UseMiddleware<CorrelationIdMiddleware>()`. The code returns JSON with four checks. `docs/api/README.md:1886-1907` (§C.9) describes it correctly, so the catalogue contradicts itself. | Confirmed |
| M-13 | **Security headers stop at three.** `SecurityConfiguration.cs:15-17` sets `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`. No HSTS, no CSP, no `Permissions-Policy` (`grep UseHsts|Strict-Transport-Security` → no matches), and `AllowedHosts` is `"*"`. | Confirmed |
| M-14 | **A demo-policy endpoint ships in every environment.** `v1.MapAuthPolicyDemoEndpoints()` is called unconditionally (`Program.cs:119`) although the file's own header says "Replace these with real feature endpoints" (`AuthPolicyDemoEndpoints.cs:9-12`). It is protected by the fallback policy, so this is attack-surface hygiene rather than a control failure. | Confirmed |
| M-15 | **Onboarding exemption for the whole `/admin` tree.** `OnboardingMiddleware.cs:20-29` exempts `/api/v1/admin` wholesale even though its own comment describes "profile, onboarding-wizard, and invitation endpoints" only. An `OnboardingPending` principal can reach every `/admin/*` route their claims allow, and any future `/admin/*` route is exempt automatically. Authorization still gates each route, so no unauthorised access follows today. | Confirmed |
| M-16 | **`/metrics` is gated but undocumented as to scheme.** `MetricsPolicy` accepts only the internal-token or scrape-token schemes (`AuthorizationConfiguration.cs:111-117`); `docs/api/README.md:1911-1914` describes the status codes loosely. Not verified at runtime. | Partially confirmed |
| M-17 | **Ledger/statement naming drift.** The admin credit/debit/revoke `201` bodies are `BlossomLedgerEntryDto` with `blossomBalanceAfter`/`createdAt` (`BlossomDtos.cs:10,16`), while the doc's ledger-entry example uses `balanceAfter`/`occurredAt`/`kind` (`IBlossomService.cs:58-69`). Frontend code following the example will not bind. | Confirmed |
| M-18 | **Pricing `400` bodies carry no `code`.** `docs/api/README.md:822-825` specifies `400 {code: "validation"}` and `400 {code: "scope-inconsistent"}`; the code returns `400 {message}` (`PricingEndpoints.cs:304`; `PricingService.cs:325-327`). A client branching on `code` silently fails. | Confirmed |
| M-19 | **Three documented status codes are not produced.** Activate returns `409 rule-immutable` where `docs/api/README.md:866` says `400 if not a Draft` (`PricingEndpoints.cs:294-298`); cancel returns `200` for any rule where `:873` says `400` for an Active priced rule (no status check at all, `PricingService.cs:162-176`); three routes return an empty-bodied `404` (`PricingEndpoints.cs:50`, `:187`; `SystemStatisticsEndpoints.cs:135`) against the `{message}` convention at `:188`. | Confirmed |
| M-20 | **`PlanEntitlement` overrides can be silently ignored.** `SubscriptionService.ResolveTierLimitAsync` consults the in-memory catalog first and only falls back to the `PlanEntitlements` table when the key is absent, so a corrected DB row never wins for `blossoms.monthly`; `LedgerJobs` derives the next period's allowance from the catalog rather than `IEntitlementResolver`, bypassing per-org overrides. `EntitlementResolver` skips the catalog entirely once any plan row exists (`EntitlementResolver.cs:30-38`), so a partially seeded catalog resolves unlisted keys to the caller's fallback. | Confirmed from source |
| M-21 | **Alert cooldown never elapses under sustained breach.** `open.LastObservedAt = now` is written before the cooldown test, so a continuously breaching rule increments `OccurrenceCount` for ever and never re-fires; `MaxAlertsPerHour` guards only the `open is null` branch. Lower impact than C-5 but the same subsystem. | Confirmed from source |
| M-22 | **`LoadSamplesAsync` has no organisation predicate and no row limit** (`AlertService.cs:396-412`) even though `EvaluateRuleAsync` accepts an `organizationId`; a per-org rule would aggregate every organisation's samples. Latent today because every seeded rule is system-wide. | Confirmed from source |

---

## Open questions and disagreements

1. **Is C-4 a defect or a deliberate rollout state?** `implementation-plan.md:881` and the inline `appsettings.json` comment both describe `UseLegacyFormula: true` as intentional. I report it as a finding because `docs/api/README.md:747` presents the pricing section as plain "implemented" with no caveat, and because a reader of that catalogue would reasonably conclude rules affect billing. If the team's position is "the flag is the documented rollout gate", then the correct remediation is a documentation fix, not a code change.

2. **Is H-4 intended?** `backend-requirements.md:210` explicitly grants `pricing:view` to boutique owner/manager, so "boutique can read pricing" is by design. What is *not* defensible under any reading is that admin routes have no org scope and that `price-book/{entryId}` resolves without an org predicate while carrying per-org contract data. The two documents disagree about the boutique grant; the scoping defect is independent of that disagreement.

3. **C-5 contradicts `docs/backend/README.md:301-304`.** That section states the mismatch exists but frames it as "those rules fire only when another producer supplies those names". I did not find another producer, and I searched for one (`grep -rn 'new SystemMetricSample'`, `grep -rn 'UpsertAsync'`). If a producer exists outside this repository — an external exporter writing directly to `SystemMetricSamples` — the finding downgrades from "nine dead rules" to "nine rules with an out-of-repo dependency". That is the one thing that would change this finding, and it is not determinable from the repository.

4. **Test-suite green is not evidence for most of these findings.** 1 042 tests pass and yet C-2, C-5, H-1(c), H-3 and C-1 are all uncovered:
   - no self-approval test (`AdminApprovalFlowIntegrationTests.cs:121-151` covers a staff non-reviewer and a third-party reviewer);
   - the alert test hand-seeds a never-produced metric name;
   - no test asserts a `400` for a missing `Idempotency-Key`, which is exactly why H-1(a) survived;
   - no test covers activate-with-body or the `pricing.rule.activated` event;
   - no test asserts the absence of the four missing endpoints.
   `Aveline.Api.Tests` runs against the EF **in-memory** provider for the integration tests (e.g. `TenantIsolationTests.CreateContext`), so the hand-edited M7 migration, the partial indexes, the CHECK constraints and the GiST exclusion constraint are exercised only by the 25 Postgres tests — which is why `PricingActivationPostgresTests` and the price-book constraint tests matter and should be extended rather than replaced.

5. **The `int`-overflow defect in C-3 was misreported by a delegated workstream.** That report gave `2e9 + 2e9 + 3e8` as the repro and claimed it yields `0.1`; the actual result is `5032.8`. The defect is real but the arithmetic is subtler than a simple negative wrap; the verified repro is `2000000000 + 147483648 + 1000000000 = -1147483648 → 0.1`. I report the corrected version and flag the discrepancy rather than smoothing it.

---

## 7 · What is correct (verified)

Stated explicitly so it is not mistaken for omission:

- **Build and suite:** `0 warnings, 0 errors`; **1 042/1 042 tests pass**, including 25 Testcontainers-PostgreSQL tests.
- **D-3 lost-update race is genuinely fixed, twice over.** Consumption uses an atomic SQL increment (`UsageRepository.cs:56-64`); adjustments use the `xmin` row-version token (`BillingConfigurations.cs:112-113`, migration `20260911165632:51-57`) with a 5-attempt retry that reloads and re-applies the delta plus jitter (`BlossomService.cs:397-425`), mapped to `409 concurrent-modification`. `LedgerPostgresTests.TwentyParallelCredits_ProduceTheExactArithmeticSum` and `XminConcurrencyToken_RaisesOnStaleUpdate` both pass.
- **D-1, D-2, D-12 are fixed.** One catalog, `PlanEntitlementDefaults.cs:33`; the repository now takes the limit as a parameter (`UsageRepository.cs:37-49`); the three duplicated tables are gone. What remains is a duplicated *fallback literal* (`SeedFallbackLimit = 150m`), not a duplicated table.
- **No SQL injection sink.** Every raw-SQL site is parameterised or built only from server-side values: `CustomerMemoryRepository.cs:49-53` (`{0}`/`{1}`/`{2}` with an `object[]`), `:69-84` (`SqlQueryRaw` with `ORDER BY` and `LIMIT` parameterised), `ApiMetricRepository.cs:60` (const `@`-placeholder SQL + typed `NpgsqlParameter[]`). `FromSqlRaw`/`FromSqlInterpolated`/`ExecuteSqlInterpolated`/`NpgsqlCommand` do not appear anywhere. The partition-drop function reads its identifier from `pg_class`, constrains it with `^ApiRequestLogs_[0-9]{8}$` and a parent-table join, and quotes it with `%I` (`Migration 20260911190644:74-98`).
- **Statistics query inputs are whitelisted, not interpolated.** `groupBy` ∈ `hour|day|month` (`ApiStatisticsValidation.cs:15,103`) and is applied in C# (`ApiStatisticsService.cs:277-282`); `windowSize` ∈ `instant|minute|hour|day` and is mapped to a fixed stored value (`SystemStatisticsEndpoints.cs:20,156`; `SystemStatisticsService.cs:132,156`); `metric` is an equality predicate only.
- **Tenant scoping on org routes is done properly.** `OrganizationScopeAuthorizationHandler` resolves `sub` → local user → `GetMembershipAsync` and requires `Status = Active` plus a role grant (`:61-90`), so a stale JWT org claim cannot cross tenants; API-key cross-tenant use returns `404` before authorization can answer `403` (`ApiKeyTenantScopeMiddleware.cs:36-43`).
- **API-key security is sound.** 32 base62 chars from `RandomNumberGenerator.GetInt32` ≈ 190.5 bits (`ApiKeyCredentials.cs:25-35`); constant-time comparison (`ApiKeyService.cs:230-232`, `ApiKeyCredentials.cs:48-57`); scopes reject `pricing:*`, `admin:*`, `billing:adjust` (`ApiKeyScopes.cs:15,37`); a key can never mint another key (`ApiKeyEndpoints.cs:40-46`); the secret is returned once and never logged (logs carry only `prefix`/`keyId`, `ApiKeyService.cs:93-95`).
- **Webhook HMACs are constant-time and fail closed.** Clerk/Svix (`ClerkWebhookVerifier.cs:61-78`, 5-minute tolerance at `ClerkWebhookEndpoints.cs:18`) and WhatsApp (`WebhookSignatureVerifier.cs:41-45`), both with `CryptographicOperations.FixedTimeEquals`.
- **Internal-token and scrape-token auth are correct** — `FixedTimeEquals`, fail-closed when unset, separate schemes never used as the default (`InternalTokenAuthenticationHandler.cs:39-66`; `ScrapeTokenAuthenticationHandler.cs:31-51`).
- **CORS is correct** — explicit origin allow-list with `AllowCredentials`, validated non-empty at startup; `AllowAnyOrigin`/`SetIsOriginAllowed` appear nowhere (`CorsConfiguration.cs:24-43`).
- **Money arithmetic is `decimal` end to end**, at `decimal(18,4)`, with DB-level CHECK constraints on the balance identity and ledger deltas (`BillingConfigurations.cs:73-93`, `LedgerConfigurations.cs:14-18`), and zero-guarded percentage maths. The only money defect is C-3's `int` sum.
- **`PricingRuleCache` invalidation within a process is correct** — a generation counter folded into the key invalidates every entry atomically without enumerating keys (`PricingRuleCache.cs:34-37`). The gap is only cross-instance (H-3).
- **Alert auto-resolution has no off-by-one.** `ConsecutiveOkCount >= AutoResolveConsecutiveOk` resolves on exactly the Nth consecutive OK (`AlertService.cs:277-311`), pinned by `AlertEvaluationTests.cs:84-115`.
- **All six retention/rollup jobs are UTC-consistent** with non-overlapping windows (`grep 'DateTime.Now|ToLocalTime|TimeZoneInfo'` in the two modules → zero matches).

---

## 8 · Reproducibility appendix

### 8.1 Route-table diff (`openapi.yaml` vs implementation)

A Python script walks `Aveline.Api/**/*.cs`, tracks the enclosing `MapGroup("...")` prefix, normalises `{...}` segments, and prepends `/api/v1` to build the implemented route set; it then diffs the 104 `^  /...:` paths in `docs/api/openapi.yaml`. Result — **documented but not implemented:** `/api/v1/admin/orgs/{organizationId}/entitlement-overrides`, `/api/v1/orgs/{organizationId}/statistics/billing/burn-rate`, `/api/v1/orgs/{organizationId}/statistics/customers/active`, `/api/v1/orgs/{organizationId}/statistics/staff/seats`, `/api/v1/admin/statistics/billing/{profitability,org-usage,adjustments,plan-changes,downgrades}`, `/api/v1/admin/audit`, plus `/api/v1/auth/claims` (mapped as `/auth/claims`, a script artefact to be ignored). `/health*` and `/metrics` are mapped on the root app, not under `/api/v1`, so they are false negatives of the script and are confirmed present at `HealthEndpoints.cs:15,23,24` and `Program.cs:114`. `GET /api/v1/admin/orgs` is absent from `openapi.yaml` but present in `docs/api/README.md:1346` and FR-4.8.

### 8.2 C-3 repro (executed outside the repository)

```csharp
static decimal Legacy(int i, int o, int c) {
    decimal totalTokens = i + o + c;              // int arithmetic, unchecked
    return Math.Max(Math.Ceiling(totalTokens/1000m*10m)/10m, 0.1m);
}
static decimal RulePath(long n, int units, int dp) {
    decimal raw = n / (decimal)units;
    return Math.Max(Math.Round(raw, dp, MidpointRounding.AwayFromZero), 0.1m);
}
Console.WriteLine(Legacy(2000000000, 147483648, 1000000000));  // -> 0.1
Console.WriteLine(RulePath(3147483648L, 1000, 1));             // -> 3147483.6
Console.WriteLine(unchecked(2000000000 + 147483648 + 1000000000)); // -> -1147483648
```

### 8.3 Commands used

```
dotnet build Aveline.Api/Aveline.Api.csproj -v q --nologo
dotnet test  Aveline.Api.Tests/Aveline.Api.Tests.csproj --nologo -v q
```

---

## 9 · Recommendation (separable from the findings)

Ordered by risk reduction per unit of effort. This is advice; the findings above stand independently.

**Before the next release**

1. **Add the self-approval guard** (C-2): reject `reviewerClerkUserId == request.ClerkUserId` in `AdminApprovalService.ApproveAsync`, and decide whether `Moderator` should hold `AdminReviewPolicy` at all. Add the missing test. This is a few lines and closes the only privilege escalation found.
2. **Make `Idempotency-Key` required on money-shaped POSTs** (H-1a): return `400 {code: "idempotency-key-required"}` when the header is absent, matching `docs/api/README.md:268`. Then close the concurrency window (H-1b) by inserting the replay record *before* invoking the endpoint inside a transaction that the endpoint's write participates in, or by taking a per-(org, endpoint, key) advisory lock.
3. **Widen the token sum in the legacy path** (C-3): `long total = (long)inputTokens + outputTokens + cachedTokens;`. One line, and it removes an under-billing path that is silent by construction.
4. **Decide and record the `UseLegacyFormula` position** (C-4): either flip it off after verifying the cache is warm, or add the caveat to `docs/api/README.md:747`. Do not leave the catalogue implying rules are live.
5. **Fix the `int`-bound and defaults on the pricing DTOs** (M-9) and the `state`/`accountState`/paging mismatch (H-2) — both are pure contract drift.

**Next**

6. **Wrap `ActivateRuleAsync` in a transaction** and add the `Version` unique index (H-3); implement or delete the unused `ActivatePricingRuleRequest`; publish `pricing.rule.activated`.
7. **Give the admin pricing reads an org scope or an admin-only policy** (H-4): add an org predicate to `GetPriceEntryAsync`/`ListPriceEntriesAsync` on the tenant path, and reconcile `backend-requirements.md:210` with `docs/api/README.md:751-752` — one of them is wrong about boutique grants.
8. **Close C-1 consciously.** I would sequence it as: audit *read* (add a query method to `IAuditRepository`, the `GET /admin/audit` route, and wire `AuditViewPolicy`), then `GET /admin/orgs` (the `admin:orgs:read` permission and grant already exist), then the entitlement-overrides PATCH, then the billing statistics. Every one of these is currently a documented-but-absent capability, so the alternative — deleting them from the docs — is equally legitimate if they are descoped.
9. **Fix the alert pipeline (C-5)** by making the collector (or a dedicated drift/blossom collector) emit the names the rules reference, or by rewriting the seed to the names the collector emits. Until then, four Critical controls are decorative. Add an alert on collector/writer failure — `ApiTelemetryWriter` currently discards a failed batch with only a log line, and no metric covers DB write failures.
10. **Harden the environment:** non-empty `Telemetry:IpHashSalt`, a real `INTERNAL_API_TOKEN` with no committed fallback, HSTS/CSP headers, move the demo-policy endpoints behind a Development guard (M-1, M-2, M-13, M-14).
11. **Add the deferred load gate.** `docs/backend/README.md:268-272` states plainly that the FR-6.3/BR-6.3 5 000 req/s p99 ≤ 1 ms gate is unmeasured. Either measure it or keep the deviation visible; do not let it migrate into the "done" column.

---

*Report authored under the read-only constraint: no file under `Aveline.Api/`, `Aveline.Api.Tests/`, `agent-service/` or `frontend/` was modified. The only file created is this report.*
