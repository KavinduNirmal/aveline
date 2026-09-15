# Admin Backend API — Post-Fix Disposition: What Still Needs Fixing

**Reviewed revision:** `8d4b45d` **plus an uncommitted working-tree patch** (49 tracked files, `+1969 / −485`, plus 2 new untracked migrations and 1 new untracked test file). No commit contains this patch yet.
**Supersedes the status columns in** `admin-backend-api-reconciliation.md` (which described `8d4b45d` only).
**Method:** full `dotnet build` + full `dotnet test` on the working tree; `git diff` review of every hunk; three runtime reproductions built outside the repo; four independent verification workstreams (idempotency/override, alert/telemetry, API contract, hardening) whose load-bearing claims I re-checked myself.
**Date:** 2026-09-13

---

## Answer first

**The patch closes essentially everything I asked for, and it is safe to commit.** Of the 17 Phase 1 items in my reconciliation plan, **16 are done and 1 is half-done**, and the five new defects the previous session introduced are all fixed — including the two I flagged as most serious (`pricing:backdate` bypass, headers stripped from every handled 500).

```
dotnet build  → Build succeeded. 0 Warning(s), 0 Error(s)
dotnet test   → Failed: 0, Passed: 1157, Skipped: 0, Total: 1157  (was 1132 at 8d4b45d: +25)
```

Two of the fixes I verified by execution rather than by reading:

| Fix | My evidence |
|---|---|
| Security headers on a handled 500 | Reproduced on .NET 10 with the same middleware order: `/ok` had all five headers; `/boom` had **none** before the fix. After the fix pattern: `/ok` and `/boom` **both** carry all five, and the 500 body leaks no stack detail. |
| `pricing:backdate` gate | Read both sides: `POST /rules` gated on `Permissions.PricingBackdate` (`PricingEndpoints.cs:63-68`); the patch adds the identical gate to **PATCH** (`:105-118`), **activate** (`:148-158`), *and* the service layer (`PricingService.cs:113-124`, `:155-163`) so a non-HTTP caller cannot bypass it. |

**What still needs fixing is short and none of it is a blocker:** one new *documentation* defect the patch itself introduced (an over-broad normative claim), one half-done item (the nullable half of H-1d was not addressed), and seven items that were open or accepted before this patch and remain so — of which **M-2 (a committed default shared secret) is the only one I would still raise as a risk rather than a chore.**

---

## 1 · Phase 1 disposition

