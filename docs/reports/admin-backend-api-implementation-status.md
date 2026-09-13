# Admin Backend API — Implementation Completeness Assessment

**Revision reviewed:** `a542a5e` (clean working tree)
**Commits since the previous disposition:** `3791519` "close the API reconciliation findings", `a542a5e` "fail the idempotency guard closed and close the disposition gaps" — the previously-uncommitted patch plus a follow-up, now both landed (59 files, `+14250 / −510`).
**Method:** `dotnet build` + full `dotnet test` at HEAD; `git show` review of both commits; a route-by-route diff of every `MapGroup`/`Map*` call against `docs/api/openapi.yaml`; verification that each *deferred* item is genuinely absent from the code rather than merely undocumented.
**Date:** 2026-09-13

---

## Answer first

**Yes — the admin backend as specified is done.** Every defect the plan committed to fix is fixed, all six phases (0–6) are complete, and all eight review findings I raised across three passes are now closed. The build is clean and **1 162 of 1 162 tests pass with 0 skipped**.

But "done" needs three qualifications, and the third matters more than the first two:

1. **Nine items are formally deferred** — declared in writing, with the reason and the impact, in `docs/backend/README.md` §Deferred and `docs/api/README.md` Appendix P. Deferred is not missing; they are recorded scope decisions.
2. **Three feature areas were never in scope** for this work: the load-test harness, the Python instrumentation, and the billing-statistics family.
3. **One de facto gap is not on any deferred list.** The `POST /internal/agent-runs` ingest pipeline has **no producer anywhere in the repository**. `agnet-service/` contains no call to it, and all five `dataQuality` flags are hard-coded `false` (`AgentStatisticsDtos.cs:20-24`). The C# side of Phase 4 is complete and tested, but it receives no data, so the agentic-statistics endpoints will serve empty series and null percentiles against real traffic. The documents describe this as "deferred per risk R-1" — which is accurate — but it is the one item that means a shipped feature does not actually function end to end.

```
dotnet build → Build succeeded. 0 errors (26 warnings, all pre-existing: obsolete Testcontainers ctors + EF1002 in tests)
dotnet test  → Failed: 0, Passed: 1162, Skipped: 0, Total: 1162
```

---

## 1 · What is complete

### 1.1 The twelve original defects (D-1 … D-12)

All closed. `docs/backend/backend-requirements.md:85-96` lists them; the load-bearing ones are verified in code and by test:

| Defect | Closure |
|---|---|
| **D-1 / D-2** — plan limit ignored, ledger seeded at the Seed tier | Single entitlement catalogue (`PlanEntitlementDefaults.cs:33`); the repository now takes the limit as a parameter (`UsageRepository.cs:37-49`, `:97-119`). |
| **D-3** — lost-update race on the Blossom balance | Two mechanisms: atomic `ExecuteUpdateAsync` (`UsageRepository.cs:56-64`) and an `xmin` optimistic token with a 5-attempt reload-and-retry (`BlossomService.cs:397-425`). Proven by `LedgerPostgresTests.TwentyParallelCredits_ProduceTheExactArithmeticSum` and `XminConcurrencyToken_RaisesOnStaleUpdate`. |
| **D-9** — doc/code drift in the permission catalogue | `docs/architecture/authorization.md` regenerated; the `org:principal` literal removed. |
| **D-11** — no correlation id | `CorrelationIdMiddleware` with `X-Request-Id`, validation, echo and agent propagation. |
| **D-12** — three copies of the plan-limit table | One catalogue; only a fallback literal remains. |

### 1.2 The six phases

