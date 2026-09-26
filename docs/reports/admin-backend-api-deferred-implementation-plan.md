# Admin Backend API — Implementation Plan for Deferred and Missing Work

**Scope:** every item recorded as deferred, missing, or not-implemented in
[`admin-backend-api-implementation-status.md`](admin-backend-api-implementation-status.md),
plus the residual-risk register, each with a disposition recommendation.
**Baseline reviewed:** `a542a5e` (clean working tree, 6 untracked files only).
**Method:** read-only inspection of the code, EF configurations, migrations and docs at
HEAD, plus `git` inspection of the branch graph. Every factual claim below carries a
`path:line` citation; claims I could not verify are labelled **Inferred** or **Unverified**.
**Date:** 2026-09-13
**Estimates:** deliberately omitted — this is a sequence-and-dependency plan, per the request.

---

## 0 · How to read this plan

| Section | What it settles |
|---|---|
| §1 | The one correction to the status report that changes what to build |
| §2 | Facts the plan depends on, and how well each is established |
| §3 | Disposition for all 32 deferred, missing and residual-risk items, in one table |
| §4 | The phases and the numbered tasks |
| §5 | Dependency graph and suggested sequence |
| §6 | Decisions only the user can make |
| §7 | Risks, and how each would announce itself |
| §8 | What this plan does **not** cover |

Each task has the shape:

> **T-n.n — title** · *Fixes:* the report item · *Touches:* `path` files
> Work · Depends on · Acceptance · Verification

"Acceptance" is the condition that makes the task done; "verification" names the test or
command that proves it. Where an analogous test already exists, it is cited so the
implementer copies a real pattern rather than inventing one.

---

## 1 · Answer first

**The plan has 49 tasks across 8 phases. Three findings change its shape.**

**1. The 13 `/internal/visual/*` routes are not "a skeleton to build" — a working
implementation exists on `origin/catalog` and only needs porting.** The status report
calls the module "skeleton only" (report line 82). That is true of `feature/admin-backend-api`
but not of the repository: `Aveline.Api/Modules/VisualIntelligence/` at HEAD holds six
markdown files and no C# at all (`find Aveline.Api/Modules/VisualIntelligence -type f`),
while `origin/catalog` carries 46 C# files in that module, seven entities, a migration,
five test files, and the routes (`git ls-tree -r --name-only origin/catalog -- Aveline.Api/Modules/VisualIntelligence/`).
Moreover the Python agent in the current tree still calls `/api/internal/inventory/...`
(`agent-service/app/tools/registry.py:161-175`), and the catalog branch registers those
legacy paths as aliases (`origin/catalog:Aveline.Api/Endpoints/VisualEndpoints.cs:109-125`).
**Consequence:** T-4.1 is a port-and-reconcile task, not a from-scratch build. This is the
single largest change to the plan's cost and risk profile.

**2. The billing-statistics family needs a table that does not exist — and the report
understates it.** The catalog declares `DailyBillingMetrics` and `BillingRollupJob` as the
storage for S-4, S-5 and S-6 (`docs/backend/statistics-catalog.md:100,117,132,820,823`).
Neither exists: `grep -rn "DailyBillingMetrics\|BillingRollupJob" --include=*.cs Aveline.Api`
returns nothing. The report recorded these five endpoints as "deferred" without noting that
their documented data source is also absent. Their cost is a rollup, not five handlers.

**3. Two statistics are uncomputable as specified, for reasons that are not code volume.**
S-5 `margin_lkr` needs a USD→LKR rate, and no such rate exists anywhere in the repository
(`grep -rniE "usd_?to_?lkr|exchange_?rate" --include=*.cs --include=*.json Aveline.Api` →
no matches). S-9 `byViolatedKey` needs the violated entitlement key persisted, but
`ApiRequestLog` deliberately never stores response bodies (`Aveline.Api/Modules/Statistics/Models/ApiRequestLog.cs:10-12`)
and its `ErrorCode` column has no writer (`grep -n "ErrorCode" Aveline.Api/Modules/Statistics/Telemetry/ApiTelemetryMiddleware.cs`
→ no matches). Both need a decision or a schema change before a handler can be written.

**Also worth acting on immediately, unrelated to any deferred list:** a null `steps` array
on `POST /internal/agent-runs/{workflowId}/steps` throws `NullReferenceException` and
returns **500**, not 400 — `ValidateSteps` iterates its argument without a null check
(`Aveline.Api/Modules/Statistics/Services/AgentRunIngestService.cs:101,248-251`) and the
endpoint's `MapProblem` rethrows anything it does not recognise
(`Aveline.Api/Modules/Statistics/Endpoints/InternalAgentRunEndpoints.cs:98-107`).
This becomes reachable the moment Phase 1 wires a producer (T-1.7).

---

## 2 · Established facts and their confidence

### 2.1 Confirmed by direct read in this session

| Fact | Evidence |
|---|---|
| No producer calls `/internal/agent-runs`; all five `dataQuality` flags come from one all-false constant | `grep -rn "agent-runs" agent-service/` → only vendored `site-packages` hits; `Aveline.Api/Modules/Statistics/DTOs/AgentStatisticsDtos.cs:19-24` |
| The eight admin-statistics paths and all three internal agent-run paths are absent from `openapi.yaml` | `grep -c 'admin/statistics/agents' docs/api/openapi.yaml` → `0`; `grep -c 'admin/statistics/api'` → `0`; `grep -c '^  /internal'` → `0` |
| The admin-statistics routes do exist in code and are prefixed `/api/v1` | `AgentStatisticsEndpoints.cs:116`, `ApiStatisticsEndpoints.cs:132,181`; prefix at `Aveline.Api/Program.cs:135,161` |
| `GET /api/v1/orgs/{id}/integrations/messages` is undocumented in both files | `Aveline.Api/Endpoints/IntegrationEndpoints.cs:35`; absent from `openapi.yaml` and from `docs/api/README.md` |
| Daily rollup is blocked by a unique index that omits `WindowSize` | `Aveline.Api/Infrastructure/Data/Configurations/ApiConsumptionConfigurations.cs:52-64`; the job's own remarks at `Jobs/ApiStatsRollupJob.cs:15-18,123` |
| `DailyAgentMetrics` / `AgentStatsRollupJob` do not exist | `grep -rn "DailyAgentMetrics\|AgentStatsRollupJob" --include=*.cs Aveline.Api` → one docstring, `Services/AgentStatisticsService.cs:11` |
| `DailyBillingMetrics` / `BillingRollupJob` do not exist | `grep -rn "DailyBillingMetrics\|BillingRollupJob" --include=*.cs Aveline.Api` → no matches |
| `recompute` returns 501 and the enum member it waits on is declared but unused | `Aveline.Api/Modules/Billing/Endpoints/PricingEndpoints.cs:195-199`; `grep -rn "CorrectionRecompute" --include=*.cs .` → one hit, `Models/BlossomLedgerEntry.cs:31` |
| `ValidateAudience = false`, no `ClockSkew` set, no `azp` check anywhere | `Aveline.Api/Configurations/AuthenticationConfiguration.cs:96-104`; `grep -rn "azp\b\|ClockSkew" --include=*.cs Aveline.Api/ Aveline.Api.Tests/` → no matches |
| No `AddRateLimiter` / `EnableRateLimiting` anywhere; the two in-handler limiters both use a non-atomic, fail-open limiter | `grep -rn "AddRateLimiter\|EnableRateLimiting" --include=*.cs .` → no matches; `Aveline.Api/Endpoints/WebhookEndpoints.cs:115-124`, `Endpoints/OrganizationEndpoints.cs:497-507`, `Infrastructure/RateLimiting/DistributedRateLimiter.cs:6-10,61-66` |
| `ApiRequestLog.ErrorCode` has no writer | `grep -n "ErrorCode" Aveline.Api/Modules/Statistics/Telemetry/ApiTelemetryMiddleware.cs` → no matches |
| The catalog names an audit action the code never writes | `docs/backend/statistics-catalog.md:167` says `org.subscription.changed`; `Aveline.Api/Modules/Billing/Services/SubscriptionService.cs:129,141` writes `org.plan.changed` |
| No load/perf harness exists | `ls tests` → no such directory; `grep -rn "BenchmarkDotNet" --include=*.csproj .` → no matches |
| S-10's endpoint already exists and is already contracted | `Aveline.Api/Modules/Billing/Endpoints/SubscriptionEndpoints.cs:84,100`; `docs/api/openapi.yaml:1915` |

### 2.2 Confirmed via subagent investigation, cross-checked against the code