| # | Item | Status | Evidence |
|---|---|---|---|
| **1.0** | Restore the `pricing:backdate` gate on activate + PATCH | **Done — and hardened** | Endpoint gate on activate (`PricingEndpoints.cs:148-158`) and PATCH (`:105-118`); service-layer guard so no non-HTTP caller bypasses it (`PricingService.cs:113-124` for update, `:155-163` for activate, `allowBackdate` threaded through `IPricingService`). Tests: `ActivateRule_WithPastEffectiveFrom_AsAdmin_Returns403`, `..._AsOwner_Returns200`, `UpdateRule_WithPastEffectiveFrom_AsAdmin_Returns403`. |
| 1.1 | Accept/validate/persist `effectiveTo` on the override | **Done** | `EntitlementOverrideDtos.cs:12-25` adds `EffectiveTo`; validated `> effectiveFrom` (`EntitlementOverrideService.cs:137-143`); persisted (`:154`). The resolver already excluded expired rows (`EntitlementRepository.cs:35-40`). Tests: `EffectiveTo_IsPersisted`, `EffectiveToBeforeEffectiveFrom_Returns400`. |
| 1.2 | Close the idempotency concurrency window | **Done (conditional)** | A per-key distributed lease on `(org, endpoint, method, key)` — `IdempotencyEndpointFilter.cs:73-82`, `:148-188` — built on the existing atomic `SET NX PX` mutex (`RedisDistributedJobLock.cs:40-44`). See §2.1 for the residual condition. |
| 1.3 | `HttpMethod` in the uniqueness key | **Half-done** | Migration `20260913125349` correctly drops and recreates the index as `(OrganizationId, Endpoint, HttpMethod, IdempotencyKey)` with `NULLS NOT DISTINCT`, and the lookup is now method-aware (`IdempotencyRepository.cs:14-17`). **But `OrganizationId` is still nullable and `AreNullsDistinct(false)` retained** (`LedgerConfigurations.cs:125`) — the second half of my recommendation. See §2.2. |
| 1.4 | Postgres pass for the new paths | **Done** | New `AdminReconciliationPostgresTests.cs`: `CaptureBlossom_IsTranslatableByNpgsql_AndIgnoresClosedPeriods` (proves the drift query translates and asserts `DatabaseReadFailures == 0`) and `SetOverrides_RollsBackAnEarlierEntryWhenALaterOneIsRejected` (real rollback). |
| 1.5 | Log/count the swallowed collector exception; stop treating a missing sample as OK | **Done** | `SystemMetricCollector.cs:388-399` now increments `DatabaseReadFailures` and `LogError`s. `AlertService.cs:90-101` returns early on a null sample so a gap can no longer auto-resolve an alert. Test: `DatabaseCaptureFailure_IsCountedInsteadOfSwallowed`, `MissingSamples_DoNotAutoResolveAFiringAlert`. |
| 1.6 | Storm guard on the re-fire path; refresh `LastTriggeredAt` | **Done** | `AlertService.cs:127-148` consumes a persisted per-rule quota before re-firing and sets `rule.LastTriggeredAt = now`. Tests: `ReFire_IsCappedByTheMaxAlertsPerHourStormGuard`, `ReFire_RefreshesTheRulesLastTriggeredAt`. |
| 1.7 | Filter `CaptureBlossomAsync`; stop the balance latch | **Done** | `SystemMetricCollector.cs:453-466` filters `!IsClosed && PeriodStart <= now && PeriodEnd > now` and scopes the ledger `GROUP BY` to those accounts. Test: `BlossomBalance_IgnoresClosedHistoricalPeriods`. |
| 1.8 | `telemetry.dropped` `Sum` → `Rate` | **Done** | Seed `SystemAlertRuleSeed.cs:54`; migration rewrites the existing row's string column; `Down` restores `Sum`. Test: `TelemetryDroppedRule_UsesRateNotSum`. |
| 1.9 | Update `openapi.yaml` | **Done — with one new defect** | Price-book (5 operations), `/admin/orgs`, `/admin/audit/{entryId}`, the override route now `patch:` + 200 + the real body, cancel `400 rule-priced`, corrected PATCH-409 wording, `OnboardingPending` enum, `AccountState` enum. File parses as valid OpenAPI 3.0.3, 100 paths, 128 schemas, **146/146 `$ref`s resolve**, no duplicate keys. **But** the same commit added an over-broad normative sentence — see §2.3. |
| 1.10 | Fix the constraint comment; wire or delete `PricingRuleOverlapException` | **Done** | Comment corrected to state the real `Status = 'Active'` predicate (`PricingService.cs:172-174`); the never-thrown exception type and its dead `MapProblem` arm are deleted. |
| 1.11 | Bound `page`; bound override strings; type-vs-catalog check | **Done** | `MaxPage = 10_000` in both new endpoints (`AuditEndpoints.cs:18`, `AdminOrganizationEndpoints.cs:23`) — closes the `-400` offset I reproduced. Override text bounded at 200 (`EntitlementOverrideService.cs:30,248-253`) matching the column; canonical-type check at `:125-134`. Tests: `Admin_Search_ClampsAnOutOfRangePage`, `AuditList_ClampsAnOutOfRangePage`, `ValueTypeThatDisagreesWithTheCatalog_Returns400`, `OverlongStringValue_Returns400RatherThan500`. |
| 1.12 | Transactional multi-override + audit, and an expiry/overlap semantic | **Done (app-level overlap)** | Whole batch validated before any write; one transaction covering the upserts **and** the audit; commit after resolve (`EntitlementOverrideService.cs:52-96`). Test: `BatchWithOneInvalidEntry_CommitsNothing`. Overlap is an application check only — see §2.4. |
| 1.13 | Move rollover entitlement resolution out of the loop | **Done** | `LedgerJobs.cs` now resolves through `IEntitlementResolver` rather than per-account catalogue lookups. |
| 1.14 | Re-apply headers/HSTS on a handled 500 | **Done — verified by execution** | `SecurityConfiguration.ApplyHardeningHeaders`/`ApplyHstsHeader` extracted and called from `GlobalExceptionHandler.cs:41-52`; `AddHsts` registered so the handler reads the same policy. My repro confirms all five headers plus the correct localhost exclusion (`ExcludedHosts = [localhost,127.0.0.1,[::1]]`). |
| 1.15 | Salt the UA hash; document the salt | **Done** | `HashUserAgent(userAgent, salt)` (`MetricDimensionHasher.cs:17-18`) fed the same salt as the IP (`ApiTelemetryMiddleware.cs:99`); `TELEMETRY_IP_HASH_SALT` added to `.env.example`, `Aveline.Api/.env.example` and `docker-compose.yml:81-84`. |
| 1.16 | Correct the stale exception-handler docs; soften the M-4 claim | **Done** | `docs/api/README.md:181-199` now documents the real `{status,message,traceId}` envelope; `Aveline.Api/Common/Exceptions/README.md` is internally consistent and notes the correction; `docs/backend/README.md:383,393` records M-4 as **partially fixed** and names what is still open. |