| Phase | Status |
|---|---|
| 0 — foundations | Complete: correlation ids, audit table + redactor, permission catalogue, job lock, OTel + `/metrics`, `/health/live` + `/health/ready`. |
| 1 — pricing | Complete; `recompute` returns 501 by design (see §2). |
| 2 — ledger + entitlements | Complete: append-only ledger, O(1) projection, idempotency replay store, `IEntitlementResolver`, org/admin Blossom operations, plan change, expiry/rollover/cleanup jobs. |
| 3 — API access, users, orgs | Complete, including the three surfaces added in `#241`: audit read, admin org search, entitlement overrides. |
| 4 — agentic statistics | **C# complete; no data producer** — see §3. |
| 5 — API consumption statistics | Complete except the deferred load-test gate. |
| 6 — system statistics + alerts | Complete except the deferred billing-statistics family. |

### 1.3 Every finding from my three review passes — closed

The last two commits closed the remainder, so the review chain is now empty:

| Finding | Closure in `3791519` / `a542a5e` |
|---|---|
| **§2.1** lease failed *open* | Now fails **closed**: an unreachable lock store returns `503 { code: "idempotency-unavailable" }` instead of executing unguarded (`IdempotencyEndpointFilter.cs`), with the wait budget configurable (`Billing:IdempotencyLeaseWaitSeconds`) so the timeout path is testable. Four new unit tests in `IdempotencyEndpointFilterTests.cs`. |
| **§2.2** nullable `OrganizationId` could still collide | Index is now **partial** on `"OrganizationId" IS NOT NULL` (`LedgerConfigurations.cs:113-128`), with a migration that drops the `NULLS NOT DISTINCT` form. |
| **§2.4** override overlap was application-level only (TOCTOU) | A real database exclusion constraint: `EXCLUDE USING gist ("OrganizationId" WITH =, "Key" WITH =, tstzrange("EffectiveFrom","EffectiveTo",'[)') WITH &&)`, plus `DbUpdateException` → `409 { code: "override-overlap" }`. The Postgres test asserts **SQLSTATE `23P01`**, which is the correct way to prove a constraint fired. |
| **§2.3** over-broad OpenAPI normative claim | Scoped in `docs/api/README.md:48-56` to exclude `/internal/**`, the Development-only `/api/v1/policies/**` demo routes and the SignalR hubs. |
| **§2.5** three stale doc statements | All corrected: the `OnboardingPending` route list now names the real prefixes and the single `POST /api/v1/admin/requests` carve-out; `pageSize < 1` documented as falling back to 50; the admin state-change `reason` `minLength: 10` removed. Dead `PricingRecomputeResult` schema dropped; agent-run path param renamed to `runId`; the `state` legacy alias now documented in the OpenAPI parameter list. |

`docs/api/README.md` §A.4's "Known deviations" table was also extended with the three endpoints that fall back rather than clamp, so the stated convention and the code now agree.

---

## 2 · Deferred items (recorded, with reasons)

These are **conscious scope decisions**, each with a written reason and impact. Sources: `docs/backend/README.md:379-403` (medium findings), `:150`/`:204`/`:243-297`/`:372-377` (phase deviations), `docs/api/README.md:2110+` (Appendix P).

### 2.1 Missing endpoints — listed in Appendix P

| Endpoint | Consequence | Recorded at |
|---|---|---|
| `GET /orgs/{id}/statistics/billing/burn-rate` | 404 | `docs/api/README.md:2121` |
| `GET /orgs/{id}/statistics/customers/active`, `.../staff/seats` | 404 | `:2140` |
| `GET /admin/statistics/billing/profitability`, `/org-usage`, `/adjustments`, `/plan-changes`, `/downgrades` | 404 | `:2150+` |
| `GET /admin/pricing/rules/{id}/recompute` | Implemented as **501**; the compensating-ledger job is not scheduled | `:789`, `docs/backend/README.md:150` |
| `/internal/telemetry` (`POST /alerts`, `GET /quota/{id}`) | No route; telemetry is written directly | `implementation-plan.md:355` |
| 13 `/internal/visual/*` routes | No route (module is skeleton only) | `docs/api/README.md` §C |