The following came from five parallel evidence tracks and were either independently
re-verified above or read directly from the cited implementation: the Python service has
**no** run/step model, no wall-clock timing (`time.perf_counter`/`monotonic` absent from
`app/` except the rate limiter), no tool/retry counting, no cost or cached-token plumbing,
and no paused-run reporting (`grep -rn -iE "interrupt|Command\(" app` → no hits); its
checkpointer is passed as a call-time kwarg while the graph is compiled bare
(`app/workflows/concierge_workflow.py:311-330`, `331-379`); `langgraph` is **not pinned**
in any project manifest (`requirements.txt:6` is bare; `pyproject.toml` has no dependency
section). `SystemMetricSample` has no organization column and the collector hard-codes
`{}` dimensions (`Modules/Statistics/Models/SystemStatisticsModels.cs:15-49`,
`Jobs/SystemMetricCollector.cs:183`), with the designed extension path in
`AlertService.LoadSamplesAsync` (`Services/AlertService.cs:470-473`). The WhatsApp handler
verifies the signature before the rate limit, uses a constant-time compare, has no
timestamp/nonce check, and does not catch `DbUpdateException`
(`Endpoints/WebhookEndpoints.cs:103-124,158`, `Modules/Integrations/Services/WebhookSignatureVerifier.cs:41-44`).

### 2.3 Inferred, and what would settle it

- **Inferred:** porting `origin/catalog` will conflict with HEAD's 63-commit lead. The
  merge base is `a2adc22` (2026-09-10); `origin/catalog` is 20 commits ahead and 63 behind.
  Its one migration, `20260910064834_AddVisualIntelligenceEntities`, predates HEAD's M1–M8
  and was generated against a different `AppDbContextModelSnapshot`. **Settles it:** running
  the port in a scratch branch (T-4.1).
- **Inferred:** the catalog branch's visual code targets the same auth model. It requires
  `AuthorizationConfiguration.InternalServicePolicy` (`origin/catalog:.../VisualEndpoints.cs:21`),
  which exists at HEAD (`Aveline.Api/Configurations/AuthorizationConfiguration.cs:110-115`).
  **Settles it:** the port compile.
- **Unverified from the two other branches:** whether `commerce`, `customer` and `inventory`
  tool READMEs at HEAD describe Python modules that were also never merged. Only
  `app/tools/inventory/` was checked — it holds `README.md` plus stale `__pycache__/*.pyc`
  for six modules whose `.py` files are untracked (`.gitignore:122-125`).

---

## 3 · Disposition for every deferred and residual item

Legend — **Build**: implement it. **Port**: recover existing work. **Accept**: keep as a
recorded deviation, no work. **Decide**: blocked on a human decision (§6).

| # | Item | Disposition | Task |
|---|---|---|---|
| 1 | `GET …/billing/burn-rate` (404) | Build, on-the-fly first | T-4.2 |
| 2 | `GET …/customers/active` (404) | Build + fix the unfiltered count | T-4.3 |
| 3 | `GET …/staff/seats` (404) | Build (smallest item in the plan) | T-4.4 |
| 4 | `GET /admin/statistics/billing/profitability` (404) | Build minus `marginLkr`; needs FX decision | T-4.5, D-5 |
| 5 | `…/billing/org-usage` (404) | Build; needs cross-org aggregation | T-4.6 |
| 6 | `…/billing/adjustments` (404) | Build; all inputs present | T-4.7 |
| 7 | `…/billing/plan-changes` (404) | Build; tier pair via audit JSON | T-4.8 |
| 8 | `…/billing/downgrades` (404) | Build partially; `byViolatedKey` needs T-4.10 | T-4.9, T-4.10 |
| 9 | `POST /admin/pricing/rules/{id}/recompute` (501) | Build the job | T-5.2 |
| 10 | `/internal/telemetry` (no route) | **Accept** — telemetry is written in-process by design | — |
| 11 | 13 × `/internal/visual/*` (no route) | **Port from `origin/catalog`** | T-4.1 |
| 12 | M-2 committed internal-token default | Build the guard; cheap and safe | T-8.1 |
| 13 | M-4 WhatsApp replay window | Build the index + the duplicate catch | T-8.2 |
| 14 | M-5 no application-level rate limiting | **Decide** (infrastructure) | D-7 |
| 15 | M-8 bare price-book array | **Accept**; it is a frontend contract change | — |
| 16 | M-10 `/system/overview` uncached | Build — ≥8 round-trips per call | T-7.1 |
| 17 | M-11 `eventbus` ignores `from`/`to` | Build, using the metric samples | T-7.2 |
| 18 | M-22 org-agnostic metric samples | **Accept**; design path exists and is documented | — |
| 19 | Hour→day compaction / 400-day daily rollup | Build | T-2.1, T-2.2 |
| 20 | `DailyAgentMetrics` / `AgentStatsRollupJob` | Build — it also bounds in-memory aggregation | T-2.3 |
| 21 | `DailyBillingMetrics` / `BillingRollupJob` | Build — **new item**; S-4/S-5/S-6 depend on it | T-2.4 |
| 22 | `Pricing:UseLegacyFormula: true` | Build the flip, sequenced after T-5.2 | T-6.1 |
| 23 | Cross-instance pricing-cache invalidation | Build, conditional on multi-instance deployment | T-6.2 |
| 24 | FR-6.3 load gate unmeasured | Measure report-only first | T-8.4 |
| 25 | Python instrumentation G-1…G-14 | Build — the top priority | Phase 1 |
| 26 | `dataQuality` catalogue/wire mismatch | Build the reconciliation | T-1.8, T-1.10, T-3.3 |
| 27 | 11 `/api/v1` routes outside the machine contract | Build, plus a CI guard | Phase 3 |
| 28 | `Quotas:EnforcementEnabled: false` | Build the rollout prerequisites, then decide | T-6.3, D-6 |
| 29 | SEC-M1 / H-5 JWT audience, `azp`, skew | **Decide** (Clerk token template) | D-3 |
| 30 | Blossom exhaustion unenforced (ADR-010 §6) | **Decide** (product policy) | D-2 |
| 31 | `UsageAccount.StaffCount` / `ActiveCustomerCount` never written | Build the counting path; clears `materialisedCounts` | T-4.12 |
| 32 | Python tool-path mismatch (`/api/internal/inventory/search`) | Reconcile during the port | T-4.1, D-10 |

Note on #11: the report lists `/internal/telemetry` as missing. Inspecting what exists
instead shows telemetry is written in-process — request telemetry by `ApiTelemetryWriter`
(`Aveline.Api/Modules/Statistics/Telemetry/ApiTelemetryWriter.cs:14,55`), system metrics by
`SystemMetricCollector` (`StatisticsModule.cs:63`), alerts by `AlertService`
(`StatisticsModule.cs:65`) — and only two `/internal/**` groups are mapped
(`Modules/Billing/Endpoints/UsageEndpoints.cs:18`, `Modules/Statistics/Endpoints/InternalAgentRunEndpoints.cs:18`).
Adding a remote-write endpoint for data the process already collects would create a second
write path with no consumer. Recommend leaving it out and correcting the plan document that
specifies it (`docs/backend/implementation-plan.md:355`).

Equally, #19 M-22: org-scoped alerting is an extension, not a defect. The seed rules are
system-wide, so the missing column costs nothing today; the extension path is written down
(`Services/AlertService.cs:452-458`) and the invariant is pinned by
`Aveline.Api.Tests/SystemStatisticsEntityConfigurationTests.cs:68-115`.

---

## 4 · Phases and tasks

### Phase 0 — Fix the cheap correctness bugs and unfreeze decisions

Nothing here is large; all four tasks are prerequisites for something later, and each is a
one-file change.

**T-0.1 — Guard the null `steps` array.** *Touches:*
`Aveline.Api/Modules/Statistics/Services/AgentRunIngestService.cs` (around `:101,248`).
Add a null guard to `ValidateSteps` that throws `AgentRunValidationException` (→ 400) rather
than dereferencing. *Depends on:* nothing. *Acceptance:* a body of
`{"organizationId": null, "steps": null}` on `POST /internal/agent-runs/{id}/steps` returns
400 with a message. *Verification:* extend `Aveline.Api.Tests/AgentRunIngestTests.cs`, which
already asserts the 401/400/409/413 mapping at `:89`.

**T-0.2 — Make the two uncomputable statistics decision-ready.** *Touches:* a short
addendum to `docs/backend/statistics-catalog.md` and `docs/api/README.md` Appendix P.
Record that S-5's `margin_lkr` has no FX source and S-9's `byViolatedKey` has no persisted
source, and mark both fields as "contract field, no data source" the way Appendix P already
handles unshipped endpoints. *Depends on:* nothing. *Acceptance:* a reader of Appendix P can
tell which response fields in the intended contract are not deliverable and why.
*Verification:* the two `grep` commands in §2.1 reproduced in the addendum.

**T-0.3 — Correct the audit-action name in S-8's documented source.** *Touches:*
`docs/backend/statistics-catalog.md:167`. Change `org.subscription.changed` to
`org.plan.changed`, matching the code (`SubscriptionService.cs:129,141`). *Depends on:*
nothing. *Acceptance:* an implementer filtering on the catalog's literal would match rows.
*Verification:* `grep -n 'org\.plan\.changed' Aveline.Api/Modules/Billing/Services/SubscriptionService.cs`.

