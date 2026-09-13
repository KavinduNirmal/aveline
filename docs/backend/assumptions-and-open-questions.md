# Aveline Backend — Assumptions, Open Questions, and Recommended Next Steps

**Status:** Proposed — the open questions below need answers before the phase they
block.

**Companion:** [Requirements](backend-requirements.md) · [Domain Model](domain-model.md) · [Statistics Catalog](statistics-catalog.md) · [Implementation Plan](implementation-plan.md)

---

## 1. How to read this document

- **Assumptions** are statements this plan treats as true without having verified
  them. Each states what it would cost to be wrong and how to check it.
- **Open questions** are forks that inspection cannot settle — they need a
  business, cost, or priority decision. Each names who can answer it and which
  phase it blocks.
- **Verified facts** are not repeated here; they live with citations in the
  requirements and domain documents.

Nothing in this document is a finding. The findings, with citations, are in
[backend-requirements.md §1](backend-requirements.md#1-current-state-and-gap-analysis).

---

## 2. Assumptions

### A1 · "Blossom-to-token price adjustment" means adjustable normalisation and an adjustable LKR price book

**Assumption.** The requirement covers both directions of the conversion: tokens
→ Blossoms (the normalisation rate that determines what a workflow *costs*) and
Blossoms → LKR (the commercial price that determines what a customer *pays*).

**Why it is assumed.** `docs/architecture/pricing_plan.md` defines both
(§4.1 sets `1 Blossom ≈ 1,000 units`; §9 proposes Blossom pack prices in LKR) but
`ADR-010` only decided the first. The phrase "Blossom-to-token price adjustment"
is directionally ambiguous.

**If wrong.** If only the normalisation rate is in scope, `BlossomPriceEntries`
and the price-book CRUD are dropped, removing roughly 3 developer-days from
Phase 1. If only the LKR price is in scope, the more consequential half — the
rate that silently re-prices every AI workflow — is missed.

**How to settle.** See [OQ-1](#oq-1).

### A2 · The `AttemptNumber` on `AgentStepRun` is sufficient for retry modelling

**Assumption.** A retried node produces additional `AgentStepRun` rows with an
incremented `AttemptNumber`, and `AgentWorkflowRun.RetryCount` is the total of
`AttemptNumber > 1` rows.

**Grounding.** No retry logic exists anywhere in the agent service today, so the
shape is a design choice, not an observation. **This is the assumption most
likely to be wrong**, because retries might be implemented at the HTTP transport
layer (httpx) rather than the node layer, in which case there is no node to
re-run and `AttemptNumber` would stay at 1 while real retries happen invisibly.

**If wrong.** `RetryCount` and the retry statistic (S-20's failure breakdown)
would be zero while retries actually occur, which is worse than not reporting the
metric at all.

**How to check.** When G-7 (retry instrumentation) is implemented, record the
retry at the layer where it actually happens and confirm the shape. Until then
`dataQuality.retryInstrumented` must be `false`.

### A3 · PostgreSQL is 16 and `NULLS NOT DISTINCT` is available

**Assumption.** `docs/ADR/ADR-003` specifies PostgreSQL with pgvector, and the
local stack uses a `pgvector/pgvector:pg16`-style image. `NULLS NOT DISTINCT`
(PostgreSQL 15+) is therefore available for the unique indexes on
`ApiRequestMetrics`, `AgentWorkflowRuns`, `IdempotencyRecords`, and
`BlossomConversionRules`.

**If wrong.** Every one of those unique indexes fails to create. The fallback is
`coalesce(col, sentinel)` expression indexes, which is uglier but equivalent.

**How to check.** `SELECT version();` against the deployed database before Phase 1.

### A4 · The team is willing to raise coverage selectively rather than repo-wide

**Assumption.** The coverage gate is raised to 60 % for the four new/extended
high-risk modules and left at 30 % repo-wide, rather than forcing a repo-wide jump
that would require retrofitting tests onto the three existing vertical slices.

**If wrong.** Either the new code ships under-tested, or unrelated slices absorb
test debt they did not create.

**How to settle.** See [OQ-10](#oq-10).

### A5 · A single instance runs each background job during the phases this plan covers

**Assumption.** `DistributedJobLock` is written and used, but the deployment is
expected to be single-instance for Phases 0–6, so a lock failure is a degraded
mode rather than an outage.

**If wrong.** Two instances run `BillingPeriodRolloverJob` simultaneously. The
job is idempotent by design (unique index on `(OrganizationId, PeriodStart)`) and
the second run is a no-op, so the assumption is not load-bearing — but the lock
should still be written in Phase 0.

### A6 · `OrganizationMemberships` already has a unique index on `(UserId, OrganizationId)`

**Assumption.** The handler
(`OrganizationScopeAuthorizationHandler.cs:59-74`) resolves one membership via
`GetMembershipAsync`, and `OnboardingService` calls `GetMembershipAsync` and
handles both the "create" and "activate" cases
(`OnboardingService.cs:245-263`), which implies uniqueness. The index itself was
**not confirmed** during this review.

**If wrong.** Two memberships for one user and org make `GetMembershipAsync`
non-deterministic, which is an authorization bug, not a display bug.

**How to check.** Read
`Infrastructure/Data/Configurations/OrganizationMembershipConfiguration.cs` and
run `\d "OrganizationMemberships"`. This is a five-minute check and should be done
in Phase 0.

### A7 · Redis is available wherever telemetry, quotas, and rate limits are needed

**Assumption.** `docker-compose.yml` runs Redis, `CacheConfiguration` falls back to
in-memory when it is absent (`Configurations/CacheConfiguration.cs:24-47`), and the
new telemetry pipeline is deployed where Redis exists.

**If wrong.** Quota counters have no atomic backing store, and this plan's
fail-closed quota decision (§8.4 of the implementation plan) becomes unreachable.

**How to check.** Confirm the production platform provides Redis. `ADR-006` names
the deployment platform; that document was read but the managed Redis service was
not confirmed.

### A8 · `agnet-service` is the canonical spelling and will not be renamed

**Assumption.** The directory is spelled `agnet-service` (not `agent-service`) in
the repository, the Dockerfile, the CI workflow, and every document. This plan
uses the existing spelling everywhere to avoid breaking paths.

**If wrong.** Nothing breaks, but a rename would touch CI, `docker-compose.yml`,
and every documentation reference — a mechanical refactor outside this plan's scope.

### A9 · Every consumer of the existing `/internal/usage/record` contract can tolerate additive fields

**Assumption.** Adding pricing snapshot fields to the `AiUsageRecordResponse` is a
non-breaking change because the only consumer is the Python `report_usage` helper,
which ignores the response body.

**Grounding.** `usage_reporter.py` posts and checks the status code; it does not
deserialise the response. Verified by reading
`agnet-service/app/services/usage_reporter.py:85-105`.

**If wrong.** The agent service breaks on ingest. The mitigation is that the
endpoint keeps its existing `201` shape and the new fields are additive.

### A10 · The empty `backend/Aveline.Domain|Application|Infrastructure` projects stay out of scope

**Assumption.** These three directories contain no buildable code that
`Program.cs` references, and the modular monolith lives in `Aveline.Api/Modules/`
per `ADR-001`.

**If wrong.** This plan targets the wrong project. **How to check:** confirm no
`.csproj` under `backend/` is in `Aveline.Api/Aveline.Api.sln`. This was checked
only by listing files, not by reading the solution file — a five-minute
confirmation.

---

## 3. Open questions

### OQ-1 · What does "Blossom-to-token price adjustment" mean?

**Question.** Which conversions must be adjustable:

- **(a)** only the token→Blossom normalisation rate (`UnitsPerBlossom`);
- **(b)** only the Blossom→LKR commercial price;
- **(c)** both, as this specification assumes?

**Why it matters.** (a) determines what every AI workflow costs a customer, and
changing it silently re-prices usage. (b) determines revenue and only affects
future invoices. They have different permission models, different audit
requirements, and different blast radii. Getting this wrong means either
building a price book nobody asked for or missing the mechanism the pricing
model's §4.4 explicitly anticipates ("Aveline should eventually move toward …
model-specific cost calculation → normalized AI cost → Blossom units").

**Who can answer.** The product owner / whoever wrote the requirement.

**Blocks.** Phase 1 scope.

**Recommendation.** (c). The two are already coupled in
`docs/architecture/pricing_plan.md`, the price book is cheap (~1 entity, 3 days),
and without it plan profitability (S-5) cannot be computed.

### OQ-2 · Does a Blossom price change need to be retroactive?

**Question.** When the normalisation rate changes, may already-recorded usage be
re-priced, and if so, for how far back?

**Why it matters.** This decides whether `PricingRecomputeJob` is a
compliance-grade, audited, compensating-entry operation or a simple convenience.
Retroactive re-pricing of a customer's Blossoms is commercially visible: it can
turn a comfortable balance into an overdrawn one after the fact.

**Who can answer.** Product owner.

**Blocks.** Phase 2 (the recompute job).

**Recommendation.** Default to **non-retroactive**. Allow backdating only for
(a) rules whose `EffectiveFrom` precedes any usage, and (b) explicit corrections
requiring `pricing:backdate`, which writes compensating `CorrectionRecompute`
ledger entries and never mutates `AiUsageRecord`.

### OQ-3 · Should consumption live in the entitlement ledger too?

**Question.** Should `BlossomLedgerEntries` also contain a `Consumption` entry
per workflow, making one table the complete statement of account?

**Why it matters.** Symmetry is appealing and makes the balance a single `SUM`.
But `AiUsageRecord` is already an append-only consumption log, and the ingest path
runs on every AI workflow — the hottest write path in the system.

**Who can answer.** Backend lead.

**Blocks.** Nothing; Phase 2 can ship either way.

**Recommendation.** **No** (this plan's decision A-4). Keep `AiUsageRecord` as the
consumption log and `UsageAccount.BlossomUsed` as its aggregate. If a unified
ledger is wanted later, it is an additive migration that backfills from
`AiUsageRecord` — reversing the decision is cheap, whereas a doubled hot-path
write is not.

### OQ-4 · Are billing periods UTC months or boutique-local months?

**Question.** `UsageTrackerService.GetCurrentPeriod()` computes UTC calendar
months (`UsageTrackerService.cs:119-125`). Should a Colombo boutique's period
start at 00:00 UTC (05:30 local) or 00:00 Asia/Colombo?

**Why it matters.** It changes the period boundary, and therefore which month a
usage record belongs to. It also affects the definition of "today" in every daily
statistic. Payment providers almost universally bill on UTC.

**Who can answer.** Product owner.

**Blocks.** Phase 2 (period rollover).

**Recommendation.** Keep **UTC**. It is already implemented, it matches provider
conventions, and it is deterministic. Add `Organization.TimeZone` for *display*
only and state clearly in the API that periods are UTC. Changing the boundary
later is a one-line change if the org's data does not span a boundary during
migration.

### OQ-5 · Does this work include Blossom enforcement (blocking requests)?

**Question.** Should a request be rejected when `blossom_remaining <= 0`?

**Why it matters.** `ADR-010` §Decision 6 explicitly defers enforcement, and the
requirements list "upgrade" and "billing usage" but never say a request may be
blocked. Implementing enforcement turns a partially degraded AI feature into a
hard failure for a paying customer — a product decision, not an engineering one.

**Who can answer.** Product owner.

**Blocks.** Nothing in this plan; it is deliberately excluded.

**Recommendation.** **Not in this plan.** The ledger work here is the prerequisite
either way. If enforcement is wanted, it should be a separate, flagged slice with
a soft-warning period first (`blossom.balance.threshold` at 20 % already exists in
this design).

### OQ-6 · Are `AgentsInvolved` and agent statistics dimension sets stable?

**Question.** Should `AgentWorkflowRun.AgentsInvolved` keep the current
`text[]` shape, or be normalised into a join table?

**Why it matters.** A `text[]` with a GIN index answers "which runs involved the
visual agent" efficiently and is one fewer table. A join table makes per-agent
statistics joins cheaper and enforces referential integrity to a registered agent
list. Postgres array types are already used elsewhere in this codebase's spirit
(willingness to use `jsonb`), but no `text[]` column exists yet.

**Who can answer.** Backend lead.

**Blocks.** Phase 4 schema.

**Recommendation.** **Keep `text[]`.** The agent set is 4 values and effectively
static; a join table would be a 4-row dimension table joined on every statistics
query for no benefit. Add a `CHECK` that every element is in the known set.

### OQ-7 · What status code for exceeding a plan quota?

**Question.** Should creating an API key on a non-Rose plan, or exceeding a
request quota, return:

- **402 Payment Required** — semantically correct but legally ambiguous in some
  jurisdictions and rarely used;
- **403 Forbidden** — consistent with the existing plan-gating behaviour
  (`OnboardingService.SaveAiCustomizationAsync` throws `ArgumentException`, which
  the endpoint maps to 400);
- **429 Too Many Requests** — correct for a *rate* limit, wrong for an
  *entitlement* limit;
- **400 Bad Request** — what the existing plan gating effectively produces.

**Why it matters.** It is a frontend contract decision. The React dashboard must
render an upgrade prompt for exactly one of these.

**Who can answer.** Frontend lead + product owner.

**Blocks.** Phase 2 response contract.

**Recommendation.** **403** for a capability the plan does not include
(`api.access`, `ai.customAgents`) and **429** for exceeding a numeric quota, with
a structured body in both cases naming the entitlement key, the limit, and
`upgradeUrl`. Avoid 402: it is inconsistently handled by proxies and clients.

### OQ-8 · Is `btree_gist` installable on the target database?

**Question.** Can `CREATE EXTENSION btree_gist` run on the production PostgreSQL,
and does the deployment role have the privilege?

**Why it matters.** The GiST exclusion constraint is the only **database-level**
guarantee that two pricing rules cannot cover the same instant. Without it,
overlap prevention degrades to an application check that races under concurrent
admin writes.

**Who can answer.** Infrastructure owner.

**Blocks.** Phase 1 mechanism.

**Recommendation.** Verify before Phase 1 starts. If unavailable, ship the unique
index plus an application-level check in a serialisable transaction, and add a
Critical alert that fires if overlapping active rules are ever detected by a
periodic scan.

### OQ-9 · Which OpenTelemetry package versions work on .NET 10?

**Question.** The API currently has **no** OpenTelemetry packages
(`Aveline.Api.csproj`). Which versions of `OpenTelemetry.Extensions.Hosting`,
`OpenTelemetry.Instrumentation.AspNetCore`,
`OpenTelemetry.Instrumentation.EntityFrameworkCore`, and
`OpenTelemetry.Exporter.Prometheus.AspNetCore` are compatible with `net10.0` and
with the pinned EF Core `10.0.11`?

**Why it matters.** A version mismatch either fails the build or, worse,
silently disables instrumented spans. The Prometheus exporter in particular has
moved between packages and beta/stable status across versions.

**Who can answer.** Whoever implements Phase 0.

**Blocks.** Phase 0.

**Recommendation.** Resolve empirically at the start of Phase 0 by adding the
packages and asserting that a known span is emitted in a test — do not accept a
build that merely compiles. If the Prometheus exporter is unstable on .NET 10,
fall back to an OTLP export to the existing `otel-collector` and scrape the
collector instead.

### OQ-10 · Is a selective coverage gate acceptable?

**Question.** May the coverage gate be raised only for the four new/extended
high-risk modules, leaving the repo-wide gate at 30 %?

**Why it matters.** A repo-wide jump to 60 % would force test work onto the three
existing vertical slices, which is unrelated to this plan and competes for the
same developer-days.

**Who can answer.** Team lead / whoever owns the grading rubric.

**Blocks.** Phase 0 CI configuration.

**Recommendation.** Selective. See [A4](#a4--the-team-is-willing-to-raise-coverage-selectively-rather-than-repo-wide).

### OQ-11 · Who owns the Python instrumentation work?

**Question.** Gaps G-1 through G-14 are changes to `agnet-service`, which belongs
to Slice 1 and Slice 2, not to the billing/statistics slice. Who implements them,
and on what schedule?

**Why it matters.** Without them, every agentic statistic in
[statistics-catalog.md §5](statistics-catalog.md) returns `null` or zero with
`dataQuality` flags set false. The statistics feature is then a correctly-built
pipeline with no data.

**Who can answer.** Team lead.

**Blocks.** Phase 4 value (the schema and the .NET side can ship regardless).

**Recommendation.** Split Phase 4 into 4a (.NET ingest, storage, endpoints,
`dataQuality` flags — this plan's owner) and 4b (Python instrumentation — the
slice owners), with 4b scheduled at the same time as 4a so the data exists when
the endpoints land. Do **not** have one person do both; the Python work touches
`concierge_workflow.py`, `state_events.py`, and the sub-graphs, which are owned by
other slices and will conflict.

---

## 4. What would change this plan

Recorded so the plan is falsifiable.

| If this turns out to be true | Then this plan changes how |
| --- | --- |
| The system is expected to exceed ~200 req/s sustained | Rollups move from Phase 4 to Phase 5, and `ApiRequestMetrics` becomes the only telemetry table with the raw log disabled |
| The deployment is multi-region | Period boundaries must move to UTC-aligned and the quota counters must be region-scoped; `Organization.TimeZone` becomes load-bearing |
| `btree_gist` is unavailable | M2 uses the application-level overlap check and a detection alert (OQ-8) |
| Enforcement of Blossom exhaustion is required | A new Phase 7 is inserted; this plan's ledger is the prerequisite and nothing is rework |
| A payment provider is chosen | `OrganizationSubscriptions` already carries `ExternalProvider`/`ExternalSubscriptionId`; the provider client is additive but webhook signature verification and reconciliation become new work |
| `NULLS NOT DISTINCT` is unavailable (PostgreSQL < 15) | Every unique index in §3/§6/§7 of the domain model is rewritten with `coalesce` sentinels |
| The 30 % backend coverage gate is considered a grading requirement rather than a floor | Coverage becomes a Phase 0 blocker and the phasing is re-sequenced |

---

## 5. Recommended next steps

Ordered. The first four are decisions or five-minute checks, not implementation.

| # | Action | Owner | Effort |
| --- | --- | --- | --- |
| 1 | Answer [OQ-1](#oq-1) (scope of "price adjustment") | Product owner | 15 min |
| 2 | Answer [OQ-7](#oq-7) (quota status code) | Frontend lead + product owner | 15 min |
| 3 | Run `SELECT version();` and `CREATE EXTENSION IF NOT EXISTS btree_gist;` against the target database ([OQ-8](#oq-8), [A3](#a3--postgresql-is-16-and-nulls-not-distinct-is-available)) | Infrastructure | 10 min |
| 4 | Confirm the `(UserId, OrganizationId)` unique index on `OrganizationMemberships` ([A6](#a6--organizationmemberships-already-has-a-unique-index-on-userid-organizationid)) | Backend | 10 min |
| 5 | Confirm no `backend/*.csproj` is in the solution ([A10](#a10--the-empty-backendavelinedomainapplicationinfrastructure-projects-stay-out-of-scope)) | Backend | 5 min |
| 6 | Review and approve this plan, then convert Phases 0–2 into tracked issues | Team lead | 2 h |
| 7 | Resolve the .NET 10 OpenTelemetry package versions empirically ([OQ-9](#oq-9--which-opentelemetry-package-versions-work-on-net-10)) | Phase 0 implementer | 2 h |
| 8 | Start Phase 0 (correlation IDs, audit table, permissions, health split, doc-drift fixes) | Backend | 4–5 d |
| 9 | Schedule the Python instrumentation work with the slice owners ([OQ-11](#oq-11--who-owns-the-python-instrumentation-work)) | Team lead | 30 min meeting |
| 10 | Fix the three live defects that are cheap and independent: **D-1/D-2** (plan limit ignored on lazy ledger creation) in Phase 2, and **D-9** (doc drift, `"org:principal"`) in Phase 0 | Backend | included above |
| 11 | Add the `TenantIsolationTests` matrix **before** any new tenant endpoint is written | Backend | 1 d, Phase 0/1 boundary |
| 12 | Reconcile `docs/api/openapi.yaml` with generated output once endpoints exist | Backend | 0.5 d, per phase |

### On defects that are not fixed by this plan

Three findings are reported but **not** scheduled, because fixing them changes
behaviour that other slices own:

| Defect | Why not scheduled here |
| --- | --- |
| **D-5** (the LangGraph checkpointer is silently discarded, so a paused workflow cannot resume) | The fix is one line — move `checkpointer` into `graph.compile(...)` at `concierge_workflow.py:367` — but it changes the human-in-the-loop behaviour that `ADR-018` explicitly defers resume for. Needs the Slice-1 owner's decision, not a backend plan's |
| **D-6** (streaming runs emit no usage) | Requires deciding whether streaming is in scope; `/agents/query/stream` is never called from C# today |
| **D-11** (no correlation ID) | Actually scheduled — Phase 0 — but listed here because it is the one cross-cutting defect whose fix unlocks both new statistics feature areas |

D-5 is the most consequential unfixed defect in the repository: it means the
mandatory cross-platform workflow's human-approval pause does not survive a
restart. It is flagged here because it affects the *statistics* this plan defines
(S-21, `agentApprovalWaitTime`) and because the plan would otherwise be silently
built on top of a broken assumption.