Appendix P is explicit that these are **not** normative and "must not be reported as documented but missing" — which is the correct handling, and I am not reporting them as defects.

### 2.2 Deferred behaviour

| Item | Status | Recorded at |
|---|---|---|
| **M-2** committed `change-me-internal-token` default | Deferred; Development/compose only, handler fails closed when unset | `README:392` |
| **M-4** WhatsApp webhook replay window | **Partially fixed** — rate limit now runs after signature verification and the GET compare is constant-time; still no timestamp/nonce tolerance and no unique index on `InboundMessageLog.ExternalId` | `:393` |
| **M-5** no application-level rate limiting | Deferred; accepted risk SEC-M2, needs an infrastructure decision | `:394` |
| **M-8** price-book returns a bare array | Behaviour deferred (frontend contract change); now documented | `:395` |
| **M-10** `/system/overview` not cached | Claim corrected; cache still unimplemented | `:396` |
| **M-11** `/system/eventbus` ignores `from`/`to` | Deferred; documented | `:397` |
| **H-5** JWT audience disabled, no `azp`, 5-min clock skew | Deferred; accepted risk SEC-M1 | `:400` |
| **M-22** metric samples are organisation-agnostic | Deferred with a designed extension path (dimensions JSON) | `:367-371` |
| **FR-3.18** API-key `LastUsedAt` amortisation | Delivered in Phase 5 via `ApiKeyUsageAggregator` | `:204` |
| Hour→day compaction beyond 90 days; daily rollup | Deferred — the day row would collide with the 00:00 hour row | `:292` |
| `DailyAgentMetrics` rollup / `AgentStatsRollupJob` | Not implemented; percentiles computed on the fly | `:254-256` |
| **`Pricing:UseLegacyFormula` still `true`** | The pricing engine is inert until an operator flips it; documented as Decision (C-4) | `README:62` |
| Cross-instance pricing-cache invalidation | Deliberately not implemented; the 5 s TTL is the only cross-instance bound | `README:152-158` |
| 24 unarmed `PricingRecomputeResult`-adjacent items | — | — |

### 2.3 Deferred gates and harnesses

| Item | Recorded at |
|---|---|
| **Load-test harness** — the FR-6.3/BR-6.3 gate (5 000 req/s for 60 s, p99 telemetry overhead ≤ 1 ms) is a stated acceptance criterion, **not a measured one** | `README:297` |
| **Python instrumentation (G-1 … G-14)** — deferred per risk R-1 | `README:243` |

---

## 3 · The one real gap: no data producer for agentic statistics

This is the finding I would act on. It is *described* as deferred, but its consequence is not obvious from the deferral note.

- `POST /internal/agent-runs` (and `/steps`, `GET /{workflowId}`) exist, are tested, and are wired (`Program.cs`, `InternalAgentRunEndpoints.cs`).
- **Nothing calls them.** `grep -rn 'internal/agent-runs'` across `agnet-service/` and `frontend/` returns nothing.
- The `dataQuality` contract is hard-coded to all-false:

```csharp
LatencyInstrumented: false,
NodeFailuresObserved: false,
PerStepAttribution: false,
ToolInstrumented: false,
CostInstrumented: false);
```
— `Aveline.Api/Modules/Statistics/DTOs/AgentStatisticsDtos.cs:20-24`

Consequence: every agent-statistics response against real traffic carries `dataQuality` with all flags false, `GET /latency` returns a **null series** rather than zeros, and percentiles below the sample floor return `null`. `docs/backend/README.md:243-252` states this accurately ("nothing in `agnet-service/` emits run/step telemetry yet"), and the API is behaving exactly as designed — the design just has no upstream. A frontend that ignores `dataQuality` will render nothing useful; one that respects it will render "not instrumented", which is honest.

The Python service *does* still report Blossom usage (`agnet-service/app/services/usage_reporter.py`), so billing consumption is fed. It is only the richer agentic statistics that are unfed.