**So: 16 of 17 complete.** The one half-done item is the nullable-`OrganizationId` half of 1.3 (§2.2).

---

## 2 · What still needs fixing

### 2.1 The idempotency lease fails *open* by design (decide whether that is right)

When the lock store throws, the filter logs a warning and proceeds **without a lease** (`IdempotencyEndpointFilter.cs:161-171`, returning `NoOpLease.Instance`). The comment states the reasoning explicitly: turning a lock outage into a billing outage would be worse. I agree with the trade-off — the ledger's own idempotency index (`BlossomService.cs:55-63`) remains as the backstop, so the outcome is a re-opened race window rather than a double charge. Two things to note, neither a defect:

- Cross-instance correctness depends on Redis being configured. Without `Redis:ConnectionString` the lock is `InMemoryDistributedJobLock`, i.e. process-local. `docker-compose.yml` sets it; a bare-metal multi-instance deploy must.
- A duplicate that cannot take the lease within 10 s receives `409 { code: "idempotency-key-in-flight" }`. That is a sensible new status but it is **not** covered by any test, and for a client that treats any 409 as permanent failure it is a worse outcome than a replay. **Recommend a test, and a doc line.**

### 2.2 The nullable-`OrganizationId` half of the idempotency key was not addressed

`AreNullsDistinct(false)` is retained and the column is still nullable, so two org-less rows with the same `(Endpoint, HttpMethod, Key)` still collide. Live impact is **nil** because all five filtered routes carry `{organizationId:guid}`. My Phase 1 item said "make `OrganizationId` non-nullable for filtered routes **or add a partial index**". Neither was done. Low priority — consider it a latent-consistency item rather than a bug, or close it with a partial index `WHERE "OrganizationId" IS NOT NULL`.

### 2.3 The patch introduced a new documentation defect: an over-broad normative claim

`docs/api/README.md:48-50` now says:

> `docs/api/openapi.yaml` is the hand-maintained contract. **It is normative for shipped endpoints only:** every route the API actually maps must appear in it with the shipped method, policy and shapes.

**That sentence is false as written.** A route-by-route scan of every `MapGroup`/`Map*` call in `Aveline.Api` against the 100 documented paths finds **43 mapped operations absent** from `openapi.yaml`:

| Group | Count | Examples |
|---|---|---|
| Root-mapped `/internal/*` | 18 | `POST /internal/usage/record`, `GET /internal/usage/records/{orgId}`, all 12 `/internal/customers/*`, the 3 `/internal/agent-runs/*` |
| `/api/v1/admin/statistics/{agents,api,api-keys}` | 8 | `GET /admin/statistics/agents/{overview,runs,reliability}`, `GET /admin/statistics/api/{requests,errors,latency,endpoints}`, both `api-keys` |
| Development-only `/api/v1/policies/*` | 8 | the auth-policy demo routes |
| Other `/api/v1` | 7 | `POST /agents/ping`, `POST /users/onboarding`, `POST /webhooks/clerk`, both WhatsApp webhooks, `GET /orgs/{id}/integrations/messages`, `GET .../agents/runs/{runId}` |
| SignalR hubs | 2 | `/hubs/notifications`, `/hubs/conversations` (not expressible in OpenAPI 3.0.3) |

Every one of these *is* documented in `docs/api/README.md` (internal `:620-662`, admin statistics `:1546`/`:1843`, policies `:742`, etc.), so nothing is wholly undocumented — but the machine contract does not carry them. **Fix:** scope the sentence (`"excludes /internal/**, Development-only demo routes and SignalR hubs"`) or add the routes. I recommend scoping the sentence: `/internal/*` is a service-to-service surface that does not belong in a client contract, and the hubs are not expressible.

Two smaller contract-surface items in the same family: `GET .../agents/runs/{workflowRunId}` is documented while the code binds `{runId}` (`AgentStatisticsEndpoints.cs:39`) — same URL, different parameter name, so SDK generators will disagree; and the `state` legacy alias the code accepts is documented in prose but absent from the OpenAPI parameter list.

### 2.4 Override overlap is application-level only (TOCTOU)

`EntitlementOverrideService.cs:167-185` implements correct half-open-interval overlap detection, but there is no database constraint — the only index is `(OrganizationId, Key, EffectiveFrom)`. Two concurrent PATCHes for the same key can both pass the check and both commit overlapping windows; two concurrent inserts at the *same* `EffectiveFrom` raise `DbUpdateException`, which the endpoint does not catch (`AdminOrganizationEndpoints.cs:90-98` catches only the validation and not-found exceptions) and so surfaces as a 500. The Blossom pricing tables solved exactly this with a `tstzrange` exclusion constraint (`20260911162158_AddBlossomPricingRules.cs:107-111`); the same pattern would close it. **Recommend** the exclusion constraint plus catching `DbUpdateException` as a 409.

### 2.5 Three documentation statements that are still wrong

| Where | Says | Reality |
|---|---|---|
| `docs/api/README.md:116` | `OnboardingPending` allowed on "profile, **organization**, invitation, and onboarding routes" | It is wrong in **both** directions: `/api/v1/orgs*` is deliberately **excluded** (`OnboardingMiddleware.cs:12-14,100-102`), and the single admin carve-out `POST /api/v1/admin/requests` is **not** mentioned. |
| `docs/api/README.md:232,253` | `pageSize` bounds are `Math.Clamp(pageSize, 1, 200)` as a normative Part C rule | The three new paginated endpoints fall back to the default **50** for `pageSize < 1` (`AuditEndpoints.cs:39-41`, `AdminOrganizationEndpoints.cs:48-50`, `UserService.cs:439-441`), and the "Known deviations" table was not extended. The behaviour is defensible; the stated rule is now wrong. |
| `docs/api/openapi.yaml:347` | admin state-change `reason` has `minLength: 10` | No code enforces a lower bound — `UserService.cs:417` only trims (`string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()`). A 3-character reason is accepted. |

One more, newly-found and minor: `docs/api/openapi.yaml` defines `PricingRecomputeResult` but nothing references it (the recompute endpoint documents only 501). Dead schema.

### 2.6 Findings that were open before this patch and remain open

