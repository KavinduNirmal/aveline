# Aveline Backend — Requirements and Implementation Plan

**Status:** Phases 0–1 (foundations and Blossom pricing) are **implemented** on
`feature/admin-backend-api` (issues #176–#189). Phases 2–6 remain proposed; the
three blocking open questions below now gate Phase 2 only.
**Baseline:** commit `902f27f` (`integration/slice-2-to-slice-1`)
**Scope:** backend only — `Aveline.Api` and `agnet-service`. No frontend, no
screens, no UX flows.

---

## What this is

A complete, implementation-ready backend plan for seven feature areas requested
for Aveline:

1. Blossom-to-token price adjustment
2. Organization Blossom account operations (upgrade / add / deduct / remove)
3. User management
4. Organization management
5. Agentic statistics
6. API consumption statistics
7. System statistics

It was produced by reading the repository, not from memory. The findings, the
gaps, the data model, the statistics, the endpoints, and the phasing are all
traceable to `path:line` citations in the documents below.

---

## Read in this order

| Document | What it answers | Read it if you are… |
| --- | --- | --- |
| **[backend-requirements.md](backend-requirements.md)** | Current state, gap analysis, the 12 defects found, functional requirements and business rules for all seven feature areas, permissions, events, jobs, edge cases, audit, testing | The person deciding scope, or the person implementing any feature |
| **[domain-model.md](domain-model.md)** | Every entity, column, type, constraint, index, and the eight-migration sequence with the one backfill | The person writing EF Core entities and migrations |
| **[statistics-catalog.md](statistics-catalog.md)** | 43 statistics, each with formula, dimensions, granularity, retention, source, storage strategy, exposing endpoint, and access control — plus the `dataQuality` flag contract | The person building or consuming analytics |
| **[implementation-plan.md](implementation-plan.md)** | Architecture decisions, module-by-module file breakdown, jobs, caching, observability, security, testing, deployment, risks, and seven phased milestones with acceptance criteria | The person estimating, sequencing, or leading |
| **[assumptions-and-open-questions.md](assumptions-and-open-questions.md)** | 10 assumptions, 11 open questions, what would change the plan, and 12 recommended next steps | Anyone who needs to know what is *not* settled |
| **[../api/README.md](../api/README.md)** | Every endpoint: method, path, purpose, auth, permissions, params, request/response schemas, status codes, pagination, rate limits, idempotency, curl examples, and related statistics | A frontend developer |
| **[../api/openapi.yaml](../api/openapi.yaml)** | The same contract, machine-readable — 101 paths, 113 operations, validated against OpenAPI 3.0.3 | A tool or a codegen pipeline |

---

## The short version

**What already works.** Blossom usage is recorded per agent workflow into two
tables (`AiUsageRecords` append-only, `UsageAccounts` per-org-per-month ledger)
with a working ingest endpoint, a read endpoint, and a boutique-facing balance
endpoint (`Aveline.Api/Modules/Billing/**`). Users, organizations, memberships,
invitations, notifications, conversations, and integrations are implemented with
a Clerk-backed auth model and organization-scoped authorization.

**What is missing.**

| Feature area | State |
| --- | --- |
| 1 · Blossom price adjustment | **Implemented (Phase 1).** Effective-dated `BlossomConversionRules` and `BlossomPriceEntries` (M2), `BlossomCalculator`, `PricingService`, admin pricing endpoints, and ingest-time pricing behind `Pricing:UseLegacyFormula`. `POST /admin/pricing/rules/{id}/recompute` returns 501 until the Phase 2 ledger |
| 2 · Org Blossom operations | **Entirely absent.** No add/deduct/remove, no adjustment ledger, no idempotency, no concurrency control, no plan-change endpoint |
| 3 · User management | Partial. No member list, no role change, no profile update, no soft delete, no sessions, **no API keys at all** |
| 4 · Organization management | Partial. No settings update, no subscription, no entitlements, no API key management |
| 5 · Agentic statistics | Only one aggregate row per workflow. No agent/node runs, status, latency, tool calls, or retries |
| 6 · API consumption statistics | **Entirely absent.** No request telemetry, no correlation id, no quotas |
| 7 · System statistics | A Redis-only `/health` and an event-bus metrics logger. No readiness split, resource metrics, queue depth, error rate, or alerts |

**Twelve live defects were found.** The four that matter most:

- **D-3 — lost-update race on the Blossom balance.** The balance update is a
  read-modify-write with **no concurrency token anywhere in the repository**.
  Concurrent AI workflows for one organization will silently lose increments.
- **D-1 / D-2 — the plan limit is ignored** when a billing-period ledger row is
  created lazily: it is always seeded at the Seed tier value of 150 Blossoms,
  regardless of the organization's actual plan.
- **D-5 — the LangGraph checkpointer is silently discarded.** The graph is
  compiled without a checkpointer and the saver is passed as a call-time kwarg,
  which `langgraph 1.2.11` ignores. The mandatory human-in-the-loop pause does
  not survive a restart. This is Slice-1/Slice-2 territory and is **flagged, not
  scheduled** by this plan.
- **D-9 — documentation drift, now verified in three places.**
  `docs/architecture/authorization.md` omits `conversations:view` from two role
  rows and from its catalog list; `Aveline.Api/Common/Exceptions/README.md`
  documents a `GlobalExceptionHandler.cs` that does not exist; and
  `OnboardingService.cs:266` writes the literal `"org:principal"`, a value absent
  from `Authorization/Roles.cs` and therefore granted nothing.

**Twelve defects list, non-blocking ones included**, is in
[backend-requirements.md §1.3](backend-requirements.md#13-defects-found-that-the-plan-must-fix).

---

## Plan at a glance

| Phase | Content | Days | Fixes |
| --- | --- | --- | --- |
| **0** | Correlation IDs, audit table, permission catalog, policy constants, job lock, OpenTelemetry + `/metrics`, `/health/live` + `/health/ready`, doc drift | 4–5 | D-9, D-11 |
| **1** | Conversion rules, price book, pricing snapshots, admin pricing endpoints | 4–5 | — |
| **2** | Entitlement ledger, idempotency, `IEntitlementResolver`, Blossom operations, plan change, burn rate | 6–8 | D-1, D-2, D-3, D-12 |
| **3** | API keys + auth scheme, user profile/admin/sessions, org settings, member roles, Clerk webhooks | 5–6 | — |
| **4** | Agent run/step ingest, agent statistics endpoints, Python instrumentation (G-1…G-14) | 7–9 | D-4, D-5, D-6, D-7, D-8 |
| **5** | Request telemetry pipeline, API statistics, quotas, rollups, partitioning | 8–10 | — |
| **6** | Health checks, metric collectors, alert rules, system statistics | 5–6 | — |

**39–49 developer-days.** Roughly 5–7 weeks of wall-clock with three people once
Phases 0–1 are shared and 2–4 parallelise. Phase 4 has a hard dependency on
Python instrumentation owned by other slices — see
[OQ-11](assumptions-and-open-questions.md#oq-11--who-owns-the-python-instrumentation-work).

### Implementation status (Phase 0–1)

Shipped on `feature/admin-backend-api`, one commit per issue:

| Phase | Issue | What landed |
| --- | --- | --- |
| 0 | [#176](https://github.com/KavinduNirmal/aveline/issues/176) | `CorrelationIdMiddleware` (`X-Request-Id`, validation, echo, log scope, agent propagation) |
| 0 | [#177](https://github.com/KavinduNirmal/aveline/issues/177) | `AuditLogEntries` (M1), redactor, `IAuditService` |
| 0 | [#178](https://github.com/KavinduNirmal/aveline/issues/178) | 15 new permissions, boutique roles excluded from money-shaped grants |
| 0 | [#179](https://github.com/KavinduNirmal/aveline/issues/179) | `IDistributedJobLock` (Redis NX, in-memory fallback) |
| 0 | [#180](https://github.com/KavinduNirmal/aveline/issues/180) | `/health/live` + `/health/ready` + version block |
| 0 | [#181](https://github.com/KavinduNirmal/aveline/issues/181) | OpenTelemetry + authenticated `/metrics` |
| 0 | [#182](https://github.com/KavinduNirmal/aveline/issues/182) | D-9 doc drift and `org:principal` literal fixed |
| 1 | [#183](https://github.com/KavinduNirmal/aveline/issues/183) | `BlossomCalculator` (rounding modes, minimum clamp) |
| 1 | [#184](https://github.com/KavinduNirmal/aveline/issues/184) | Pricing entities + M2 (`btree_gist`, exclusion constraint) |
| 1 | [#185](https://github.com/KavinduNirmal/aveline/issues/185) | `PricingRepository` / `PricingService` and L1 cache |
| 1 | [#186](https://github.com/KavinduNirmal/aveline/issues/186) | `/api/v1/admin/pricing/**` endpoints |
| 1 | [#187](https://github.com/KavinduNirmal/aveline/issues/187) | Ingest-time pricing + `PricingRuleCacheWarmer` |
| 1 | [#188](https://github.com/KavinduNirmal/aveline/issues/188) | Postgres constraint/activation tests |
| 1 | [#189](https://github.com/KavinduNirmal/aveline/issues/189) | Documentation and AI-usage updates |

**Confirmed deviation from the proposed plan:** the M2 GiST exclusion predicate is
`Status = 'Active'` only, not `('Draft', 'Active')`. A successor Draft necessarily
overlaps the open-ended Active predecessor, so the proposed predicate would make
BR-1.8 activation impossible. Drafts may therefore overlap; activation supersedes
the predecessor atomically. See
[domain-model.md §3.1](domain-model.md#31-blossomconversionrule--blossomconversionrules).

**Deferred to Phase 2:** `AiUsageRecord` pricing-snapshot columns and
`POST /admin/pricing/rules/{ruleId}/recompute` (which writes ledger corrections).

---

## Three decisions needed before Phase 2

| # | Question | Why it blocks |
| --- | --- | --- |
| [OQ-1](assumptions-and-open-questions.md#oq-1--what-does-blossom-to-token-price-adjustment-mean) | Does "Blossom-to-token price adjustment" mean the token→Blossom normalisation rate, the Blossom→LKR commercial price, or both? | Determines Phase 1 scope (±3 days) |
| [OQ-7](assumptions-and-open-questions.md#oq-7--what-status-code-for-exceeding-a-plan-quota) | 402, 403, or 429 when a plan quota is exceeded? | Determines the frontend's upgrade-prompt trigger |
| [OQ-8](assumptions-and-open-questions.md#oq-8--is-btree_gist-installable-on-the-target-database) | Is `btree_gist` installable on the production PostgreSQL? | Determines whether pricing-rule overlap is prevented by the database or by an application check that races |

Three five-minute checks would also de-risk the plan materially:
`SELECT version()` (is it PostgreSQL 15+ for `NULLS NOT DISTINCT`?), whether
`btree_gist` installs, and whether `OrganizationMemberships` already has a unique
index on `(UserId, OrganizationId)`.

---

## Honesty notes

Stated plainly so they are not mistaken for oversights:

1. **`docs/api/openapi.yaml` is hand-authored, and this conflicts with an existing
   repository rule.** `docs/OpenApi/README.md:27` says *"Never hand-edit the
   exported spec — it is generated from the code."* That rule is right for
   implemented endpoints. This document covers endpoints that **do not exist
   yet**, so it cannot be generated. The reconciliation procedure — generated
   output wins for shipped endpoints, this file wins for planned ones, and a CI
   diff once the first planned endpoint ships — is specified in
   [api/README.md §1.3](../api/README.md).
2. **Two of four research workstreams failed mid-investigation** (the org/user
   endpoint inventory and the build/deployment conventions). Both were recovered
   by direct reading of `OrganizationEndpoints.cs`, the `.csproj` files,
   `appsettings.json`, `.github/workflows/ci.yml`, and `dotnet-tools.json`.
   Anything not directly read is marked `INFERRED`.
3. **Six statistics in the catalog are currently unmeasurable.** Agent latency,
   per-node failures, per-step token attribution, tool calls, retries, and actual
   AI cost all depend on instrumentation that does not exist. Every statistics
   response carries a `dataQuality` object naming exactly which flags are `false`,
   and the catalog states the dependency for each. A frontend that ignores that
   object will render zeros that look like healthy measurements.
4. **Enforcement of Blossom exhaustion is deliberately not in this plan.**
   `ADR-010` §Decision 6 defers it, and this plan does not reverse that. The
   ledger work here is the prerequisite either way.
5. **Frontend behaviour is out of scope.** Where an endpoint exists only to serve
   a screen, that is noted, not designed.