Note there is also a **catalogue/wire naming mismatch** on that contract, documented at `README:246-249`: the API uses `costInstrumented` where the catalogue says `costIsEstimated`, and `retryInstrumented`, `materialisedCounts`, `streamingRunsIncluded` and `unattributedRunsExcluded` are not emitted at all.

---

## 4 · Contract artefacts: 11 routes in code but not in `openapi.yaml`

`docs/api/README.md:48-56` now scopes the normative claim to **client-facing `/api/v1` routes**, excluding `/internal/**`, the Development-only demo routes and the SignalR hubs. Measured against that scoped rule, a route-by-route scan still finds **11 `/api/v1` operations present in code and absent from `openapi.yaml`**:

| Route | In `docs/api/README.md`? |
|---|---|
| `GET /admin/statistics/agents/{overview,runs,reliability}` | Yes (`:1546`) |
| `GET /admin/statistics/api/{requests,errors,latency,endpoints}`, `GET /admin/statistics/api-keys` | Yes (`:1843-1844`) |
| `GET /orgs/{id}/integrations/messages` | **No** — undocumented in both |
| `POST /users/onboarding` | Yes (`:408`) |
| `GET`/`POST /webhooks/whatsapp/{id}` | **No** — undocumented in both |

The eight admin-statistics routes are the same gap I reported each round: they are client-facing, team-only (`stats:system`), and documented in the README, but still absent from the machine contract. `GET /orgs/{id}/integrations/messages` and the two WhatsApp webhook operations are undocumented in **both** files, so they fail the repo's own reconciliation rule outright.

Also verified: `openapi.yaml` is structurally valid — 100 paths, 128 schemas, 146/146 `$ref`s resolve, no duplicate keys or `operationId`s.

---

## 5 · Residual risk register (accepted, not defects)

Carried from `docs/security/auth-security-review.md` and the deferred tables; listed so nothing is mistaken for an oversight:

| ID | Item | Why it is accepted |
|---|---|---|
| SEC-M1 / H-5 | JWT audience unvalidated, no `azp`, 300 s clock skew | Header-only tokens, no `__session` cookie, strict CORS allow-list |
| SEC-M2 / M-5 | No application-level rate limiting | Infrastructure decision |
| M-2 | Committed default internal token | Development/compose only; fails closed when unset |
| M-4 | WhatsApp webhook replayable indefinitely | Meta sends no timestamp; bounded by the 120/min limiter |
| — | Blossom exhaustion is not enforced | Deliberate per `ADR-010` §Decision 6, not reversed by this plan |
| — | `Pricing:UseLegacyFormula: true` | The pricing engine is inert until an operator flips it |

---

## 6 · Assessment

**Complete against its own specification.** Every committed defect is closed, every phase is delivered, and the eight findings from my review chain are all fixed — two of them verified by execution (headers on a handled 500; the backdate gate), and the newest additions verified by the right kind of test (a Postgres test asserting SQLSTATE `23P01` for the exclusion constraint is the correct proof, not a mock).

**Three things a reader of the status tables should not overlook:**

1. **Agentic statistics have no upstream.** The endpoints work; nothing feeds them. This is the only item where "deferred" masks a shipped feature that cannot function on real traffic.
2. **The pricing engine is off.** `Pricing:UseLegacyFormula: true` means every admin pricing operation succeeds and affects nothing. Documented as Decision (C-4) — but worth repeating in any handover.
3. **Eleven `/api/v1` routes are still outside the machine contract**, eight of them deliberately-built admin-statistics endpoints that the README documents and the OpenAPI does not.

**Recommended next, in order:** wire the Python instrumentation to `/internal/agent-runs` (it unlocks a whole delivered feature); add the eight admin-statistics paths plus the three wholly-undocumented routes to `openapi.yaml`; then decide the two policy items — the committed internal-token default and whether to measure the deferred load gate.