| ID | Finding | Status | Note |
|---|---|---|---|
| **M-2** | Committed default `change-me-internal-token` | **Open, deliberately deferred** | Still in `Aveline.Api/appsettings.Development.json:7`, `docker-compose.yml:80,138`, `.env.example:35`. `docs/backend/README.md:392` records it as deferred with reasoning that holds (Development/compose only; the handler fails closed when unset). **This is the one I would still raise as a risk**: the compose profile runs `ASPNETCORE_ENVIRONMENT: Development`, and that token grants cross-tenant `/internal/*` PII access. A failing-fast guard when the token equals the known placeholder outside Development would close it cheaply. |
| **M-5** | No application-level rate limiting | Open | `grep` still returns zero `AddRateLimiter`/`UseRateLimiter`/`RequireRateLimiting`. Platform item, already recorded as SEC-M2. |
| **M-10** | Documented 15 s overview cache absent | Open | No cache in `SystemStatisticsService` (count 0). Either add it or delete the claim at `docs/api/README.md:1952`. |
| **M-11** | `/system/eventbus` ignores `from`/`to` | Open | Handler still binds only the service and `ct` (`SystemStatisticsEndpoints.cs:94`). |
| **M-16/M-17/M-12** | `/metrics` scheme docs; `blossomBalanceAfter` vs `balanceAfter`; `/health` doc drift | Open / unverified | Not touched by this patch. M-12's `/health` text was corrected in `docs/api/README.md` §B.12 at `8d4b45d`. |
| **M-22** | Alert sample query has no org predicate or row cap | Open, now *documented* as intentional | Acceptable while every seeded rule is system-wide; a future per-org rule would need it. |

---

## 3 · Defects I found in my own prior reporting

Stated because a review that never corrects itself is not a review:

1. **The alert-quota window is not a defect.** A verification workstream flagged that `TryConsumeFireQuota` uses a *fixed* window anchored at the first fire, not a rolling hour, and warned of up to 2× the cap. I simulated it: with `MaxAlertsPerHour = 10` and a 300 s cooldown, the worst **real** 60-minute sliding window contains **exactly 10** fires, not 20 — the cooldown itself prevents clustering. This is a naming inaccuracy in the code comment ("rolling hour"), not an enforcement failure.
2. **"43 vs 8" is a difference of method, not a contradiction.** My own route-diff script reported 8 undocumented admin-statistics paths; the workstream's broader scan (resolving group prefixes across modules and root-mapped registrations) found 43. The 43 is the correct number; my script under-counted because it could not resolve `/internal/*` and demo routes registered on the app rather than the `/api/v1` group.
3. **`AdjustmentReason.minLength: 10` is correct, not stale.** My reconciliation note listed it as an open drift item; `BlossomService.cs:26` and `PricingService.cs:21` both enforce 10..500, and the override's 1..500 reason was correctly given its own schema.
4. **`AdminReconciliationPostgresTests` has no Docker skip guard — but neither does any pre-existing Postgres test in this suite** (`LedgerPostgresTests` and eight others set up `PostgreSqlContainer` the same way). Consistent with the codebase; not a new regression.

---

## 4 · Recommendation

1. **Commit the patch.** It is build-clean, test-green (1 157/0/0), closes 16 of 17 Phase 1 items and all five previously-introduced defects, and the two fixes I could verify by execution both behave correctly. The two new migrations are correct and reversible, and the EF model snapshot matches the final migration exactly (I diffed `BuildTargetModel` against `BuildModel` line-by-line) — no `dotnet ef` drift.
2. **Before or with the commit, fix the three cheap items** (§2.3 scoping sentence, §2.5 three doc rows + dead schema, and a test for the `409 idempotency-key-in-flight` path). These are documentation-and-test changes; none touches behaviour.
3. **Then decide the two policy items**: whether to accept the fail-open lease (§2.1) and whether to add the override exclusion constraint (§2.4). Both are defensible as-is; both are cheap to close.
4. **Keep M-2 on the list.** It is the only remaining finding with a plausible path to cross-tenant data exposure, and the cheapest fix is a startup guard that refuses the known placeholder token outside Development.