**T-0.4 — Pin `langgraph`.** *Touches:* `agent-service/requirements.txt:6-7`,
`agent-service/pyproject.toml`. Today `langgraph` and `langgraph-checkpoint-postgres` are
bare, so the version the docs cite (`docs/backend/backend-requirements.md:89` says 1.2.11)
is not reproducible and defect D-5's behaviour cannot be asserted. Pin the installed version
exactly. *Depends on:* nothing. *Acceptance:* a fresh `pip install -r requirements.txt`
resolves to one version. *Verification:* `pip freeze | grep langgraph` against the pin;
`test-python` already enforces Ruff plus a 90 % pytest gate
(`.github/workflows/ci.yml:137-142`).

---

### Phase 1 — Give the agentic statistics a producer (the top priority)

This is the item the status report flags as the one shipped feature that cannot function.
It splits into four independent-ish streams: lifecycle fixes in the Python graph, run-level
capture, step-level capture, and the producer plus its truthful `dataQuality`.

#### Workstream 1A — Graph lifecycle (the hard prerequisite)

**T-1.1 — Decide and fix the checkpointer.** *Touches:*
`agent-service/app/workflows/concierge_workflow.py:311-330,371-379`,
`agent-service/app/workflows/checkpointer.py:41-60`, `agent-service/app/workflows/state_events.py:61-62`.
The graph is compiled with no checkpointer and the saver is passed as a call-time kwarg.
Confirm empirically whether that kwarg is honoured; if not, compile with the saver instead.
*Depends on:* T-0.4. *Acceptance:* a run's state survives a process restart — assert by
reading the checkpoint table after restart. *Verification:* a new test beside
`agent-service/tests/test_tracing.py`; the checkpoint table is created by
`create_checkpointer` (`checkpointer.py:41-60`).

**T-1.2 — Implement or explicitly stub the human-in-the-loop pause.** *Touches:*
`app/workflows/concierge_workflow.py:183-202` (`commerce_agent` returns
`needs_approval: False`), the pause path at `:12-14,187-189`.
`grep -rn -iE "interrupt|Command\(" app` finds no `interrupt(`/`Command(resume=...)`, so no
run can legitimately report `PausedForApproval`, and S-21 (`agentApprovalWaitTime`) has no
input. Either implement the pause or record in the plan that S-21 stays unmeasurable.
*Depends on:* T-1.1. *Acceptance:* either a run transitions to `PausedForApproval` and
resumes, or the deferred status is written down with S-21 named.
*Verification:* a test driving pause→resume, or an update to the status report.

#### Workstream 1B — Run-level capture

**T-1.3 — Introduce a run/step capture model in Python.** *Touches:* a new module under
`agent-service/app/` (suggested `app/telemetry/`), plus `app/schemas/`. This is the missing
data model: `grep -rn -iE "step_index|agentstepreport|agentrunreport|attempt_number" app`
finds only a field declaration (`app/schemas/response.py:28`). Model the fields the C#
contract requires — the full list is in `Aveline.Api/Modules/Statistics/DTOs/AgentRunIngestDtos.cs:10-65`.
*Depends on:* nothing. *Acceptance:* a dataclass/Pydantic model per run and per step with
the exact wire field names. *Verification:* unit test asserting the serialised key set
matches the DTO names.

**T-1.4 — Capture wall-clock timing and the run identity.** *Touches:*
`app/api/agents.py:93` (the existing `request_id`), `:117-126` (the post-run call site),
`app/workflows/state_events.py:63-78`. Add `perf_counter`-based start/end, populate
`StartedAt`, `CompletedAt`, `DurationMs`. There is no timing source today — the only
`time.*` call in `app/` is the rate limiter's window (`app/middleware/rate_limit.py:58`).
*Depends on:* T-1.3. *Acceptance:* a completed run reports a duration within tolerance of
the wall-clock elapsed time. *Verification:* unit test with a stubbed clock.

**T-1.5 — Capture per-node steps, failures, tool calls, retries and token attribution.**
*Touches:* `app/workflows/state_events.py:63-78` (the existing `astream_events(v2)` loop
already reads `metadata.langgraph_node` on `on_chain_start` and the final `on_chain_end`),
`app/agents/customer_memory/nodes.py:415-419` (real `usage_metadata` capture),
`app/workflows/concierge_workflow.py:245-265` (the token mapping), and the
`app/api/agents.py:229-244` stream loop. Add a `try/except` around the node loop so node
failures set a step `Failed` status and an `ErrorCode` rather than being swallowed.
*Depends on:* T-1.3, T-1.4. *Acceptance:* a run that fails in one node reports that node as
`Failed` with an error code and the run as `Failed`. *Verification:* unit test injecting a
failing node; combine with the existing `test_tracing.py` span assertions.

**T-1.6 — Count tool calls and retries explicitly.** *Touches:* the capture module from
T-1.3 and the step loop from T-1.5.
`grep -rn --include=*.py -E "on_tool_|callbacks=" app` returns only event-name strings
(`app/api/agents.py:243`), no callback handler. *Depends on:* T-1.5. *Acceptance:*
`toolCallCount` and `retryCount` are non-zero for a run that calls a tool and retries once.
*Verification:* unit test with a tool stub invoked twice.

#### Workstream 1C — The producer

**T-1.7 — Ship the run-report producer.** *Touches:* `app/services/usage_reporter.py`
(the reusable transport: it already builds `{settings.api_base_url}/internal/usage/record`
with `X-Internal-Token` at `:45-50`, camelCase payload at `:52-62`, a 10 s
`httpx.AsyncClient` at `:66`, no retry/batching/queue), `app/api/agents.py:120-126,152-185`
(the existing best-effort reporting call site and its swallow-failures shape),
`app/core/config.py:17-18` (the token and base URL already exist — no new config needed).
Post a complete run to `/internal/agent-runs` after each terminal run. *Depends on:*
T-1.3–T-1.6, T-0.1. *Acceptance:* a real agents-service run creates an `AgentWorkflowRuns`
row with steps. *Verification:* a Python test against a stub transport asserting the payload
shape, and an integration test asserting the C# side accepts it. Copy the privacy invariant
from `Aveline.Api.Tests/AgentRunPrivacyTests.cs` — the payload must carry no prompt text,
tool arguments or tool results, only `argsHash`/`resultBytes` (`AgentRunIngestDtos.cs:6-9`).

**T-1.8 — Make `dataQuality` truthful.** *Touches:*
`Aveline.Api/Modules/Statistics/DTOs/AgentStatisticsDtos.cs:11-25`,
`Aveline.Api/Modules/Statistics/Services/AgentStatisticsService.cs` (the 12 emission sites
listed in §2), `Services/AgentRunIngestService.cs:165`. Replace the single
`AgentDataQualityDto.Uninstrumented` constant with values derived from whether the
instrumentation exists. *Depends on:* T-1.7. *Acceptance:* `latencyInstrumented`,
`toolInstrumented`, `perStepAttribution`, `nodeFailuresObserved` and `retryInstrumented`
become `true` when the data is present; `costInstrumented` stays `false` until T-1.9 and
`materialisedCounts` stays `false` until T-4.12. *Verification:* a test
asserting a run with steps flips exactly the flags its data supports.
**Do not** do this before T-1.7 — a flag set `true` ahead of the data turns an honest null
into a false zero, which is the exact failure the catalog warns about
(`docs/backend/statistics-catalog.md:783-785`).

**T-1.9 — Feed actual cost and cached tokens.** *Touches:*
`app/services/usage_reporter.py:20` (`actual_cost_usd` defaults to `0.0`), the caller at
`app/api/agents.py:172-180` (passes neither `cached_tokens` nor `actual_cost_usd`), and the
cost path in the capture model. *Depends on:* T-1.7. *Acceptance:* `actualCostUsd` is
non-zero for a run whose provider reports a cost, or the field stays documented as
estimated. *Verification:* unit test; and keep `costInstrumented: false` until this lands,
because S-5 and S-19 depend on it (`docs/backend/statistics-catalog.md:120,789`).

