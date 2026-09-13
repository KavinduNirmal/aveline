# Aveline Backend — Requirements and Implementation Plan

**Status:** Phases 0–6 (foundations, Blossom pricing, the entitlement ledger, API
access/user/organization administration, agentic statistics, API consumption
statistics, and system statistics and alerts) are **implemented** on
`feature/admin-backend-api` (issues #176–#231). The eight-migration sequence in
[domain-model.md](domain-model.md) is complete: M1 `AuditLogEntries`, M2 pricing, M3
ledger, M4 entitlements, M5 API access, M6 agent statistics, M7 API consumption and
M8 system statistics are all applied. No further migration is planned by this document.
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
| 2 · Org Blossom operations | **Implemented (Phase 2).** Append-only `BlossomLedgerEntries` (M3) with the O(1) `UsageAccount` projection, idempotency replay store, `BlossomService` credit/debit/revoke, entitlement catalog + `IEntitlementResolver` (M4), org/admin Blossom endpoints, subscription/plan-change endpoints, and the expiry/rollover/cleanup jobs. **Fixes D-1, D-2, D-3, D-12** |
| 3 · User management | **Implemented (Phase 3).** Profile update, soft delete with membership/API-key revocation, Clerk session list/revoke, and the cross-org admin user search + account-state endpoints |
| 4 · Organization management | **Implemented (Phase 3).** Settings update with AI-context entitlement gating, settings read with resolved entitlements, member list with filters, role change with the FR-3.4 guards, and the `ApiKeys` table + scheme/endpoints behind the `api.access` entitlement |
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

**Security deviation (#237):** [backend-requirements.md §3.5](backend-requirements.md#35-permissions)
lists `pricing:view` as granted to boutique owner/manager, but the admin pricing
**read** routes are gated by the team-only `PricingAdminRead` policy (Admin or
Owner) so the two documents agree with [../api/README.md §C.1](../api/README.md)
("never available to boutique roles"). The boutique grant remains in the permission
catalog for any future boutique-facing read surface; it no longer reaches
`/admin/pricing/**`. The `GET /admin/pricing/price-book/{entryId}` lookup also takes
an optional `organizationId` and refuses a per-organization override outside that
organization (global entries stay readable).

**Deferred to Phase 2:** `AiUsageRecord` pricing-snapshot columns and
`POST /admin/pricing/rules/{ruleId}/recompute` (which writes ledger corrections).

**Deviation (#240):** rule activation now runs the predecessor trim and the successor
activation in one transaction (BR-1.8) and the optional `{ "effectiveFrom": ... }`
body is honoured; `pricing.rule.activated` and `pricing.rule.cancelled` are published
on the `IEventBus`. The documented **cross-instance** cache invalidation subscriber
is **not** implemented: within a process the `PricingRuleCache` generation counter
invalidates immediately, and on other instances the 5-second TTL is the only
invalidation mechanism. See [backend-requirements.md §3.6](backend-requirements.md#36-events-jobs-and-webhooks).

### Implementation status (Phase 2 — ledger and entitlements)

| Issue | What landed |
| --- | --- |
| [#190](https://github.com/KavinduNirmal/aveline/issues/190) | M3: extended `UsageAccounts`, `BlossomLedgerEntries`, `IdempotencyRecords` + backfill |
| [#191](https://github.com/KavinduNirmal/aveline/issues/191) | `BlossomLedgerRepository` and `BlossomService` credit/debit/revoke |
| [#192](https://github.com/KavinduNirmal/aveline/issues/192) | Idempotency replay store and endpoint filter |
| [#193](https://github.com/KavinduNirmal/aveline/issues/193) | M4 entitlements, `IEntitlementResolver`, D-1/D-2/D-12 fixes |
| [#194](https://github.com/KavinduNirmal/aveline/issues/194) / [#195](https://github.com/KavinduNirmal/aveline/issues/195) | Org and admin Blossom endpoints |
| [#196](https://github.com/KavinduNirmal/aveline/issues/196) | Subscription, plan-change and entitlement endpoints |
| [#197](https://github.com/KavinduNirmal/aveline/issues/197) | Expiry, period-rollover and idempotency-cleanup jobs |
| [#198](https://github.com/KavinduNirmal/aveline/issues/198) | `AiUsageRecord` pricing-snapshot columns |
| [#199](https://github.com/KavinduNirmal/aveline/issues/199) | Postgres concurrency, constraint and backfill coverage |
| [#200](https://github.com/KavinduNirmal/aveline/issues/200) | Documentation and AI-usage update |

**Confirmed deviations from the proposed plan:**

1. **M2 exclusion predicate is `Active` only** (see the Phase 0–1 note above).
2. **`POST /admin/pricing/rules/{ruleId}/recompute` returns 501** until the ledger
   recompute job is scheduled; the snapshot columns it needs now exist (M6-free
   migration `AddAiUsageRecordPricingSnapshot`).
3. **The pricing-snapshot columns ship in Phase 2**, not in M6 as the domain model
   suggested, so ingest can persist the applied rule (FR-1.4).
4. **A `nextPeriod` plan change stores the target tier on the subscription**; the
   organisation's live tier and period allocation change at rollover.

### Implementation status (Phase 3 — API access, users and organizations)

| Issue | What landed |
| --- | --- |
| [#201](https://github.com/KavinduNirmal/aveline/issues/201) | M5: `ApiKeys` table, `Organization` setting columns, verified membership unique index |
| [#202](https://github.com/KavinduNirmal/aveline/issues/202) | `ApiKey` generation/hash/scope validation and `ApiKeyRepository` |
| [#203](https://github.com/KavinduNirmal/aveline/issues/203) | `ApiKey` authentication scheme, org-scoped policies, tenant-scope 404 middleware |
| [#204](https://github.com/KavinduNirmal/aveline/issues/204) | API-key management endpoints and the `api.access` entitlement gate |
| [#205](https://github.com/KavinduNirmal/aveline/issues/205) | Organization settings, member list and the FR-3.4 role-change guards |
| [#206](https://github.com/KavinduNirmal/aveline/issues/206) | User profile, soft delete, Clerk sessions, admin user search/state endpoints |
| [#207](https://github.com/KavinduNirmal/aveline/issues/207) | Clerk webhook receiver (Svix signature) and idempotent read-model sync |
| [#208](https://github.com/KavinduNirmal/aveline/issues/208) | Tenant isolation matrix, docs and AI-usage update |

**Confirmed deviations from the proposed plan:**

1. **API keys cannot create other API keys.** A machine credential with
   `apikeys:manage` is rejected with 403 on `POST /api-keys`: allowing it would let a
   key mint a wider key, and there is no user to attribute the creation to.
2. **`LastUsedAt` amortisation (FR-3.18) is deferred to the Phase 5 telemetry
   pipeline.** No per-request write happens today; the column and the flush job are
   the Phase 5 work item.
3. **`Clerk:WebhookSecret` absence fails closed with 503.** The endpoint is
   anonymous by necessity, so it refuses to accept unsigned events rather than
   trusting the caller.
4. **An organization is never adopted from a Clerk `organization.created` event.**
   Only organizations created through onboarding are updated, so an unreviewed
   Clerk org cannot appear as a tenant.
5. **The member role-change guards are enforced in the service, not only the
   policy.** Only a boutique owner holds `settings:manage`, so the owner-grant and
   last-owner guards are defence in depth; they are covered by
   `MembershipRoleChangeTests` at the service level.

### Implementation status (Phase 4 — agentic statistics)

| Issue | What landed |
| --- | --- |
| [#209](https://github.com/KavinduNirmal/aveline/issues/209) | M6: `AgentWorkflowRuns`, `AgentStepRuns`, the `AiUsageRecord.AgentWorkflowRunId` link and its migration |
| [#210](https://github.com/KavinduNirmal/aveline/issues/210) | `PercentileCalculator`, `IAgentRunRepository`/`AgentRunRepository`, `IAgentStatisticsService`/`AgentStatisticsService` (S-13…S-23) and `StatisticsModule` |
| [#211](https://github.com/KavinduNirmal/aveline/issues/211) | `/api/v1/orgs/{organizationId}/statistics/agents/**` and the team-only `/api/v1/admin/statistics/agents/{overview,runs,reliability}` subset |
| [#212](https://github.com/KavinduNirmal/aveline/issues/212) | `/internal/agent-runs` ingest (`POST ""`, `POST /{workflowId}/steps`, `GET /{workflowId}`) with idempotency, the terminal conflict, the step cap and D-7 |
| [#213](https://github.com/KavinduNirmal/aveline/issues/213) | `AgentStatsRetentionJob` (daily 03:00 UTC) and `StaleAgentRunJob` (hourly) with the `AgentStats:*` config keys |
| [#214](https://github.com/KavinduNirmal/aveline/issues/214) | These documents and the AI-usage record |

**Confirmed deviations from the proposed plan:**

1. **The Python instrumentation (gaps G-1…G-14) is deferred per risk R-1.** The
   C# ingest, storage, endpoints and jobs are complete and covered by tests, but
   nothing in `agnet-service/` emits run/step telemetry yet, so every live response
   carries `dataQuality` with `latencyInstrumented`, `nodeFailuresObserved`,
   `perStepAttribution`, `toolInstrumented` and `costInstrumented` **all `false`**.
   `GET /latency` returns a null series rather than zeros. The catalog's §8 flag
   names differ slightly from the wire contract: the API uses `costInstrumented`
   where the catalog says `costIsEstimated`, and the API omits
   `retryInstrumented`, `materialisedCounts`, `streamingRunsIncluded` and
   `unattributedRunsExcluded` until the corresponding work exists.
2. **There is no `DailyAgentMetrics` rollup and therefore no `AgentStatsRollupJob`.**
   Percentiles are computed on the fly over the bounded window (catalog §9 says the
   on-the-fly path is sufficient at current volumes). The hybrid "rollup beyond
   seven days" described for S-16 is not implemented.
3. **`AgentStats:MinSampleForPercentile` and the retention keys are new config
   keys** beyond the two the plan's §10.2 listed (`MaxStepsPerRun`,
   `PausedRunTimeoutHours`). `AgentStats:StepRetentionDays` (90) and
   `AgentStats:RunRetentionDays` (400) were previously hard-coded in the catalog.
4. **Step-level endpoints (`/steps`, `/tokens`, `/tools`) apply only `agentKey`
   and the time window.** The step filter does not join the run table, so
   `status`/`triggerKind` are ignored for those three statistics.
5. **`AgentKey` is validated against the registered set** (`customer_memory`,
   `visual_insight`, `commerce`, `orchestrator`) per BR-5.5, and a step report that
   overlaps an existing `(StepIndex, AttemptNumber)` is a 409 rather than a database
   error.
6. **S-23 concurrency is not exposed here.** The catalog routes it to
   `/api/v1/admin/statistics/system/queues`, which belongs to Phase 6.

---

### Implementation status (Phase 5 — API consumption statistics)

| Issue | What landed |
| --- | --- |
| [#220](https://github.com/KavinduNirmal/aveline/issues/220) | M7: `ApiRequestMetrics`, the partitioned `ApiRequestLogs` (hand-edited migration), `ApiQuotaUsage`, the `aveline_ensure_api_request_log_partition`/`aveline_drop_old_api_request_log_partitions` functions and their Postgres tests |
| [#221](https://github.com/KavinduNirmal/aveline/issues/221) | `ApiTelemetryMiddleware`, bounded `TelemetryChannel`, `RouteTemplateResolver`, `MetricDimensionHasher`, `ApiTelemetryWriter`, the incremental `ApiMetricRepository` upsert and the `Telemetry:*` config keys |
| [#222](https://github.com/KavinduNirmal/aveline/issues/222) | `LatencyBuckets`, the `IApiStatisticsService` S-24…S-32 implementation, window validation and the raw-log reads |
| [#223](https://github.com/KavinduNirmal/aveline/issues/223) | `/api/v1/orgs/{organizationId}/statistics/api[-keys]` and the team-only `/api/v1/admin/statistics/api*` endpoints with the 400 validation surface |
| [#224](https://github.com/KavinduNirmal/aveline/issues/224) | `QuotaService` (Redis Lua counter + in-memory fallback), `QuotaEnforcementMiddleware`, `ApiKeyUsageAggregator` (closes FR-3.18) and `ApiQuotaResetJob` |
| [#225](https://github.com/KavinduNirmal/aveline/issues/225) | `ApiStatsRollupJob`, `ApiRequestLogPartitionJob` and `ApiStatsRetentionJob` |
| [#226](https://github.com/KavinduNirmal/aveline/issues/226) | The 1 000-request rollup acceptance test, these documents and the AI-usage record |

**Confirmed deviations from the proposed plan:**

1. **`Quotas:EnforcementEnabled` defaults to `false` (safe rollout).** Quota is measured
   on every request but rejected with 429 only when the flag is on. A limit of `0` means
   "not configured → unlimited", never an exhausted quota. When the counter store is
   unreachable the quota path **fails closed** (treats the meter as exhausted) only while
   enforcement is enabled; with enforcement disabled it can never fail a request.
2. **Hour→day compaction beyond the 90-day hourly retention is deferred.** Hourly and
   daily windows share one table and one unique dimension index, so a day row at the day
   boundary would collide with the 00:00 hour row. `ApiStatsRollupJob` therefore
   recomputes-and-replaces (normalises and merges) the just-closed hour; the 400-day daily
   rollup from S-24/§9 is not produced yet.
3. **The load-test harness is deferred.** There is no load runner in CI, so the FR-6.3 /
   BR-6.3 gate — 5 000 req/s for 60 s with p99 telemetry overhead ≤ 1 ms — is a noted
   acceptance criterion, not a measured one. The middleware inlines no I/O and only
   stamps/enqueues, and `ApiTelemetryWriterTests` covers buffer overflow, but the
   latency gate itself is unproven in this environment.
4. **JWT user/org attribution is claim-only.** `RequestPrincipal` reads `org_id`/`user_id`
   claims; it deliberately performs no database lookup on the request path (FR-6.3), so a
   Clerk token without those claims is attributed to `OrganizationId = NULL` and counted
   in system statistics only (BR-6.1). API-key traffic is fully attributed.
5. **`IX_ApiRequestLogs_Slow` and `IX_ApiRequestLogs_Errors` are created by raw SQL.**
   EF Core identifies an index by its property set, so the two partial indexes on
   `OccurredAt` cannot both be modelled; they exist in the hand-edited M7 migration and
   are verified by `ApiConsumptionPostgresTests`.
6. **New `Telemetry:*` retention/threshold keys** (`RawLogRetentionDays`,
   `HourlyRollupRetentionDays`, `DailyRollupRetentionDays`, `WriterBatchSize`,
   `WriterFlushSeconds`, `MinSampleForPercentile`, `MaxWindowDays`,
   `QuotaWarningPercent`, `IpHashSalt`) are exposed in `appsettings.json` beyond the
   plan's §10.2 list.

---

### Implementation status (Phase 6 — system statistics and alerts)

| Issue | What landed |
| --- | --- |
| [#227](https://github.com/KavinduNirmal/aveline/issues/227) | M8: `SystemMetricSamples`, `SystemAlertRules`, `SystemAlerts`, the CHECK/unique indexes and the twelve seeded rules |
| [#228](https://github.com/KavinduNirmal/aveline/issues/228) | `SystemMetricCollector` (pure `BuildSamples`, bounded 100-sample retry buffer), `SystemMetricRetentionJob` (daily 03:30, 30 days), `ISystemMetricRepository` and the `Observability:SystemMetric*` keys |
| [#229](https://github.com/KavinduNirmal/aveline/issues/229) | `IAlertService`/`AlertService` (Avg/Max/Min/Sum/Rate/Count over the window, cooldown aggregation, persisted consecutive-OK auto-resolution, critical notification, audit and `system.alert.*` events) and `AlertEvaluationJob` (60 s, lock-guarded) |
| [#230](https://github.com/KavinduNirmal/aveline/issues/230) | `/api/v1/admin/statistics/system/{overview,metrics,queues,errors,throughput,eventbus,alerts}` and `POST /alerts/{alertId}/acknowledge` under `stats:system` |
| [#231](https://github.com/KavinduNirmal/aveline/issues/231) | This document, [../api/README.md §C.8](../api/README.md), [statistics-catalog.md](statistics-catalog.md) and the AI-usage record |

**Confirmed deviations from the proposed plan:**

1. **Collected metric names follow BR-7.8** (`aveline.<subsystem>.<measure>`, e.g.
   `aveline.api.error_rate`, `aveline.agent.runs_running`). The seeded rules from #227 still
   reference the short `api.*` / `agent.*` / `blossom.*` names for metrics the collector does
   not produce, so those rules fire only when another producer supplies those names.
2. **The collector records CPU seconds with unit `count`.** The documented unit set
   (`count`, `ms`, `bytes`, `ratio`, `percent`) has no `seconds` member.
3. **BR-7.11 is honoured only for organization-scoped critical alerts.**
   `NotificationRecords.OrganizationId` is a required FK to `Organizations` and the scheduled
   rules are system-wide, so a system-wide critical alert logs that no notification was
   created rather than writing an invalid FK. `IAlertService.EvaluateRuleAsync` accepts an
   optional organization id, so an org-scoped alert does create the record through
   `IRecipientResolver`.
4. **`inbound_message_backlog` and `publish_latency_ms` are unmeasurable today.**
   `InboundMessageLog` has no processed marker and `EventBusMetrics` exposes counters only;
   both appear in each response's `omitted` list instead of as zero (BR-7.10).
5. **The metrics endpoint parameter is `windowSize`** (`instant|minute|hour|day`), not the
   `groupBy` name used in the §C.8 draft; the collector writes `instant` samples.
6. **Rate aggregation sums positive deltas per minute.** For a cumulative counter this is the
   increase over the window, and a reset cannot produce a negative rate.
7. **`Observability:SystemMetricCollectionSeconds` and `SystemMetricRetentionDays` are new
   config keys**; `AlertEvaluationSeconds` and `AutoResolveConsecutiveOk` were already in the
   plan's §10.2 list.

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