**T-1.10 — Close the `dataQuality` catalogue/wire naming gap.** *Touches:*
`docs/backend/statistics-catalog.md:789-797` and the emitted DTO.
The catalogue defines `costIsEstimated` with **inverted polarity** ("`true` means cost is a
placeholder") while the wire emits `costInstrumented`; `retryInstrumented`,
`materialisedCounts`, `streamingRunsIncluded` and `unattributedRunsExcluded` are catalogued
but not emitted. *Depends on:* T-1.8. *Acceptance:* the table at `statistics-catalog.md:789`
lists exactly the names and polarity the API emits. *Verification:* a test asserting the
serialised `dataQuality` key set equals a literal list, so the two cannot drift again.

#### Workstream 1D — Boundedness

**T-1.11 — Report `unattributedRunsExcluded`.** *Touches:* `AgentStatisticsService`, the
ingest resolution path (`AgentRunIngestService.cs:457-471`), `AgentRunUnattributedTests.cs`
(already exists). *Depends on:* T-1.8. *Acceptance:* the flag is `false` and org-scoped
numbers exclude unattributed runs, or the exclusion is shown in the response.
*Verification:* extend `Aveline.Api.Tests/AgentRunUnattributedTests.cs`.

---

### Phase 2 — Rollups and retention

**T-2.1 — Make the API-stats unique index admit a day row.** *Touches:*
`Aveline.Api/Infrastructure/Data/Configurations/ApiConsumptionConfigurations.cs:52-64` and a
new migration. The index `IX_ApiRequestMetrics_Dimensions` covers
`{OrganizationId, ApiKeyId, UserId, RouteTemplate, HttpMethod, StatusCode, WindowStart}` and
**omits `WindowSize`**, so a `day` row at `00:00` collides with the `hour` row at the same
`WindowStart` — the exact cause the job documents (`Jobs/ApiStatsRollupJob.cs:15-18`).
Add `WindowSize` to the key. *Depends on:* nothing. *Acceptance:* a `day` row and an `hour`
row can coexist at the same `WindowStart`. *Verification:* the index DDL is already asserted
against `pg_indexes` in `Aveline.Api.Tests/ApiConsumptionPostgresTests.cs:75`; extend that
assertion and add a rollup-at-midnight test to `ApiStatsRollupJobTests.cs:19-30`.
Follow the hand-written-migration pattern of `20260913134710_AddOverrideNoOverlapConstraintAndPartialIdempotencyIndex.cs:17-22`.

**T-2.2 — Produce the daily rollup.** *Touches:* `Jobs/ApiStatsRollupJob.cs` — add a
`RecomputeDayAsync` over the 24 hour rows, reusing `Rebuild` (`:82-132`) and the
delete/insert two-statement pattern (`:71-77`) so the job stays idempotent (BR-6.10).
*Depends on:* T-2.1. *Acceptance:* a closed day has exactly one `WindowSize = "day"` row per
dimension set, and re-running the job changes nothing. *Verification:* extend
`ApiStatsRollupJobTests.cs`. Note `ApiStatsRetentionJob.cs:50,76` already reads a `"day"`
window, so retention starts working the moment the rows exist.

**T-2.3 — Add `DailyAgentMetrics` and `AgentStatsRollupJob`.** *Touches:* a new entity +
`DbSet` (`Infrastructure/Data/AppDbContext.cs`), a configuration
(`Infrastructure/Data/Configurations/`), a migration, a job registered in
`StatisticsModule.cs:55-65` beside `AgentStatsRetentionJob`, and a repoint of
`AgentStatisticsService.GetLatencyAsync` (`:75-120`). Today aggregation materialises every
run and step in the window in memory (`QueryRunsAsync`/`QueryStepsAsync` at `:57,125,184,229,262,283`),
which is the second reason to build this beyond the catalog's S-16.
*Depends on:* Phase 1 (the rollup needs rows). *Acceptance:* a latency query for a window
older than the rollup horizon reads the rollup, and the numbers match the on-the-fly path.
*Verification:* a rollup-idempotency test mirroring `ApiStatsRollupJobTests.cs:19-30`, plus a
parity test against `AgentStatisticsQueryTests.cs` and `PercentileCalculatorTests.cs`.

**T-2.4 — Add `DailyBillingMetrics` and `BillingRollupJob`.** *Touches:* new entity,
configuration, migration, and a hosted job. **Newly identified in this plan** — the catalog
declares this table as the source for S-4, S-5 and S-6
(`statistics-catalog.md:100,117,132,820,823`) and it does not exist
(`grep -rn "DailyBillingMetrics\|BillingRollupJob" --include=*.cs Aveline.Api` → no matches).
Shape: keyed on `(OrganizationId, Day, <dimensions>)` with pre-computed count/sum/percentile
columns, as `statistics-catalog.md:831-838` specifies. *Depends on:* nothing (it reads
`AiUsageRecords`, which exist). *Acceptance:* a closed day has one row per
organization × dimension; `BlossomUnits` sums match `AiUsageRecords` for the day.
*Verification:* a Postgres rollup test modelled on `ApiConsumptionPostgresTests.cs`; the
daily-sum invariant asserted against a seeded set.

---

### Phase 3 — Contract reconciliation

**T-3.1 — Add the eight admin-statistics paths to `openapi.yaml`.** *Touches:*
`docs/api/openapi.yaml`. These are client-facing, `stats:system`-gated, README-documented
(`docs/api/README.md:1546,1843-1844`) and absent from the machine contract
(`grep -c 'admin/statistics/agents' docs/api/openapi.yaml` → `0`). Paths and handlers:
`/api/v1/admin/statistics/agents/{overview,runs,reliability}`
(`AgentStatisticsEndpoints.cs:120,128,136`) and
`/api/v1/admin/statistics/api/{requests,errors,latency,endpoints}` plus
`/api/v1/admin/statistics/api-keys` (`ApiStatisticsEndpoints.cs:136,147,158,169,184`).
*Depends on:* nothing. *Acceptance:* each path resolves in a validator and mirrors the
shipped parameters — note that the admin variants take no `organizationId` (it is passed as
`null` to `Build`), which differs from the org-scoped twins. *Verification:* the generated
document at `/openapi/v1.json` (`Aveline.Api/Program.cs:34,105`) diffed against the YAML.

**T-3.2 — Document the three wholly-undocumented routes.** *Touches:*
`docs/api/openapi.yaml` and `docs/api/README.md`. `GET /api/v1/orgs/{id}/integrations/messages`
(`Aveline.Api/Endpoints/IntegrationEndpoints.cs:35`), `POST /api/v1/users/onboarding`
(`Endpoints/UserEndpoints.cs:36`) and `GET`/`POST /api/v1/webhooks/whatsapp/{id}`
(`Endpoints/WebhookEndpoints.cs:34`) appear in neither file, so they fail the repository's own
reconciliation rule (`docs/api/README.md:48-56`). *Depends on:* nothing. *Acceptance:* all
four operations appear in both documents or are explicitly excluded with a reason.
*Verification:* the CI check from T-3.5.

**T-3.3 — Reconcile the shape drifts.** *Touches:* `docs/api/openapi.yaml`. Three confirmed:
`DataQuality` documents nine flags while the wire emits five with one renamed and inverted
(`openapi.yaml:3884-3892` vs `AgentStatisticsDtos.cs:11-25`, and T-1.10);
`AgentReliabilityResponse` documents a `series` array the wire does not return
(`openapi.yaml:4697-4719` vs `AgentStatisticsDtos.cs:132-142`); and `/cost` documents a
`groupBy` parameter no code accepts (`openapi.yaml:2637-2645`). *Depends on:* T-1.10.
*Acceptance:* for each, either the YAML or the code changes, and a test pins the chosen
shape. *Verification:* extend the route/shape tests in
`Aveline.Api.Tests/AgentStatisticsEndpointsTests.cs`.

**T-3.4 — Close the internal-surface decision consistently.** *Touches:*
`docs/api/README.md:48-56`, `docs/api/openapi.yaml`. The scoping rule already excludes
`/internal/**` from the normative claim, so the three internal agent-run paths are correctly
outside the contract — but the YAML's commented "Planned / not yet implemented" appendix
should say so, rather than leaving a reader to infer it. *Depends on:* nothing.
*Acceptance:* one paragraph states that `/internal/**` is documented in prose only and why.
*Verification:* read-back against `docs/OpenApi/README.md:27`.

**T-3.5 — Add the CI reconciliation guard the docs already ask for.** *Touches:*
`.github/workflows/ci.yml` (a new job with `needs: hygiene`), plus a small script.
`docs/api/README.md:66-67` states: *"A CI check that diffs the two documents for shipped
endpoints should be added."* The generated document already exists in Development
(`Aveline.Api/Program.cs:105`, served at `/openapi/v1.json`), so the job can boot the API the
way `zap-baseline` does (`ci.yml:327-350`) and diff the `/api/v1` path set against
`docs/api/openapi.yaml`. *Depends on:* T-3.1, T-3.2. *Acceptance:* the job fails when a
shipped `/api/v1` route is missing from the YAML. *Verification:* deliberately remove a path
from the YAML in a scratch branch and observe the failure.

---

### Phase 4 — The missing statistics endpoints and the visual surface

**T-4.1 — Port the Visual Intelligence implementation from `origin/catalog`.**
*Touches:* `Aveline.Api/Modules/VisualIntelligence/**`, `Aveline.Api/Endpoints/VisualEndpoints.cs`,
`Aveline.Api/Infrastructure/Data/Configurations/Inventory*Configuration.cs`, a **regenerated**
migration, `Aveline.Api/Program.cs:73,113` on the source branch, and the Python tools.
The source branch has 46 C# files in the module, seven entities, a migration, five test
files, and 13 `/internal/visual/*` routes plus alias groups
(`origin/catalog:Aveline.Api/Endpoints/VisualEndpoints.cs:21-137`).
**Do not port the migration file.** `20260910064834_AddVisualIntelligenceEntities` was
generated against the catalog branch's `AppDbContextModelSnapshot`, which predates HEAD's
M1–M8; porting it would leave the snapshot describing a model missing 63 commits of schema.
Copy the entity and configuration classes, then generate a fresh migration against HEAD.
*Depends on:* nothing. *Acceptance:* the 13 routes resolve under `InternalServicePolicy`,
the entities migrate cleanly onto HEAD's schema, and the ported tests pass.
*Verification:* the ported `VisualEndpointsIntegrationTests.cs`,
`VisualIntelligenceEntityConfigurationTests.cs`, `VisualIntelligencePostgresTests.cs`,
`InventoryRepositoryTests.cs` and `InventoryServiceTests.cs`; plus a route-table assertion
that all 13 paths resolve and 401 without the internal token.
**Open sub-question to settle during the port:** the Python registry currently calls
`/api/internal/inventory/search` (`agent-service/app/tools/registry.py:161`), a path with the
legacy prefix that the docs do not list. Decide whether the canonical Python target becomes
`/internal/visual/inventory/search` and update `registry.py`, or the alias is kept. The
documented list at `docs/api/README.md:678-688` already omits the catalog branch's
`/internal/visual/search-inventory` alias, so the alias set needs a decision either way.

**T-4.2 — Burn rate (S-4).** *Touches:* a new endpoint on `BlossomEndpoints` or a new
statistics endpoint file, a DTO, and a service method.
Numerator and denominator already exist: `IBlossomService.GetUsageAsync` returns a per-day
`BlossomUnits` series (`Services/BlossomService.cs:338-374`) and `GetBalanceAsync` gives
available (`:206`); window validation with a 92-day cap exists at
`Endpoints/BlossomEndpoints.cs:19,266-277`.
Build it on the fly first (the volumes do not justify T-2.4 as a blocker); switch to the
rollup after T-2.4 if needed. *Depends on:* nothing. *Acceptance:* `window` accepts
`7d|14d|30d`; `projectedExhaustionAt` is `null` when `burnRatePerDay` is 0 — never a
sentinel date (`statistics-catalog.md:104`). *Verification:* endpoint test modelled on
`Aveline.Api.Tests/BlossomEndpointsIntegrationTests.cs`.

**T-4.3 — Active customer count (S-11) — and fix the unfiltered count it duplicates.**
*Touches:* a new endpoint, DTO and service method, plus
`Aveline.Api/Modules/Billing/Services/SubscriptionService.cs:212-213,286-291`.
The existing count is a bare `Count(c => c.OrganizationId == org)` with **no date
predicate**, so it does not implement the 90-day activity rule
(`docs/ADR/ADR-010-usage-tracking-architecture.md:71-74`, `statistics-catalog.md:212`).
Implement the real definition — a distinct customer with a `CustomerInteraction`, `Order`,
`Conversation`/`Message` or profile update inside 90 days
(`CustomerInteraction.cs:32`, `Order.cs:52`, `Conversation.cs:35`, `Message.cs:47`,
`Customer.cs:32,38`) — and use it for both this endpoint and the entitlement-usage response
so the two cannot disagree. *Depends on:* the D-1 definition. *Acceptance:* a customer whose
last activity is 91 days old is excluded. *Verification:* a seeded test with fixtures at
89/90/91 days; there is no booking table to consider
(`grep -rln "Appointment\|Booking" --include=*.cs Aveline.Api` → nothing).

**T-4.4 — Staff seats (S-12).** *Touches:* a new endpoint and DTO; reuse the count at
`Aveline.Api/Modules/Billing/Services/SubscriptionService.cs:209-211`. The smallest item in
this plan: add the `boutiqueRole` breakdown and a 5-minute cache
(`statistics-catalog.md:230,234`). *Depends on:* nothing. *Acceptance:* the count equals
`COUNT(*) FROM OrganizationMemberships WHERE Status = 'Active'`. *Verification:* endpoint
test; the route/DTO shell to copy is `SubscriptionEndpoints.cs:100-117`.

**T-4.5 — Profitability (S-5).** *Touches:* a new admin statistics endpoint, DTO, and
service. Inputs: `AiUsageRecord.ActualCostUsd/BlossomUnits/Provider/Model/CreatedAt`
(`Models/AiUsageRecord.cs:33,36,51,58,84`), `BlossomPriceEntry.PriceLkr`
(`BlossomPriceEntry.cs:23`), `Organization.PlanTier` (`Organization.cs:46`),
`UsageAccount.PlanTierSnapshot` (`UsageAccount.cs:71`). `AiUsageRecord` carries no plan tier,
so `planTier` grouping needs the join. **`marginLkr` is not deliverable** (T-0.2, D-5):
either omit the field, or introduce an FX rate source. *Depends on:* D-5.
*Acceptance:* series emitted per requested `groupBy`; `dataQuality` reports cost as
estimated while `ActualCostUsd` is always 0 (`statistics-catalog.md:120`).
*Verification:* seeded aggregation test asserting sums and the grouping dimension.

**T-4.6 — Org usage ranking (S-6).** *Touches:* a new endpoint, DTO, and a **cross-org**
repository method. Every existing ledger and usage read is org-scoped
(`Repositories/BlossomLedgerRepository.cs:124`, `Repositories/IUsageRepository.cs:38-49`),
so this needs a genuinely new query shape with `rank()`, paging and `PageMeta`. Reuse the
paging normaliser (`Modules/Statistics/Domain/ApiStatisticsValidation.cs:18`) and the
`PageMeta` DTO pattern (`DTOs/SystemStatisticsDtos.cs`). *Depends on:* nothing.
*Acceptance:* orgs ranked by `Σ BlossomUnits` over the window with correct paging.
*Verification:* seeded test asserting rank order and page boundaries.

**T-4.7 — Adjustment activity (S-7).** *Touches:* a new endpoint, DTO, and cross-org ledger
aggregation. **All inputs already exist**, including the exact index the catalog requires —
`(OrganizationId, EntryType, CreatedAt)` at `Infrastructure/Data/Configurations/LedgerConfigurations.cs:67`.
Needs: cross-org filtering, an `actorUserId` filter, `Σ|BlossomDelta|`, day bucketing, and a
`byActor` grouping. *Depends on:* nothing. *Acceptance:* a spike in `AdminCredit` for one
actor is visible in `byActor`. *Verification:* seeded ledger fixtures asserting counts,
absolute sums and the actor breakdown.

**T-4.8 — Plan change history (S-8).** *Touches:* a new endpoint, DTO, and service.
Source is `BlossomLedgerEntries` (`EntryType ∈ {PlanUpgradeProration, PlanDowngradeAdjustment}`,
`Models/BlossomLedgerEntry.cs:25,28`) plus `AuditLogEntries`, whose query surface already
supports every filter needed (`Modules/Audit/Repositories/IAuditRepository.cs`).
**The tier pair is only recoverable by parsing audit `BeforeJson`/`AfterJson`** — `fromTier`
and `toTier` are not columns (`AuditLogEntry.cs:32,35`; `SubscriptionService.cs:141-148`).
*Depends on:* T-0.3. *Acceptance:* `items` reports real from/to tiers; a malformed audit
payload degrades to a null tier rather than throwing. *Verification:* seeded audit rows with
valid and malformed JSON.

**T-4.9 — Downgrade statistics (S-9).** *Touches:* a new endpoint and DTO, plus T-4.10 for
the breakdown. The producer of the 409 is `PlanLimitViolationException` returned as
`code: "plan-limit-violation"` with `violations[]`
(`Endpoints/SubscriptionEndpoints.cs:145-150`, `Services/ISubscriptionService.cs:62-66`).
`attempted` and `blocked` are derivable from `ApiRequestMetrics` by route and status class,
but note that the raw-log retention is 7 days (`Jobs/ApiStatsRetentionJob.cs:32`) while S-9
asks for 90 — so the metric rollup, not the raw log, must be the source. `byViolatedKey` is
**not** derivable — see T-4.10. *Depends on:* T-4.10 for the breakdown.
*Acceptance:* `attempted`, `blocked` and `blockedRate` are correct; `byViolatedKey` is
either correct or listed in `omitted` the way other unmeasurable fields are
(`docs/backend/statistics-catalog.md:766-770`). *Verification:* seeded `ApiRequestMetrics`
rows with mixed 200/409 outcomes.

**T-4.10 — Persist the violated entitlement key, or accept the gap.** *Touches:*
`Aveline.Api/Modules/Statistics/Models/ApiRequestLog.cs`,
`Telemetry/ApiTelemetryMiddleware.cs:82-103`, `Telemetry/ApiRequestAggregator.cs:88`, and a
migration. Today `ApiRequestLog.ErrorCode` exists but is never assigned, and
`ApiRequestLog` deliberately stores no response body (`ApiRequestLog.cs:10-12`), so
`byViolatedKey` has no source. Also note `Telemetry:RawLogRetentionDays` is 7
(`Jobs/ApiStatsRetentionJob.cs:32`) while S-9 asks for 90 days, so a raw-log-based
implementation would be truncated. Recommended mechanism: have
`SubscriptionEndpoints` stamp the violated key into the telemetry sample (the field already
exists) rather than persisting bodies. *Depends on:* nothing. *Acceptance:* either a 409 from
`change-plan` records the violated key, or T-4.9 marks the field omitted with the reason.
*Verification:* an integration test asserting a 409's key is queryable.

**T-4.11 — Wire the already-ported visual tools to the API paths chosen in T-4.1.**
*Touches:* `agent-service/app/tools/registry.py:161-175` and the ported tool modules.
*Depends on:* T-4.1. *Acceptance:* the Python agent calls paths that exist and 200.
*Verification:* a Python test against a stubbed transport asserting the exact URL per tool;
`test-python` enforces the 90 % coverage gate (`ci.yml:140-142`), so these tests are
mandatory, not optional.

**T-4.12 — Write the materialised entitlement counts.** *Touches:*
`Aveline.Api/Modules/Billing/Models/UsageAccount.cs:86` (`StaffCount`,
`ActiveCustomerCount`), the counting job, and the `materialisedCounts` flag.
Both columns exist and **nothing writes them** (`grep -rn "StaffCount\|ActiveCustomerCount"
--include=*.cs Aveline.Api` → the model plus migration designer/snapshot only). The
catalogue already requires this work and names the flag that reports it
(`docs/backend/statistics-catalog.md:205,795`), and the API currently cannot emit that flag
at all. Uses the counts from T-4.3 and T-4.4, so build it after them. *Depends on:* T-4.3,
T-4.4. *Acceptance:* both columns are populated within the documented 5-minute freshness
window, and `materialisedCounts` is emitted as `true`. *Verification:* a job test asserting
the columns after a seeded membership/customer change; extend
`Aveline.Api.Tests/AgentStatsJobsTests.cs` or the billing job tests for the harness pattern.

---

### Phase 5 — Pricing engine activation

**T-5.1 — Index usage rows by pricing rule.** *Touches:*
`Aveline.Api/Infrastructure/Data/Configurations/BillingConfigurations.cs:51,59` and a
migration. The snapshot columns exist (`Models/AiUsageRecord.cs:61-76`, migration
`20260911172513_AddAiUsageRecordPricingSnapshot.cs:15-76`) but there is no index on
`PricingRuleId`, so the recompute query would be a scan. *Depends on:* nothing.
*Acceptance:* a partial index on `PricingRuleId WHERE "PricingRuleId" IS NOT NULL`.
*Verification:* assert against `pg_indexes` following
`ApiConsumptionPostgresTests.cs:75`.

**T-5.2 — Implement the compensating-ledger recompute job.** *Touches:*
`Aveline.Api/Modules/Billing/Endpoints/PricingEndpoints.cs:193-199` (replace the 501),
`Models/BlossomLedgerEntry.cs:31` (`CorrectionRecompute` — declared and never used), a new
job class, `Services/IPricingService.cs:63-99` (add the recompute operation), and
`Modules/Audit/Models/AuditAction.cs:13` (`PricingRuleRecomputed` — declared and unused).
The job must: select `AiUsageRecords` for the rule, recompute Blossom units from the
corrected rule, and write compensating `CorrectionRecompute` entries. *Depends on:* T-5.1.
*Acceptance:* recomputing a rule produces ledger corrections whose sum equals the delta
between old and new pricing, and an audit entry is written. *Verification:* a Postgres test
modelled on `PricingActivationPostgresTests.cs`; assert the ledger delta explicitly.
*Note:* while `Pricing:UseLegacyFormula` is `true` the snapshot columns stay NULL
(`Aveline.Api/Modules/Billing/Services/UsageTrackerService.cs:131-152`), so a recompute run
today finds zero rule-priced rows. Sequence T-6.1 after this only if backfill is required.

**T-5.3 — Decide the backfill policy.** *Touches:* documentation plus, if required, a
one-shot backfill job. Rows ingested under the legacy formula carry a NULL pricing snapshot,
so flipping the flag does not retroactively price them
(`docs/backend/README.md:150`). Decide: leave history unpriced, or backfill via T-5.2 with a
cutover date. *Depends on:* T-5.2. *Acceptance:* the decision is recorded where the flag is
documented (`implementation-plan.md §10.6`). *Verification:* read-back.

---

### Phase 6 — Engine and enforcement rollout

**T-6.1 — Flip `Pricing:UseLegacyFormula` to `false`.** *Touches:*
`Aveline.Api/appsettings.json:18`. The only reader is
`UsageTrackerService.cs:131`, and the no-rule-match path is already safe:
`PricingService.ResolveAsync` returns `PricingRuleSnapshot.Fallback` with `IsFallback: true`
and logs `pricing.rule.missing` (`Services/PricingService.cs:36-43`), after which the
snapshot is stored as NULL (`UsageTrackerService.cs:150-152`). Cache warmth is not a
correctness prerequisite — `PricingRuleCacheWarmer` runs every 60 s
(`Services/PricingRuleCacheWarmer.cs:15,47-66`) and `ResolveAsync` falls through to the
database on a miss (`PricingService.cs:35`). *Depends on:* T-5.3. *Acceptance:* new
`AiUsageRecords` carry non-NULL `PricingRuleId`/`NormalizedUnits` where a rule matches.
*Verification:* `UsagePricingSnapshotTests.cs:35` and `UsagePricingTests.cs:38` already
toggle the key via `UseSetting`, so the flip is already covered — extend them to assert the
snapshot is non-null on the non-legacy path.

**T-6.2 — Cross-instance pricing-cache invalidation (conditional).** *Touches:*
`Aveline.Api/Modules/Billing/Domain/PricingRuleCache.cs:10,14,33,35-36` and
`Services/PricingService.cs:89,137,190,214`. Today `Invalidate()` increments a process-local
generation counter and the cache holds a 5-second TTL, so a rule change on instance A is
visible on B only after the TTL. **Do this only if the API is deployed with more than one
replica.** If it is, publish a `pricing.rule.changed` event from the four mutation sites and
add a hosted subscriber that calls `Invalidate()`. *Depends on:* the deployment topology
question. *Acceptance:* two cache instances converge immediately after one mutates.
*Verification:* a two-instance unit test; harness precedent
`Aveline.Api.Tests/PricingRuleCacheWarmerTests.cs`.

**T-6.3 — Prepare quota enforcement, then decide.** *Touches:*
`Aveline.Api/appsettings.json:66-68`, `Services/QuotaService.cs:50-53,138-152`, and the
entitlement seed. `Quotas:EnforcementEnabled` ships `false`
(`appsettings.json:68`) and `QuotaService` treats a resolved limit of `0` as unlimited
(`:50-53`), so flipping the flag alone changes nothing for orgs with no
`api.requests.*` entitlement. Required order: populate the entitlements, confirm the counter
store is reachable, then flip. With enforcement on, any `IQuotaCounterStore` exception marks
the meter exhausted (`:71-79`), so a Redis outage becomes org-wide 429s — confirm Redis
placement first. *Depends on:* D-6. *Acceptance:* an org over its monthly limit is rejected
429 only when enforcement is on and its entitlement is set. *Verification:*
`Aveline.Api.Tests/ApiQuotaEnforcementTests.cs:42,170` already drives both flag states;
`QuotaServiceTests.cs:75` covers fail-closed.

---

### Phase 7 — Caching and windowed system metrics

**T-7.1 — Cache `/admin/statistics/system/overview`.** *Touches:*
`Aveline.Api/Modules/Statistics/Services/SystemStatisticsService.cs:47-82` and
`Endpoints/SystemStatisticsEndpoints.cs:29-30`. The handler composes live and issues **at
least eight round-trips per call**: a readiness health check, queues, throughput, errors
over a 15-minute window, two `SystemAlerts` `COUNT(*)` queries, and a paged top-5 with a
rule-name lookup. Wrap in `IMemoryCache.GetOrCreateAsync` with a ~15-second TTL;
`AddMemoryCache()` is already registered (`Modules/Billing/BillingModule.cs:15`) and the
pattern exists at `Domain/PricingRuleCache.cs:10`. *Depends on:* nothing. *Acceptance:* a
second call inside the TTL issues no queries. *Verification:* extend
`Aveline.Api.Tests/SystemStatisticsEndpointsTests.cs` (seeded at `:159`) with a counting
decorator or query-count assertion.

**T-7.2 — Honour `from`/`to` on `/system/eventbus`.** *Touches:*
`Endpoints/SystemStatisticsEndpoints.cs:94-95`, `Services/SystemStatisticsService.cs:185-199`,
`Infrastructure/Eventing/EventBusMetrics.cs:20-22,58-67`. The handler binds no window
parameters and `EventBusMetrics` holds only cumulative `Interlocked.Read` counters with no
timestamps, so the window cannot be answered from it. The collector **already writes**
`aveline.eventbus.failed` / `aveline.eventbus.backlog` as windowed samples
(`Jobs/SystemMetricCollector.cs:255-258`), so route the window through
`ISystemMetricRepository.QueryAsync` with the existing `TryCreateWindow` helper
(`SystemStatisticsEndpoints.cs:141-144`). *Depends on:* nothing. *Acceptance:* two different
windows return different values, and a window with samples returns a series.
*Verification:* an endpoint test seeding `SystemMetricSamples` at distinct timestamps,
following `SystemStatisticsEndpointsTests.cs:149-152`.

**T-7.3 — Record the two accepted deviations in the catalogue.** *Touches:*
`docs/backend/statistics-catalog.md:766-773,781-812`. M-8 (bare array) and M-22
(org-agnostic samples) are already documented in `docs/api/README.md`; mirror the disposition
in the catalogue so the two documents agree on the intended-vs-shipped split. *Depends on:*
nothing. *Acceptance:* no catalogue entry claims behaviour the code does not have.
*Verification:* the M-8 intent is already recorded at `openapi.yaml:2298-2301`; cross-check
the catalogue against it.

---

### Phase 8 — Hardening and gates

**T-8.1 — Add a weak-token startup guard to the API.** *Touches:* `Aveline.Api/Program.cs`
or a new `Configurations/SecurityConfiguration.cs`, `appsettings.Development.json:7`,
`docker-compose.yml:80,138`. `AgentService:InternalToken` ships the literal
`change-me-internal-token` in a tracked file. The handler already fails **closed** when the
token is blank — `InternalTokenAuthenticationHandler.cs:39-44` returns
`AuthenticateResult.Fail` and the policy yields 401 — but a *committed placeholder* is a
different defect from an unset one. The Python service already guards against exactly this
set of values (`app/core/config.py:9,82-85`); the API has no equivalent. Mirror it, and
replace the compose defaults with `${INTERNAL_API_TOKEN:?set INTERNAL_API_TOKEN}`, the
pattern the same file already uses for `EMBEDDINGS_API_KEY` and
`CREDENTIALS_ENCRYPTION_KEY`. *Depends on:* nothing. *Acceptance:* the API refuses to start
with a placeholder token outside Development, or logs a hard warning and continues with an
explicit opt-in flag. *Verification:* a startup test asserting the refusal; **check the ZAP
job first** — it boots the API with `ASPNETCORE_ENVIRONMENT: Development`
(`.github/workflows/ci.yml:327`), so an over-strict guard breaks CI.

**T-8.2 — Bound the WhatsApp webhook replay window.** *Touches:*
`Aveline.Api/Infrastructure/Data/Configurations/InboundMessageLogConfiguration.cs:23-24,39-40`,
a migration, and `Aveline.Api/Endpoints/WebhookEndpoints.cs:147-158`.
The handler already verifies the signature before the rate limit (`:103-124`) and compares
in constant time (`Modules/Integrations/Services/WebhookSignatureVerifier.cs:41-44`), but a
captured signed body replays indefinitely: there is no timestamp/nonce check and no unique
index on `ExternalId`. Add a unique index on `(OrganizationId, ExternalId)` — follow
`20260913125349_AddIdempotencyHttpMethodToUniqueIndex.cs:17-22` for the
`NullsDistinct` annotation — **and** catch `DbUpdateException` around
`SaveChangesAsync` (`WebhookEndpoints.cs:158`) to return an ack instead of a 500. That catch
is mandatory, not defensive: without it the first duplicate becomes a 500 via
`Common/Exceptions/GlobalExceptionHandler.cs:53-63` and triggers a Meta retry storm. The
deliberate-duplicate pattern to copy is `Billing/Endpoints/BlossomEndpoints.cs:328`.
*Depends on:* D-4 (duplicate semantics). *Acceptance:* a replayed `wamid` is not stored
twice and is acked. *Verification:* extend `WebhookEndpointsIntegrationTests.cs` and
`WebhookSignatureVerifierTests.cs`; the backfill must tolerate pre-existing duplicates.

**T-8.3 — Fix the `ApiRequestLog.ErrorCode` writer gap.** *Touches:*
`Telemetry/ApiTelemetryMiddleware.cs:82-103`. Shared with T-4.10; listed separately because
the missing writer also affects error attribution generally, not only S-9.
*Depends on:* nothing. *Acceptance:* a 4xx/5xx log row carries the error code when one is
available. *Verification:* extend `Aveline.Api.Tests/ApiTelemetryMiddlewareTests.cs`.

**T-8.4 — Measure the load gate report-only.** *Touches:* a new `tests/load/` directory, a
k6 or NBomber script, and a new non-blocking CI job modelled on `zap-baseline`
(`ci.yml:307-363`, including `continue-on-error`). The FR-6.3 / BR-6.3 gate — 5,000 req/s for
60 s with p99 telemetry overhead ≤ 1 ms (`docs/backend/backend-requirements.md:783,884`) —
is stated as an acceptance criterion and has never been measured; no harness exists
(`ls tests` → no such directory; `grep -rn "BenchmarkDotNet" --include=*.csproj .` → no
matches). The middleware's per-request work is inlined and non-blocking — `DateTime.UtcNow`,
`Stopwatch.GetTimestamp()`, `RequestPrincipal.Resolve`, `RouteTemplateResolver.Resolve`, and
**two SHA-256 hashes over interpolated strings**
(`Telemetry/ApiTelemetryMiddleware.cs:73-104`, `Telemetry/MetricDimensionHasher.cs:13-22`) —
so the hash cost is the thing to measure. *Depends on:* D-8. *Acceptance:* a report publishes
p99 overhead with telemetry enabled versus disabled. *Verification:* the job's artefact;
`ApiTelemetryWriterTests.cs` already covers buffer overflow functionally
(`TelemetryChannel.cs:25-27` drops oldest).

---

## 5 · Dependency graph and sequence

```
Phase 0  T-0.1 ─┬─> T-1.7 (producer can't be trusted until 400 is correct)
                 │
         T-0.4 ──> T-1.1 ──> T-1.2
                 │
Phase 1  T-1.3 ──> T-1.4 ──> T-1.5 ──> T-1.6 ──┐
                                                ├─> T-1.7 ──> T-1.8 ──┬─> T-1.9
                                                │                     └─> T-1.10 ──> T-3.3
                                                └─> T-1.11

Phase 2  T-2.1 ──> T-2.2
         T-2.3 (needs Phase 1 rows)
         T-2.4 (independent; unblocks T-4.2 and T-4.6 if on-the-fly proves insufficient)

Phase 3  T-3.1, T-3.2 ──> T-3.5        T-3.4 (independent)

Phase 4  T-4.1 ──> T-4.11              T-4.10 ──> T-4.9
         T-4.2, T-4.4, T-4.6, T-4.7 (independent)
         T-4.3 (needs D-1) ──┐
         T-4.4 ──────────────┴─> T-4.12
         T-4.5 (needs D-5)   T-4.8 (needs T-0.3)

Phase 5  T-5.1 ──> T-5.2 ──> T-5.3

Phase 6  T-5.3 ──> T-6.1     T-6.2 (needs topology)     T-6.3 (needs D-6)

Phase 7  T-7.1, T-7.2, T-7.3 (independent)

Phase 8  T-8.1, T-8.3 (independent)   T-8.2 (needs D-4)   T-8.4 (needs D-8)
```

**Suggested sequence.**

1. **Phase 0** in full — four small changes that unfreeze everything else.
2. **Phase 1** — highest value: it makes a shipped feature functional. Nothing else in this
   plan converts as much delivered-but-dead surface into working surface.
3. **Phase 3** — cheap, and it removes the one gap that fails the repository's own rule.
   T-3.5 prevents recurrence permanently.
4. **Phase 4** in the order T-4.1 (largest, but a port), then T-4.4 → T-4.2 → T-4.7 → T-4.6
   (ascending difficulty), then T-4.10 → T-4.9, then T-4.8, then T-4.12 once T-4.3 and T-4.4
   are done, with T-4.3 and T-4.5 gated on decisions.
5. **Phase 7** — two isolated wins, no dependencies.
6. **Phase 2** — once Phase 1 has produced rows worth rolling up.
7. **Phase 5 → 6** — a deliberate feature rollout, not a cleanup. It changes what customers
   are charged, so it belongs on the release calendar, not in a fix sprint.
8. **Phase 8** — T-8.1 and T-8.3 immediately; the rest as the decisions land.

---

## 6 · Decisions required

| ID | Question | Blocks | Recommended default |
|---|---|---|---|
| **D-1** | What counts as an "active customer" for S-11 and the plan limit? The catalog and ADR-010 §Decision 5 say 90 days of activity across interactions, orders or profile updates — but nothing enforces it, and the existing code counts *all* customers. | T-4.3, and the `customers.active.max` entitlement guard | Adopt the documented 90-day rule (interaction **or** order **or** conversation), and make the entitlement guard use the same query so the two cannot disagree. |
| **D-2** | Does Blossom exhaustion block AI requests, throttle them, or stay unenforced? `ADR-010` §Decision 6 defers enforcement; the ledger balance currently decrements below zero with no guard (`UsageRepository.cs:57-64`). | Whether T-8.2-style work expands into a request-path gate | Keep unenforced for now — enforcement can begin rejecting paying customers mid-period and changes webhook semantics. If it is needed, pair it with a cheap cached balance read, not a per-request DB hit. |
| **D-3** | Does the Clerk `jwt-aveline-v1` template emit an `aud` claim? `ValidateAudience` is `false` (`AuthenticationConfiguration.cs:100`). | Closing SEC-M1 / H-5 | Verify against a real token **before** changing anything. Turning audience validation on without an `aud` claim rejects every authenticated request. Clock skew reduction (300 s → 30 s) is a separate, safe change. |
| **D-4** | On a duplicate WhatsApp `wamid`, ack-and-drop or 409? | T-8.2 | Ack-and-drop with a log — Meta retries on non-2xx, and a duplicate delivery is not the caller's fault. |
| **D-5** | Is an FX rate in scope for S-5's `marginLkr`? No USD→LKR source exists anywhere in the repository. | T-4.5 | Ship the series without `marginLkr`, mark it omitted, and revisit when a rate source is chosen. Do not hard-code a rate. |
| **D-6** | Quota enforcement: which orgs get `api.requests.*` entitlements, and is the counter store a single Redis instance or HA? With enforcement on, a counter-store outage is a 429 for every org. | T-6.3 | Populate entitlements for all live orgs, verify Redis topology, then flip. Never flip the flag first. |
| **D-7** | Application-level rate limiting (SEC-M2): key by IP, by org, or both, and on what backing store? Behind Container Apps, `RemoteIpAddress` may be a proxy address. | Whether T-8.2-style limiter work generalises | Decide the key source first. A global limiter that also throttles `/internal/**` ingestion or the SignalR hubs is worse than no global limiter. |
| **D-8** | Is the FR-6.3 load gate blocking or report-only, and are dedicated runners available? | T-8.4's CI shape | Report-only on shared runners. A hard p99 gate on shared CPU will be flaky and will get disabled, which is worse than a measured report. |
| **D-9** | Is the API deployed with more than one replica? | T-6.2 | If single-replica, accept the 5-second TTL and record it; do not build event-bus invalidation for a topology that does not exist. |
| **D-10** | Canonical Python tool paths: keep the legacy `/api/internal/inventory/...` aliases the current agent uses, or move `registry.py` to `/internal/visual/...` and drop them? | T-4.1, T-4.11 | Move to `/internal/visual/...` (the documented contract) and delete the aliases, so there is one canonical path. Update `registry.py` in the same change. |

---

## 7 · Risks

Ordered by severity. "Signal" is what would tell you the risk has materialised.

| # | Risk | Mitigation | Signal |
|---|---|---|---|
| R-1 | **The `origin/catalog` port (T-4.1) conflicts badly or silently regresses HEAD's schema.** The branch is 63 commits behind and its migration predates M1–M8. | Port entity/configuration classes only; **regenerate** the migration against HEAD; never port `AppDbContextModelSnapshot.cs`. Run the ported tests plus a full `dotnet ef migrations script` diff. | A generated migration that drops or alters tables added by M1–M8. |
| R-2 | **Turning `dataQuality` flags truthful (T-1.8) before the data exists** replaces honest nulls with false zeros — the exact failure the catalogue warns about. | Sequence T-1.8 strictly after T-1.7, and derive each flag from actual presence of data, never from "we intended to instrument this". | A percentile that reads as a real number when the sample count is below the floor. |
| R-3 | **Flipping `Pricing:UseLegacyFormula` (T-6.1) changes what customers are charged** with no recompute path for history, and it is a one-line config change that is easy to ship casually. | T-5.2 before T-6.1, T-5.3 (backfill policy) recorded, and the flip treated as a release. The fallback path is already safe, so the risk is financial, not technical. | Support tickets about invoice amounts; `AiUsageRecords.NormalizedUnits` changing for comparable traffic. |
| R-4 | **Enabling quota enforcement (T-6.3) converts a Redis outage into org-wide 429s**, because `QuotaService` fails closed when enforcement is on. | Confirm counter-store topology and populate entitlements first. | A spike in 429s correlated with a cache-store incident. |
| R-5 | **The WhatsApp unique index (T-8.2) turns legitimate Meta retries into 500s** if the `DbUpdateException` catch is omitted, or the backfill fails on pre-existing duplicates. | The catch is mandatory; backfill must dedupe or use a partial index. | A burst of 500s from `WebhookEndpoints` immediately after deploy. |
| R-6 | **The E2 migration (T-2.1) locks or rebuilds a large `ApiRequestMetrics` table.** The index is created by hand-edited DDL. | Create the index concurrently where PostgreSQL allows, or schedule the migration; verify against a production-sized table. | Migration duration far exceeding the deploy window. |
| R-7 | **Scope creep through the visual port.** `origin/catalog` carries more than the 13 routes — extra alias groups, a `VisionService`, Python caches. | Port the documented 13 routes plus their dependencies, nothing else. Resolve each alias explicitly against D-10. | New routes appearing in the route table that no document lists. |
| R-8 | **A CI route-diff job (T-3.5) becomes noisy** and gets skipped if it flags the deliberate exclusions. | Encode the three documented exclusions (`/internal/**`, Development-only demo routes, hubs) in the diff, as `docs/api/README.md:48-56` specifies. | The job being disabled within a sprint of landing. |
| R-9 | **The load gate (T-8.4) cannot be met on shared runners** and produces a number nobody trusts. | Report-only, and state the runner class in the report. | p99 varying by more than the effect being measured across runs. |
| R-10 | **The Python producer (T-1.7) reports runs that the C# validation rejects**, so telemetry silently fails while the service appears healthy. The existing reporter swallows failures (`app/api/agents.py:152-185`), which hides exactly this. | Add a counter/log for rejected reports and assert the round-trip in an integration test rather than a stub alone. | Zero `AgentWorkflowRuns` rows despite live traffic — the failure mode the whole phase exists to fix. |

---

## 8 · Out of scope

- **Frontend behaviour.** Where an endpoint exists only to serve a screen, this plan notes it
  and does not design it — consistent with `docs/backend/README.md:448-449`. M-8's bare array
  is deliberately left alone for this reason.
- **The `DailyBillingMetrics` shape beyond what the catalogue specifies.** The plan names the
  table, its key and its dependency; the column list is a design step inside T-2.4.
- **`/internal/telemetry` and M-22.** Both are recommended **Accept** with reasons in §3, not
  deferrals carried forward silently.
- **The catalogue's §9 "hourly compaction" for `SystemMetricSamples`** (400-day hourly
  retention, `statistics-catalog.md:827`). This plan did not verify whether it is implemented
  — the retention job exists and the collector writes the samples, but the compaction step
  itself was not traced. **Open question, not a finding.** Settling it needs one read of
  `Aveline.Api/Modules/Statistics/Jobs/SystemMetricRetentionJob.cs` and the collector's
  write path; if compaction is absent it belongs alongside T-2.2.
- **`docs/backend/implementation-plan.md`'s §355 `/internal/telemetry` specification.** This
  plan recommends correcting that document rather than implementing the endpoint (§3, #11).

---

## 9 · Self-review: where this plan could be wrong

- **T-4.1 is the plan's largest assumption.** I confirmed the files exist on `origin/catalog`
  and that the module is empty at HEAD, and I confirmed the route list. I did **not** attempt
  the port or measure the conflict surface; "regenerate the migration" is a recommendation
  from the evidence, not a verified procedure. If the port proves infeasible, T-4.1 reverts
  to a from-scratch build and the phase's cost changes substantially.
- **T-2.4 is newly identified, so its effort is the least understood.** I established that
  `DailyBillingMetrics` and `BillingRollupJob` do not exist and that the catalogue names them
  as the source for three statistics. I did not establish how much of S-4/S-5/S-6 can be
  served on the fly instead — T-4.2 takes that route deliberately, and T-4.6 may too.
- **Phases 1 and 2 are genuinely large.** Phase 1 is not a wiring task: the Python service has
  no run/step model, no timing, no tool/retry counting, and no working pause. The report's
  framing ("wire the Python instrumentation") understates it, and this plan inherits that
  risk by keeping it as one phase.
- **Two items in the residual register are not defects** and are marked Accept rather than
  Build: `/internal/telemetry` and M-22. If the reviewer disagrees, they are cheap to move
  back into Phase 4 or 7.
