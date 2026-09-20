# Aveline Backend — Statistics and Data Point Catalog

**Status:** Proposed
**Companion:** [Requirements](backend-requirements.md) · [Domain Model](domain-model.md) · [API Catalog](../api/README.md)

Every statistic below states its name, description, formula, dimensions,
granularity, retention, data source, storage and aggregation strategy, exposing
endpoint, access control, and freshness. Nothing here is exposed by the API
unless it appears in this catalog **and** in
[`docs/api/openapi.yaml`](../api/openapi.yaml).

---

## 1. Reading guide

| Field | Meaning |
| --- | --- |
| **Formula** | The exact computation. `Σ` means sum over the stated dimension set within the window. |
| **Dimensions** | The attributes a caller may group or filter by. Any dimension not listed is not supported. |
| **Granularity** | The smallest time bucket available: `real-time`, `minute`, `hour`, `day`, `month`. |
| **Freshness** | The worst-case staleness a client should assume. |
| **Retention** | How long the underlying rows are kept, per the retention jobs in [implementation-plan.md](implementation-plan.md). |
| **Source** | The table or subsystem the value is computed from. |
| **Storage** | `rollup` = pre-aggregated and read directly; `on-the-fly` = computed at query time from source rows; `hybrid` = rollup with an on-the-fly fallback for windows the rollup does not cover. |
| **Endpoint** | The exact route that returns it. |
| **Access** | The permission required. |

**Cross-cutting rules**

- All windows are UTC. `from`/`to` are ISO 8601 with `Z`.
- The maximum window is `Telemetry:MaxWindowDays` (default 92) for on-the-fly
  statistics; rollup-backed statistics accept up to 400 days.
- Percentiles are `null` with a `reason` when the sample count is below
  `Telemetry:MinSampleForPercentile` (default 20). A misleading number is worse
  than no number.
- Every response includes `generatedAt`, `window`, and `dataQuality` where
  instrumentation gaps exist (see §8).

---

## 2. Blossom and billing statistics

### S-1 · `blossomBalance`

| Field | Value |
| --- | --- |
| Description | Available Blossoms for the current billing period |
| Formula | `MonthlyBlossomLimit + BlossomGranted − BlossomAdjusted − BlossomUsed` |
| Dimensions | `organizationId` |
| Granularity | real-time |
| Freshness | `0 s` — read directly from the projection row, updated in the same transaction as the write |
| Retention | 400 days (one row per org per period) |
| Source | `UsageAccounts` |
| Storage | on-the-fly (single-row read) |
| Endpoint | `GET /api/v1/orgs/{organizationId}/blossoms/balance` |
| Access | `billing:view:self` — every organization role holds it. The management read (`billing:view`) still governs the statement and burn-rate; the balance alone is self-service, so the associate at the counter can see what the shop has left. |
| Notes | Verified against the ledger by the `reconciliation` block in S-3. Home's Blossom meter (S-1) renders `blossomRemaining`, with the low-water note driven by `lowBalanceThresholdPercent`, not a client constant. |

### S-2 · `blossomUsage`

| Field | Value |
| --- | --- |
| Description | Blossoms consumed over a window |
| Formula | `Σ BlossomUnits` over `AiUsageRecords` where `CreatedAt ∈ [from, to)` |
| Dimensions | `organizationId`, `provider`, `model`, `workflowId`, `day` |
| Granularity | hour |
| Freshness | `≤ 5 s` (ingest latency only; no aggregation delay) |
| Retention | 400 days for records; daily rollup retained 400 days |
| Source | `AiUsageRecords` (primary), `DailyBillingMetrics` (rollup) |
| Storage | hybrid — on-the-fly for windows ≤ 31 days, rollup for longer |
| Endpoint | `GET /api/v1/orgs/{organizationId}/blossoms/usage` |
| Access | `billing:view` |

### S-3 · `blossomLedgerStatement`

| Field | Value |
| --- | --- |
| Description | Full statement of account: every entitlement movement plus consumption, with a running balance and a reconciliation check |
| Formula | Rows ordered by `CreatedAt`; running total = `MonthlyBlossomLimit + Σ deltas − Σ consumed`. `reconciliation.drift = available_blossoms − ledger_sum` |
| Dimensions | `organizationId`, `entryType`, `sourceKind`, `day` |
| Granularity | real-time |
| Freshness | `0 s` |
| Retention | 400 days |
| Source | `BlossomLedgerEntries` ∪ `AiUsageRecords` ∪ `UsageAccounts` |
| Storage | on-the-fly, paginated |
| Endpoint | `GET /api/v1/orgs/{organizationId}/blossoms/statement` |
| Access | `billing:view` |
| Notes | `drift != 0` raises a Critical `SystemAlert`. `SystemMetricCollector` emits `aveline.blossom.reconciliation.drift` (the maximum absolute drift across accounts) and `blossom.ledger.drift` watches it (S-30, #239). This is the primary integrity check of the whole Blossom system |

### S-4 · `blossomBurnRate`

| Field | Value |
| --- | --- |
| Description | Blossoms consumed per day, and the projected exhaustion date |
| Formula | `burn_rate = Σ BlossomUnits / days_in_window`; `projected_exhaustion_at = now + (available / burn_rate) days` |
| Dimensions | `organizationId`, `window` (7d, 14d, 30d) |
| Granularity | day |
| Freshness | `≤ 1 h` |
| Retention | 400 days |
| Source | `DailyBillingMetrics` |
| Storage | rollup |
| Endpoint | `GET /api/v1/orgs/{organizationId}/statistics/billing/burn-rate` |
| Access | `billing:view` |
| Notes | `projected_exhaustion_at` is `null` when `burn_rate = 0` — never `∞` or a sentinel date |

### S-5 · `blossomCostByPlan`

| Field | Value |
| --- | --- |
| Description | Internal actual AI cost versus Blossoms charged, so plan profitability is measurable |
| Formula | `actual_cost_usd = Σ ActualCostUsd`; `blossoms_charged = Σ BlossomUnits`; `cost_per_blossom_usd = actual_cost_usd / blossoms_charged`; `margin_lkr = (blossoms_charged × price_per_blossom_lkr) − (actual_cost_usd × usd_to_lkr)` |
| Dimensions | `organizationId`, `planTier`, `provider`, `model`, `day` |
| Granularity | day |
| Freshness | `≤ 1 h` |
| Retention | 400 days |
| Source | `AiUsageRecords` + `BlossomPriceEntries` + `PlanEntitlements` |
| Storage | rollup |
| Endpoint | `GET /api/v1/admin/statistics/billing/profitability` |
| Access | `stats:system` |
| Notes | **Currently unreliable** — `ActualCostUsd` is always `0` on the wire (defect D-8). The response must carry `dataQuality.costIsEstimated: true` until G-5 is fixed |

### S-6 · `orgBlossomRanking`

| Field | Value |
| --- | --- |
| Description | Organisations ranked by Blossom consumption, for capacity and abuse review |
| Formula | `Σ BlossomUnits` per org over the window, with `rank()` |
| Dimensions | `planTier`, `window` |
| Granularity | day |
| Freshness | `≤ 1 h` |
| Retention | 400 days |
| Source | `DailyBillingMetrics` |
| Storage | rollup |
| Endpoint | `GET /api/v1/admin/statistics/billing/org-usage` |
| Access | `stats:system` |

---

## 3. Organization Blossom operation statistics

### S-7 · `blossomAdjustmentActivity`

| Field | Value |
| --- | --- |
| Description | Count and total magnitude of administrative credits, debits, and revocations |
| Formula | `count = COUNT(*) GROUP BY entryType`; `total = Σ |BlossomDelta|` |
| Dimensions | `organizationId`, `entryType`, `actorUserId`, `day` |
| Granularity | day |
| Freshness | `0 s` |
| Retention | 400 days |
| Source | `BlossomLedgerEntries` |
| Storage | on-the-fly (low volume, but indexed on `(OrganizationId, EntryType, CreatedAt)`) |
| Endpoint | `GET /api/v1/admin/statistics/billing/adjustments` |
| Access | `stats:system` |
| Notes | This is the primary abuse-detection signal: a spike in `AdminCredit` for one org or one actor |

### S-8 · `planChangeHistory`

| Field | Value |
| --- | --- |
| Description | Plan upgrades and downgrades over time, with the Blossom delta each produced |
| Formula | `COUNT(*)` and `Σ BlossomDelta` where `EntryType ∈ {PlanUpgradeProration, PlanDowngradeAdjustment}` |
| Dimensions | `organizationId`, `fromTier`, `toTier`, `day` |
| Granularity | day |
| Freshness | `0 s` |
| Retention | 400 days |
| Source | `BlossomLedgerEntries` + `AuditLogEntries` (`org.plan.changed`) |
| Storage | on-the-fly |
| Endpoint | `GET /api/v1/admin/statistics/billing/plan-changes` |
| Access | `stats:system` |

### S-9 · `downgradeBlockRate`

| Field | Value |
| --- | --- |
| Description | Proportion of attempted downgrades rejected by the limit guard, and which limit caused it |
| Formula | `blocked / attempted`; broken down by the violated key (`blossoms.monthly`, `staff.max`, `customers.active.max`) |
| Dimensions | `organizationId`, `violatedKey`, `day` |
| Granularity | day |
| Freshness | `0 s` |
| Retention | 90 days |
| Source | `ApiRequestLogs` (`POST .../subscription/change-plan`, `StatusCode = 409`) + the response `violations` array |
| Storage | on-the-fly |
| Endpoint | `GET /api/v1/admin/statistics/billing/downgrades` |
| Access | `stats:system` |

---

## 4. Entitlement and plan statistics

### S-10 · `entitlementUtilisation`

| Field | Value |
| --- | --- |
| Description | Observed consumption against each limited entitlement |
| Formula | For each key: `used / limit`. `blossoms.monthly → BlossomUsed / (MonthlyBlossomLimit + BlossomGranted)`; `staff.max → COUNT(active memberships)`; `customers.active.max → COUNT(active customers)` |
| Dimensions | `organizationId`, `entitlementKey` |
| Granularity | real-time |
| Freshness | `≤ 5 min` for `staff`/`customers` (materialised on the `UsageAccounts` row); `0 s` for Blossoms |
| Retention | not historical; snapshot only |
| Source | `UsageAccounts`, `OrganizationMemberships`, `Organizations` (active customer definition from `ADR-010`: activity within 90 days) |
| Storage | on-the-fly, with a 5-minute cache |
| Endpoint | `GET /api/v1/orgs/{organizationId}/entitlements/usage` |
| Access | `billing:view` |
| Notes | **Corrected:** `UsageAccount.StaffCount` and `ActiveCustomerCount` **are** written. `EntitlementCountingJob` (`Modules/Billing/Jobs/LedgerJobs.cs:272-320`, registered at `BillingModule.cs:33`) recomputes both every five minutes, so `dataQuality.materialisedCounts` is `true` once the job has run. The earlier claim that no writer existed came from grepping the column *name* rather than the *writer*; grep the job registry instead. |

### S-11 · `activeCustomerCount`

| Field | Value |
| --- | --- |
| Description | Customers counting toward the plan limit |
| Formula | `COUNT(DISTINCT c.Id)` where the customer has an interaction, order, or profile update with `CreatedAt >= now − 90d` |
| Dimensions | `organizationId`, `day` |
| Granularity | day |
| Freshness | `≤ 1 h` |
| Retention | 400 days |
| Source | `Customers`, `CustomerInteractions`, `Orders` |
| Storage | rollup (daily snapshot) |
| Endpoint | `GET /api/v1/orgs/{organizationId}/statistics/customers/active` |
| Access | `billing:view` |
| Notes | The 90-day definition is from `ADR-010` §Decision 5 and is not currently enforced or computed anywhere |

### S-12 · `staffSeatCount`

| Field | Value |
| --- | --- |
| Description | Active staff seats consuming the plan allowance |
| Formula | `COUNT(*) FROM OrganizationMemberships WHERE OrganizationId = @org AND Status = 'Active'` |
| Dimensions | `organizationId`, `boutiqueRole` |
| Granularity | real-time |
| Freshness | `≤ 5 min` (cache) |
| Retention | not historical |
| Source | `OrganizationMemberships` |
| Storage | on-the-fly, 5-minute cache |
| Endpoint | `GET /api/v1/orgs/{organizationId}/statistics/staff/seats` |
| Access | `billing:view` |

---

## 5. Agentic statistics

### S-13 · `agentRunCount`

| Field | Value |
| --- | --- |
| Description | Number of agent workflow runs |
| Formula | `COUNT(*)` |
| Dimensions | `organizationId`, `agentKey`, `triggerKind`, `status`, `day`/`hour` |
| Granularity | hour |
| Freshness | `≤ 5 s` |
| Retention | 90 days for steps; 400 days for runs |
| Source | `AgentWorkflowRuns` (rollup: `DailyAgentMetrics`) |
| Storage | hybrid |
| Endpoint | `GET /api/v1/orgs/{organizationId}/statistics/agents/runs` |
| Access | `stats:view:agent` |

### S-14 · `agentSuccessRate`

| Field | Value |
| --- | --- |
| Description | Share of runs that completed successfully |
| Formula | `success_rate = count(status='Succeeded') / count(status IN ('Succeeded','Failed','TimedOut','Cancelled'))`. Runs still `Running` or `PausedForApproval` are **excluded** from the denominator |
| Dimensions | `organizationId`, `agentKey`, `triggerKind`, `nodeName`, `day` |
| Granularity | hour |
| Freshness | `≤ 1 h` |
| Retention | 400 days |
| Source | `AgentWorkflowRuns` / `AgentStepRuns` |
| Storage | hybrid |
| Endpoint | `GET /api/v1/orgs/{organizationId}/statistics/agents/reliability` |
| Access | `stats:view:agent` |
| Notes | **Under-reports failures today** — node errors are swallowed inside the agent service (gap G-2). Responses carry `dataQuality.nodeFailuresObserved: false` until fixed |

### S-15 · `agentFailureBreakdown`

| Field | Value |
| --- | --- |
| Description | Failures grouped by cause |
| Formula | `COUNT(*) GROUP BY ErrorCode, AgentKey, NodeName, StepKind` |
| Dimensions | `organizationId`, `errorCode`, `agentKey`, `nodeName`, `toolName`, `day` |
| Granularity | hour |
| Freshness | `≤ 5 s` |
| Retention | 90 days |
| Source | `AgentWorkflowRuns`, `AgentStepRuns` |
| Storage | on-the-fly |
| Endpoint | `GET /api/v1/orgs/{organizationId}/statistics/agents/failures` |
| Access | `stats:view:agent` |

### S-16 · `agentLatency`

| Field | Value |
| --- | --- |
| Description | End-to-end workflow latency distribution |
| Formula | `p50`, `p95`, `p99` over `DurationMs` using `percentile_cont(0.5/0.95/0.99) WITHIN GROUP (ORDER BY "DurationMs")`. `avg = Σ DurationMs / COUNT(*)` |
| Dimensions | `organizationId`, `agentKey`, `triggerKind`, `hour`/`day` |
| Granularity | hour |
| Freshness | `≤ 1 h` |
| Retention | 400 days |
| Source | `AgentWorkflowRuns` |
| Storage | hybrid — on-the-fly for ≤ 7-day windows, rollup with pre-computed percentiles beyond |
| Endpoint | `GET /api/v1/orgs/{organizationId}/statistics/agents/latency` |
| Access | `stats:view:agent` |
| Notes | **`DurationMs` is `null` for every run today** — nothing in the agent service measures it (gap G-3). Until fixed, this endpoint returns `dataQuality.latencyInstrumented: false` and a `null` series rather than zeros |

### S-17 · `agentStepLatency`

| Field | Value |
| --- | --- |
| Description | Per-node latency, to identify which node dominates a slow workflow |
| Formula | `avg = Σ DurationMs / COUNT(*)`; `p95` per `(agentKey, nodeName)` |
| Dimensions | `organizationId`, `agentKey`, `nodeName`, `stepKind`, `hour` |
| Granularity | hour |
| Freshness | `≤ 5 s` |
| Retention | 90 days |
| Source | `AgentStepRuns` |
| Storage | on-the-fly |
| Endpoint | `GET /api/v1/orgs/{organizationId}/statistics/agents/steps` |
| Access | `stats:view:agent` |

### S-18 · `agentTokenUsage`

| Field | Value |
| --- | --- |
| Description | Token consumption by agent, model, and workflow |
| Formula | `Σ (InputTokens + OutputTokens + CachedTokens)`, plus each component separately |
| Dimensions | `organizationId`, `agentKey`, `provider`, `model`, `workflowId`, `hour`/`day` |
| Granularity | hour |
| Freshness | `≤ 5 s` |
| Retention | 400 days |
| Source | `AgentWorkflowRuns`, `AgentStepRuns` |
| Storage | hybrid |
| Endpoint | `GET /api/v1/orgs/{organizationId}/statistics/agents/tokens` |
| Access | `stats:view:agent` |
| Notes | **Currently only one LLM call per workflow is measured** and only for the memory agent (gap G-4). `dataQuality.perStepAttribution: false` until fixed |

### S-19 · `agentCost`

| Field | Value |
| --- | --- |
| Description | Actual provider cost of agent work |
| Formula | `Σ ActualCostUsd`; `avg_cost_per_run = Σ ActualCostUsd / COUNT(*)` |
| Dimensions | `organizationId`, `agentKey`, `provider`, `model`, `day` |
| Granularity | day |
| Freshness | `≤ 1 h` |
| Retention | 400 days |
| Source | `AgentWorkflowRuns`, `AgentStepRuns` |
| Storage | rollup |
| Endpoint | `GET /api/v1/orgs/{organizationId}/statistics/agents/cost` |
| Access | `stats:view:agent` |
| Notes | Always `0` today (defect D-8) |

### S-20 · `agentToolUsage`

| Field | Value |
| --- | --- |
| Description | Tool call volume, success rate, and latency |
| Formula | `COUNT(*) GROUP BY ToolName, Status`; `success_rate = succeeded / total`; `avg_duration = Σ DurationMs / COUNT(*)` |
| Dimensions | `organizationId`, `agentKey`, `toolName`, `status`, `day` |
| Granularity | hour |
| Freshness | `≤ 5 s` |
| Retention | 90 days |
| Source | `AgentStepRuns` where `StepKind = 'ToolCall'` |
| Storage | on-the-fly |
| Endpoint | `GET /api/v1/orgs/{organizationId}/statistics/agents/tools` |
| Access | `stats:view:agent` |
| Notes | **No tool telemetry exists today** (gap G-6). Returns an empty series with `dataQuality.toolInstrumented: false` |

### S-21 · `agentApprovalWaitTime`

| Field | Value |
| --- | --- |
| Description | How long workflows wait for human approval, which is the human-in-the-loop bottleneck |
| Formula | `avg = Σ ApprovalWaitMs / COUNT(ApprovalWaitMs)`; `p95` over `ApprovalWaitMs`; `pending = COUNT(*) WHERE Status='PausedForApproval'` |
| Dimensions | `organizationId`, `agentKey`, `day` |
| Granularity | hour |
| Freshness | `≤ 5 s` |
| Retention | 400 days |
| Source | `AgentWorkflowRuns` |
| Storage | on-the-fly |
| Endpoint | `GET /api/v1/orgs/{organizationId}/statistics/agents/approvals` |
| Access | `stats:view:agent` |
| Notes | Meaningful only once D-5 (the ineffective checkpointer) is fixed; today a paused run cannot resume, so this measures a broken path |

### S-22 · `agentRunDetail`

| Field | Value |
| --- | --- |
| Description | Full step-by-step trace of one workflow run |
| Formula | `SELECT * FROM AgentStepRuns WHERE WorkflowRunId = @id ORDER BY StepIndex, AttemptNumber` |
| Dimensions | `workflowId` |
| Granularity | real-time |
| Freshness | `≤ 5 s` |
| Retention | 90 days |
| Source | `AgentWorkflowRuns`, `AgentStepRuns` |
| Storage | on-the-fly |
| Endpoint | `GET /api/v1/orgs/{organizationId}/statistics/agents/runs/{workflowRunId}` |
| Access | `stats:view:agent` |

### S-23 · `agentConcurrency`

| Field | Value |
| --- | --- |
| Description | Runs currently in flight — the queue depth of the AI subsystem |
| Formula | `COUNT(*) WHERE Status = 'Running'`, bucketed by `started_at` age |
| Dimensions | `organizationId`, `triggerKind` |
| Granularity | real-time |
| Freshness | `≤ 30 s` (collector interval) |
| Retention | not historical in the API; the sample series is retained per S-33 |
| Source | `AgentWorkflowRuns` |
| Storage | on-the-fly |
| Endpoint | `GET /api/v1/admin/statistics/system/queues` |
| Access | `stats:system` |

---

## 6. API consumption statistics

### S-24 · `apiRequestCount`

| Field | Value |
| --- | --- |
| Description | Request volume |
| Formula | `Σ RequestCount` over `ApiRequestMetrics` matching the filter |
| Dimensions | `organizationId`, `userId`, `apiKeyId`, `routeTemplate`, `httpMethod`, `statusClass`, `hour`/`day`/`month` |
| Granularity | hour |
| Freshness | `≤ 5 s` for the current hour (writer batch interval 2 s plus aggregation); `0 s` for completed hours |
| Retention | hourly rollup 90 days; daily rollup 400 days; raw logs 7 days |
| Source | `ApiRequestMetrics` (raw: `ApiRequestLogs`) |
| Storage | rollup |
| Endpoint | `GET /api/v1/orgs/{organizationId}/statistics/api/requests` |
| Access | `stats:view` |

### S-25 · `apiErrorRate`

| Field | Value |
| --- | --- |
| Description | Share of requests that failed, separated into client and server errors and throttle rejections |
| Formula | `client_error_rate = Σ RequestCount(4xx and not throttled) / Σ RequestCount`; `server_error_rate = Σ RequestCount(5xx) / Σ RequestCount`; `throttle_rate = Σ RequestCount(IsThrottled) / Σ RequestCount` |
| Dimensions | `organizationId`, `routeTemplate`, `statusCode`, `apiKeyId`, `hour`/`day` |
| Granularity | hour |
| Freshness | `≤ 5 s` |
| Retention | 400 days (day), 90 days (hour) |
| Source | `ApiRequestMetrics` |
| Storage | rollup |
| Endpoint | `GET /api/v1/orgs/{organizationId}/statistics/api/errors` |
| Access | `stats:view` |
| Notes | Throttling is separated from errors deliberately (FR-6.8): a 429 is not a client bug |

### S-26 · `apiLatency`

| Field | Value |
| --- | --- |
| Description | Response-time distribution per endpoint |
| Formula | Buckets are cumulative `le` counts. `p50/p95/p99` are obtained by linear interpolation within the bucket that contains the target rank: `value = lower + (upper − lower) × (rank − cumulative_below) / bucket_count`. `avg = Σ TotalDurationMs / Σ RequestCount` |
| Dimensions | `organizationId`, `routeTemplate`, `httpMethod`, `statusClass`, `hour`/`day` |
| Granularity | hour |
| Freshness | `≤ 5 s` |
| Retention | 400 days (day), 90 days (hour) |
| Source | `ApiRequestMetrics.BucketCounts`, `TotalDurationMs`, `MaxDurationMs` |
| Storage | rollup |
| Endpoint | `GET /api/v1/orgs/{organizationId}/statistics/api/latency` |
| Access | `stats:view` |
| Notes | Interpolation is approximate by construction — bucket granularity bounds the accuracy to the containing bucket's width, and the response states `precision: "bucket-interpolated"` |

### S-27 · `apiEndpointUsage`

| Field | Value |
| --- | --- |
| Description | Which endpoints an organisation or key actually calls, and how much |
| Formula | `Σ RequestCount GROUP BY RouteTemplate, HttpMethod` ordered by count |
| Dimensions | `organizationId`, `apiKeyId`, `routeTemplate`, `httpMethod`, `statusClass` |
| Granularity | day |
| Freshness | `≤ 1 h` |
| Retention | 400 days |
| Source | `ApiRequestMetrics` |
| Storage | rollup |
| Endpoint | `GET /api/v1/orgs/{organizationId}/statistics/api/endpoints` |
| Access | `stats:view` |

### S-28 · `apiKeyUsage`

| Field | Value |
| --- | --- |
| Description | Per-API-key consumption, the basis for key-level quota and billing |
| Formula | `Σ RequestCount GROUP BY ApiKeyId`; plus `lastUsedAt`, `topEndpoint`, `errorRate` |
| Dimensions | `organizationId`, `apiKeyId`, `day`/`month` |
| Granularity | hour |
| Freshness | `≤ 5 s` for counts; `≤ 5 min` for `lastUsedAt` (FR-3.18 amortisation) |
| Retention | 400 days |
| Source | `ApiRequestMetrics`, `ApiKeys` |
| Storage | rollup |
| Endpoint | `GET /api/v1/orgs/{organizationId}/statistics/api-keys` |
| Access | `stats:view` |

### S-29 · `apiUserUsage`

| Field | Value |
| --- | --- |
| Description | Per-user request volume, to distinguish human dashboard traffic from key traffic and to spot a compromised session |
| Formula | `Σ RequestCount GROUP BY UserId` |
| Dimensions | `organizationId`, `userId`, `routeTemplate`, `day` |
| Granularity | hour |
| Freshness | `≤ 5 s` |
| Retention | 400 days |
| Source | `ApiRequestMetrics` |
| Storage | rollup |
| Endpoint | `GET /api/v1/orgs/{organizationId}/statistics/api/users` |
| Access | `stats:view` |

### S-30 · `apiQuotaStatus`

| Field | Value |
| --- | --- |
| Description | Remaining quota for the current period, per metric and per key |
| Formula | `remaining = limit − used`; `resetsAt = PeriodEnd`; `percentUsed = used / limit` |
| Dimensions | `organizationId`, `apiKeyId`, `metricKey` |
| Granularity | real-time |
| Freshness | `≤ 60 s` (Redis counter, durable in `ApiQuotaUsage`) |
| Retention | 400 days |
| Source | Redis counters (live), `ApiQuotaUsage` (durable) |
| Storage | rollup |
| Endpoint | `GET /api/v1/orgs/{organizationId}/statistics/api/quota` |
| Access | `stats:view` |

### S-31 · `apiSlowestRequests`

| Field | Value |
| --- | --- |
| Description | Individual slow requests with the resource they touched, for diagnosis |
| Formula | `SELECT ... ORDER BY DurationMs DESC LIMIT n` where `DurationMs > Telemetry:SlowRequestMs` |
| Dimensions | `organizationId`, `routeTemplate`, `resourceType`, `hour` |
| Granularity | real-time |
| Freshness | `≤ 2 s` |
| Retention | 7 days (raw log) |
| Source | `ApiRequestLogs` |
| Storage | on-the-fly |
| Endpoint | `GET /api/v1/orgs/{organizationId}/statistics/api/slow-requests` |
| Access | `stats:view` |
| Notes | Returns `requestId` for correlation, never request or response bodies |

### S-32 · `apiBillableRequests`

| Field | Value |
| --- | --- |
| Description | Billable request count for a period, excluding health checks, OpenAPI, preflight, and 499s |
| Formula | `Σ RequestCount` where `RouteTemplate NOT IN excluded`, `HttpMethod <> 'OPTIONS'`, `StatusCode <> 499` |
| Dimensions | `organizationId`, `apiKeyId`, `month` |
| Granularity | month |
| Freshness | `≤ 1 h` |
| Retention | 400 days |
| Source | `ApiRequestMetrics` |
| Storage | rollup |
| Endpoint | `GET /api/v1/orgs/{organizationId}/statistics/api/billable` |
| Access | `billing:view` |

> **Shipped note (Phase 5, issues #220–#226).** S-24…S-32 are implemented over
> `ApiRequestMetrics` (hourly, exact and never sampled) and the sampled
> `ApiRequestLogs` for S-31. Deviations from the target definitions above:
>
> - **S-24/S-26/S-27.** Only `WindowSize = 'hour'` rows are produced. The 90-day
>   hourly → 400-day daily compaction is deferred because the shared dimension index
>   would collide a day row with the 00:00 hour row; see the backend README.
> - **S-26.** Percentiles are returned as `null` with `reason:
>   "insufficient_samples"` below `Telemetry:MinSampleForPercentile`, and the
>   response states `precision: "bucket-interpolated"`.
> - **S-28.** `lastUsedAt` comes from the `ApiKeys` row updated by
>   `ApiKeyUsageAggregator` at most once per minute (FR-3.18); `topEndpoint` is the
>   highest-volume route in the window.
> - **S-30.** `GET /quota` reads the durable `ApiQuotaUsage` period rows (live Redis
>   counters feed them through `ApiQuotaResetJob`). A limit of `0` means unlimited.
> - **S-32.** Billable is computed from the exact hourly rollup, excluding `/health`,
>   `/openapi`, `OPTIONS` and `499`s.

---

## 7. System statistics

### S-33 · `systemHealth`

| Field | Value |
| --- | --- |
| Description | Readiness of every dependency |
| Formula | Boolean AND of the individual check results; each check reports `name`, `status`, `durationMs`, `message` |
| Dimensions | none (single instance); `version` reported alongside |
| Granularity | real-time |
| Freshness | `0 s` |
| Retention | not stored; a sample is written to `SystemMetricSamples` every 30 s as `aveline.system.readiness` (1 = ready, 0 = not) |
| Source | `Microsoft.Extensions.Diagnostics.HealthChecks` |
| Storage | on-the-fly |
| Endpoint | `GET /health/ready` (public), `GET /health/live` (public), `GET /health` (alias of ready) |
| Access | anonymous; the payload contains no credentials, hostnames, or stack traces |

### S-34 · `systemUptime`

| Field | Value |
| --- | --- |
| Description | Process uptime and restart count |
| Formula | `uptime_seconds = now − process_start_time`; `restarts = COUNT(*)` of process-start events in the window |
| Dimensions | `instance` (from `Observability:InstanceId`) |
| Granularity | minute |
| Freshness | `≤ 30 s` |
| Retention | 30 days raw, 400 days hourly |
| Source | `SystemMetricSamples`, Prometheus `process_start_time_seconds` |
| Storage | hybrid |
| Endpoint | `GET /api/v1/admin/statistics/system/overview` |
| Access | `stats:system` |

### S-35 · `systemResourceUsage`

| Field | Value |
| --- | --- |
| Description | CPU, memory, GC, threads, and thread-pool saturation of the API process |
| Formula | `cpu_pct = process_cpu_seconds delta / wall delta × 100`; `working_set_bytes`; `gc_heap_bytes`; `thread_count`; `threadpool_queue_length` |
| Dimensions | `instance` |
| Granularity | 30 s |
| Freshness | `≤ 30 s` |
| Retention | 30 days raw, 400 days hourly average |
| Source | `.NET` runtime meters + `Process` counters, sampled by `SystemMetricCollector` |
| Storage | hybrid |
| Endpoint | `GET /api/v1/admin/statistics/system/metrics?metric=aveline.process.*` |
| Access | `stats:system` |

### S-36 · `systemQueueDepth`

| Field | Value |
| --- | --- |
| Description | Pending work in every in-process and Redis-backed queue |
| Formula | Per queue: `telemetry_channel_depth = Channel.Reader.Count`; `eventbus_backlog = _metrics.Published − _metrics.Delivered`; `notification_backlog = COUNT(NotificationDeliveries WHERE Status='Pending')`; `inbound_message_backlog = COUNT(InboundMessageLogs WHERE ProcessedAt IS NULL)`; `agent_runs_running = COUNT(AgentWorkflowRuns WHERE Status='Running')` |
| Dimensions | `queueName`, `instance` |
| Granularity | 30 s |
| Freshness | `≤ 30 s` |
| Retention | 30 days |
| Source | `Channel` internals, `EventBusMetrics` (`Infrastructure/Eventing/EventBusMetrics.cs`), `NotificationDeliveries`, `InboundMessageLogs`, `AgentWorkflowRuns` |
| Storage | hybrid |
| Endpoint | `GET /api/v1/admin/statistics/system/queues` |
| Access | `stats:system` |

### S-37 · `systemDatabaseMetrics`

| Field | Value |
| --- | --- |
| Description | Connection-pool saturation, query latency, and slow queries |
| Formula | `pool_in_use / pool_size`; `query_duration` histogram from EF Core command interceptors; `slow_queries = COUNT(*) WHERE duration > Observability:SlowQueryMs` |
| Dimensions | `instance`, `operation` (`select`/`insert`/`update`), `tableName` (from EF metadata, low cardinality) |
| Granularity | 30 s |
| Freshness | `≤ 30 s` |
| Retention | 30 days |
| Source | EF Core `DbCommandInterceptor` + `Npgsql` pool counters |
| Storage | hybrid |
| Endpoint | `GET /api/v1/admin/statistics/system/metrics?metric=aveline.db.*` |
| Access | `stats:system` |

### S-38 · `systemCacheMetrics`

| Field | Value |
| --- | --- |
| Description | Cache effectiveness and Redis latency |
| Formula | `hit_rate = hits / (hits + misses)`; `op_latency` histogram; `redis_healthy` from the existing `RedisHealthCheck` |
| Dimensions | `instance`, `cacheRegion` |
| Granularity | 30 s |
| Freshness | `≤ 30 s` |
| Retention | 30 days |
| Source | `IDistributedCache` decorator + `RedisHealthCheck` (`Infrastructure/Eventing/RedisHealthCheck.cs:16-28`) |
| Storage | hybrid |
| Endpoint | `GET /api/v1/admin/statistics/system/metrics?metric=aveline.cache.*` |
| Access | `stats:system` |

### S-39 · `systemErrorRate`

| Field | Value |
| --- | --- |
| Description | Server-error rate across the API, and unhandled exception count |
| Formula | `error_rate = Σ RequestCount(5xx) / Σ RequestCount`; `unhandled_exceptions = COUNT(log events at Error with an exception)` |
| Dimensions | `routeTemplate`, `statusCode`, `instance`, `minute`/`hour` |
| Granularity | minute |
| Freshness | `≤ 5 s` for the API-derived rate; `≤ 30 s` for the log-derived exception count |
| Retention | 400 days (hour), 30 days (minute) |
| Source | `ApiRequestMetrics`, structured logs |
| Storage | hybrid |
| Endpoint | `GET /api/v1/admin/statistics/system/errors` |
| Access | `stats:system` |

### S-40 · `systemThroughput`

| Field | Value |
| --- | --- |
| Description | Request and workflow throughput |
| Formula | `requests_per_second = Σ RequestCount / window_seconds`; `agent_runs_per_minute = COUNT(AgentWorkflowRuns) / window_minutes`; `blossoms_per_hour = Σ BlossomUnits / window_hours` |
| Dimensions | `instance`, `routeTemplate`, `triggerKind` |
| Granularity | minute |
| Freshness | `≤ 30 s` |
| Retention | 400 days (hour), 30 days (minute) |
| Source | `ApiRequestMetrics`, `AgentWorkflowRuns` |
| Storage | hybrid |
| Endpoint | `GET /api/v1/admin/statistics/system/throughput` |
| Access | `stats:system` |

### S-41 · `systemEventBusMetrics`

| Field | Value |
| --- | --- |
| Description | Redis pub/sub health: published, delivered, failed, and publish latency |
| Formula | Direct from `EventBusMetrics.Snapshot()` — already implemented as cumulative counters plus a latency histogram |
| Dimensions | `eventType`, `instance` |
| Granularity | 30 s |
| Freshness | `≤ 30 s` |
| Retention | 30 days |
| Source | `Infrastructure/Eventing/EventBusMetrics.cs`, exported every `Eventing:MetricsLogIntervalSeconds` (default 30) by `EventingMetricsExporter` |
| Storage | hybrid — the object already exposes `Snapshot()`; the collector samples it rather than re-instrumenting |
| Endpoint | `GET /api/v1/admin/statistics/system/eventbus` |
| Access | `stats:system` |

### S-42 · `systemAlerts`

| Field | Value |
| --- | --- |
| Description | Firing and historical alerts |
| Formula | `COUNT(*) GROUP BY Severity, Status`; plus the list with `firedAt`, `observedValue`, `threshold`, `occurrenceCount` |
| Dimensions | `severity`, `status`, `metricName`, `ruleId`, `day` |
| Granularity | real-time |
| Freshness | `≤ 60 s` (evaluation interval) |
| Retention | 400 days |
| Source | `SystemAlerts`, `SystemAlertRules` |
| Storage | on-the-fly |
| Endpoint | `GET /api/v1/admin/statistics/system/alerts` |
| Access | `stats:system` |

### S-43 · `systemOverview`

| Field | Value |
| --- | --- |
| Description | One aggregate payload for an operations dashboard: readiness, uptime, the top five alerts, throughput, error rate, queue depths, and active agent runs |
| Formula | Composition of S-33, S-34, S-39, S-40, S-36, S-23, S-42 for the last 15 minutes |
| Dimensions | none |
| Granularity | 30 s |
| Freshness | `≤ 30 s` |
| Retention | derived |
| Source | composite |
| Storage | on-the-fly, **not cached** (the earlier "cached 15 s" claim is corrected in #243; see the shipped note) |
| Endpoint | `GET /api/v1/admin/statistics/system/overview` |
| Access | `stats:system` |

> **Shipped note (Phase 6, issues #227–#230; alert pipeline corrected in #239).**
> S-33–S-43 are implemented as documented, with these deviations:
>
> - **S-33 readiness** is returned verbatim from `HealthCheckService`; the
>   `aveline.system.readiness` sample is not written by the collector (the readiness check
>   is synchronous and already exposed on `/health/ready`).
> - **S-34 uptime** is computed from `Process.StartTime`; `restarts` is not stored.
> - **S-35/S-36/S-39/S-40/S-41** are produced by `SystemMetricCollector`. It emits the
>   BR-7.8-compliant process/queue/event-bus/throughput names (`aveline.process.*`,
>   `aveline.queue.telemetry_channel`, `aveline.api.telemetry.dropped`, `aveline.eventbus.*`,
>   `aveline.api.requests_per_second`, `aveline.api.error_rate`, `aveline.agent.runs_running`)
>   and, since #239, also derives the business signals from existing tables:
>   `aveline.blossom.balance` (minimum `UsageAccounts.BlossomRemaining`),
>   `aveline.blossom.reconciliation.drift` (maximum absolute ledger/projection drift, the same
>   formula as `BlossomService.GetStatementAsync`), `aveline.blossom.consumed_rate` (blossoms
>   per minute over the last hour), `aveline.agent.success_rate` (succeeded / terminal runs
>   over the last hour), `aveline.agent.paused_count` (runs paused for approval),
>   `aveline.agent.steps_per_run` (mean `StepCount` of runs started in the last hour) and
>   `aveline.api.latency_p95` (bucket-interpolated p95 from `ApiRequestMetrics`). CPU seconds
>   use the `count` unit because the documented unit set has no `seconds` member.
> - **S-36** omits `inbound_message_backlog`: `InboundMessageLog` has no processed marker.
>   The response lists it in `omitted` (BR-7.10).
> - **S-37/S-38** database pool and cache metrics are not collected yet; the endpoints do
>   not expose them. Because there is no pool gauge to watch, the `db.pool.saturated` alert
>   rule (implementation-plan.md §7.4) is not seeded; eleven rules remain.
> - **S-39** reports the rate of `5xx` requests; unhandled-exception counts are not
>   instrumented and are listed in `omitted`.
> - **S-41** exposes the counter snapshot; `publish_latency_ms` is listed in `omitted`
>   because `EventBusMetrics` keeps counters only. `GET /system/eventbus` also ignores
>   the documented `from`/`to` window — the response is instantaneous, not a windowed
>   series (finding M-11).
> - **S-43** composes the same statistics over a 15-minute window and is **not cached**
>   server-side; the "cached 15 s" storage note above was stale (finding M-10).
> - **S-42 rules.** Every seeded `SystemAlertRule.MetricName` is a name the collector
>   produces; migration `20260913111104_FixSystemAlertRuleMetricNames` rewrites the rows M8
>   seeded with dead names, and `SystemMetricCollectorTests` guards the invariant.
> - **S-5 `marginLkr`** is omitted by default pending an external USD→LKR FX conversion source;
>   `ActualCostUsd` is reported directly in USD.
> - **S-9 `byViolatedKey`** requires key attribution stamped from the entitlement exception handler
>   into telemetry, as raw response bodies are deliberately never persisted in `ApiRequestLog`.

---

## 7b. Business KPIs (growth, activity, plan mix)

These are the administrator console's business reads. They live in
`Aveline.Api/Modules/Analytics`, are exposed under
`/api/v1/admin/statistics/business/*`, and are gated by the single permission
`analytics:business:read` (bearer-only; an API key is refused). Their windows are capped by
`BusinessAnalytics:MaxWindowDays` (default **400**), deliberately separate from
`Telemetry:MaxWindowDays` (92), because telemetry's cap is tuned for request forensics rather
than year-long growth reporting. Results are cached in `IDistributedCache` at
`BusinessAnalytics:CacheSeconds` (default 60) and carry
`Cache-Control: private, max-age=…`.

**Null versus zero, stated once for the family.** A count of `0` in a bucket that lies inside
the observed period is honest and is emitted as `0`. A bucket *before* the earliest
observation (`observedFrom`), or a measure that could not be computed at all, is `null`.
Every series point carries `isPartial`, true for the leading bucket clipped by `from` and for
the trailing bucket the window's `to` falls inside.

### S-44 · `businessGrowth`

| Field | Value |
| --- | --- |
| Description | New users, new organizations, and administrator access requests per bucket |
| Formula | `NewUsers = COUNT(*) FROM Users WHERE CreatedAt ∈ bucket AND DeletedAt IS NULL`; `NewOrganizations = COUNT(*) FROM Organizations WHERE CreatedAt ∈ bucket`; `NewAdminRequests = COUNT(*) FROM AdminApprovalRequests WHERE RequestedAt ∈ bucket`; `ApprovedAdminRequests = … AND Status = 'Approved'` |
| Dimensions | `granularity` (`day`\|`week`\|`month`) |
| Granularity | caller-selected |
| Freshness | `≤ 60 s` (cache TTL) |
| Retention | indefinite — `Users`/`Organizations` are not pruned |
| Source | `Users`, `Organizations`, `AdminApprovalRequests` |
| Storage | on-the-fly |
| Endpoint | `GET /api/v1/admin/statistics/business/growth` |
| Access | `analytics:business:read` |
| Notes | Buckets before the earliest row are absent and `observedFrom` names the boundary; buckets inside the period are `0` when truly empty. `previousTotals` covers the immediately preceding equal-length window. `Users.CreatedAt` is the **local first-seen** time, not Clerk's `created_at`: the `user.created` webhook stamps `DateTime.UtcNow`, and the JIT path on the first authenticated request stamps it too, so a user created in Clerk before the webhook was wired carries a later date. |

### S-45 · `businessActiveUsers`

| Field | Value |
| --- | --- |
| Description | Active users per bucket, plus the current DAU/WAU/MAU reading and stickiness |
| Formula | Per bucket: `COUNT(DISTINCT UserId) FROM ApiRequestMetrics WHERE WindowSize = 'day' AND UserId IS NOT NULL AND WindowStart ∈ bucket`. `Dau` = the same distinct-count over the trailing **1 day** ending at `to`; `Wau` over **7 days**; `Mau` over **30 days**. `Stickiness = Dau / Mau`. `ActiveOrganizations` = `COUNT(DISTINCT OrganizationId)` over the same rows. |
| Dimensions | `granularity` only. **There is no `definition` parameter** — one definition is settled, and offering a choice would invite comparing two numbers that measure different things |
| Granularity | caller-selected |
| Freshness | `≤ 60 s` |
| Retention | 400 days (`ApiStatsRetentionJob`) |
| Source | `ApiRequestMetrics` (day rows), which are exact and never sampled |
| Storage | on-the-fly over the existing rollups |
| Endpoint | `GET /api/v1/admin/statistics/business/active-users` |
| Access | `analytics:business:read` |
| Notes | **The definition is: an authenticated user who sent at least one request to the server within the window.** `UserId IS NOT NULL` is the filter, so anonymous and API-key traffic is excluded by construction. Three honesty rules: (1) when no row is attributed across the window the series is **`null`** with `dataQuality.userAttributionAvailable = false`, never `0`; (2) `dataQuality.unresolvedAttributionCount` carries the count of requests whose Clerk id was present but not yet in the claim map, so a stale map shows as a visible undercount rather than a low DAU; (3) SignalR-only sessions do not pass through `ApiTelemetryMiddleware` and are therefore not counted — a stated limit of this definition, not a defect. |

### S-46 · `businessPlanMix`

| Field | Value |
| --- | --- |
| Description | Current distribution of organizations, subscriptions and users across plan tiers, and the free-versus-premium split |
| Formula | **`OrganizationCount` and the free/premium split come from `Organizations.PlanTier`, which is authoritative for every organization.** Per tier: `OrganizationCount = COUNT(Organizations) WHERE PlanTier = tier`; `ActiveOrganizationCount = … AND IsActive`; `BilledSubscriptionCount = COUNT(OrganizationSubscriptions) WHERE PlanTier = tier AND Status IN ('Active','Trialing')`; `UserCount = COUNT(DISTINCT OrganizationMembership.UserId)` where the membership is `Active` and its org is that tier; `MonthlyPriceLkr = SUM(OrganizationSubscriptions.PriceLkr)` over billed rows |
| Dimensions | `planTier`; `asOf` instant |
| Granularity | snapshot |
| Freshness | `≤ 60 s` |
| Retention | not historical |
| Source | `Organizations` (**primary**), `OrganizationSubscriptions` (**supplementary**), `OrganizationMemberships` |
| Storage | on-the-fly |
| Endpoint | `GET /api/v1/admin/statistics/business/plan-mix` |
| Access | `analytics:business:read` |
| Notes | **Free = `PlanTier.Seed`; premium = `Bloom`, `Orchid`, `Rose`, `Enterprise`.** The response returns the per-tier rows **and** the two rolled-up sides so the definition is stated once, server-side. **`OrganizationCount` and `BilledSubscriptionCount` are deliberately two different fields**, because they are two different numbers: `OrganizationSubscriptions` holds one current row per organization, created only when a plan changes or is cancelled, so an organization that never changed plan has **no** row. The response exposes `organizationsTotal` and `organizationsWithBillingRow`, making the gap a visible subtraction. `TotalMonthlyPriceLkr` is a *list-price sum over billed rows only*, not recognised revenue: the provider columns exist but no provider client is written. |

### S-47 · `businessSubscriptionTrend`

| Field | Value |
| --- | --- |
| Description | Active subscription count over time, split by tier, with starts and cancellations per bucket |
| Formula | From the daily snapshot: `ActiveTotal` = the number of organizations with `Status IN ('Active','Trialing')` on the bucket's closing snapshot day; `ActiveByTier` the same grouped by `PlanTier`; `ChurnRate = Σ Cancelled over the window / OpeningActive` |
| Dimensions | `granularity`, `planTier` |
| Granularity | caller-selected over daily snapshots |
| Freshness | `≤ 24 h` (the snapshot runs daily at 02:00 UTC) |
| Retention | 400 days, matching `Telemetry:DailyRollupRetentionDays` |
| Source | **New** `OrganizationSubscriptionSnapshots`, whose tier column is taken from `Organizations.PlanTier` and whose billing columns come from `OrganizationSubscriptions` where a row exists; backfilled from `AuditLogEntries[Action='org.plan.changed']` |
| Storage | daily snapshot + rollup |
| Endpoint | `GET /api/v1/admin/statistics/business/subscriptions` |
| Access | `analytics:business:read` |
| Notes | **`ActiveTotal` counts *organizations* by tier, not subscription rows.** A row per active organization is written on every snapshot day whether or not a billing row exists, with `HasBillingRow` recorded, so the series cannot silently under-count. The snapshot is written at **02:00 UTC**, after `BillingRollupJob` at 01:30, so a tier change made the same day is already reflected. `dataQuality.subscriptionHistoryBackfilled` is `true` when any bucket was reconstructed from the audit ledger, and the console marks those buckets **approximate**. The backfill never reproduces the `"Grow"` phantom tier: an unparseable `AfterJson` yields no tier rather than a literal, and before the earliest parseable change the organization's live tier is reported as the series' floor. |

### S-48 · `businessUsageTrend`

| Field | Value |
| --- | --- |
| Description | Product usage per bucket: messages sent, agent runs, API calls, Blossom units consumed, and actual AI cost |
| Formula | `MessagesSent = COUNT(Messages) ⋈ Conversations WHERE CreatedAt ∈ bucket`; `AgentRuns = Σ DailyAgentMetrics.RunCount WHERE Day ∈ bucket`; `ApiRequests = Σ ApiRequestMetrics.RequestCount WHERE WindowSize='day' AND WindowStart ∈ bucket`; `BlossomUnits = Σ DailyBillingMetrics.BlossomUnits`; `ActualCostUsd = Σ DailyBillingMetrics.ActualCostUsd` |
| Dimensions | `granularity`, `organizationId` (optional) |
| Granularity | caller-selected |
| Freshness | `≤ 60 s` from the rollups; the rollups themselves are `≤ 1 h` (hour) / `≤ 24 h` (day) |
| Retention | 400 days for the rollups; messages indefinite |
| Source | `Messages` ⋈ `Conversations`, `DailyAgentMetrics`, `ApiRequestMetrics`, `DailyBillingMetrics` |
| Storage | on-the-fly over existing rollups |
| Endpoint | `GET /api/v1/admin/statistics/business/usage` |
| Access | `analytics:business:read`; when `organizationId` is supplied, the caller must additionally satisfy `admin:orgs:read` |
| Notes | When `organizationId` is absent, `ApiRequests` counts **every** request including unattributed ones (`BR-6.1`), which is correct for a platform total and is said in `dataQuality.notes`. When it is present, unattributed rows belong to no organization and are excluded. `dataQuality.agentMetricsUninstrumented` is `true`: the daily agent rollup has no user dimension, so per-user agent runs are not available. |

### S-49 · `businessOrganizationUsage`

| Field | Value |
| --- | --- |
| Description | Organizations ranked by a chosen usage measure over a window, with last-activity recency |
| Formula | Ranked `SUM` over the window of `messages`\|`agentRuns`\|`apiRequests`\|`blossomUnits`; `LastActivityAt = MAX(GREATEST(Conversation.LastMessageAt, AgentWorkflowRun day, ApiRequestMetric.WindowStart, AuditLogEntry.CreatedAt))`; `DaysSinceLastActivity = (now − LastActivityAt).Days` |
| Dimensions | `metric`, window, `limit` (≤ 100) |
| Granularity | window aggregate |
| Freshness | `≤ 60 s` |
| Retention | window ≤ 400 days |
| Source | `Conversations`, `DailyAgentMetrics`, `ApiRequestMetrics`, `AuditLogEntries`, `Organizations` |
| Storage | on-the-fly |
| Endpoint | `GET /api/v1/admin/statistics/business/organizations` |
| Access | `analytics:business:read` **and** `admin:orgs:read` |
| Notes | `LastActivityAt` is explicitly a **greatest-of reconstruction**, not a recorded fact, because there is no per-day user-activity table; the response sets `lastActivityIsReconstructed: true` and the UI labels the column. Ties are broken by `OrganizationId` so paging is stable. The endpoint enumerates tenant names, which is why it carries the second permission. |

---

## 7c. Revenue (Aveline's own income)

These are the administrator console's money reads. They live in
`Aveline.Api/Modules/Revenue`, are exposed under `/api/v1/admin/revenue/*` and
`/api/v1/admin/statistics/revenue/*`, and are gated by `revenue:read` (bearer-only; an API
key is refused), with the writes on `revenue:manage` and `revenue:refund`. Windows are capped
by `Revenue:MaxWindowDays` (default **400**), and results are cached in `IDistributedCache` at
`Revenue:CacheSeconds` (default 60) carrying `Cache-Control: private, max-age=…`.

### The two entry classes, stated once for the family

There is **no payment-provider client in this repository**. The provider columns on
`OrganizationSubscriptions` and `BlossomPriceEntries` exist and nothing writes them; the API
catalog records the position at `docs/api/README.md` (§C.2): *"Phase 3 attaches a payment
provider; until then a top-up is a recorded grant, not a charge."*

Money therefore arrives in the ledger in one of two classes, and **no response in this family
may conflate them**:

| `chargeBasis` | Meaning | Writer |
| --- | --- | --- |
| `Derived` | What the list price says *should* be billed. An expectation, not a receipt. | the top-up route (only when a `paymentReference` is supplied) and `BillingPeriodRolloverJob` |
| `Verified` | Money an Aveline operator confirmed was received, or a refund. | `POST /admin/revenue/ledger/verify` and `/refund` |

`revenueProviderSettlementAvailable` is `false` until a provider client settles money, so a
`Derived` entry is never described as collected.

### The `IncomeDataQualityDto` vocabulary

A **fifth** vocabulary, alongside system, agent, api and business, and deliberately not a reuse
of any of them: attribution and backfill say nothing about whether a price was configured or
whether a receipt was verified.

| Field | Meaning |
| --- | --- |
| `revenueProviderSettlementAvailable` | `false` until a provider client settles money. Every figure is then an expectation or an operator's confirmation |
| `subscriptionPricesConfigured` | `false` when every subscription's `PriceLkr` is `0`. A derived charge of `0` then means **no list price is configured** — not "free" — and MRR is `null`, never `0` |
| `derivedEntriesUnverified` | Count of `Derived` entries with no `Verified` counterpart. **This gap is the surface's most important number, and it is not an error** |
| `checkedAt` | When the flags were evaluated, distinct from the window's `to` |
| `notes` | Free-text, including the shared-cache-degradation note |

> **`PriceLkr` is never assigned today.** `SubscriptionService.UpsertSubscriptionAsync`
> (`:299-340`) sets tier, seats, status and period and leaves the price at `0m`. Until the price
> book populates it, `subscriptionPricesConfigured` is `false` and every MRR reading is `null`
> with that reason stated. A `0` MRR is a defect, not a measurement.

### S-50 · `revenueLedger`

| Field | Value |
| --- | --- |
| Description | The append-only revenue journal for a window, with window totals and a reconciliation block |
| Formula | Rows from `IncomeLedgerEntries` ordered `OccurredAt DESC, Id DESC`. `DerivedTotal = Σ Amount WHERE ChargeBasis = 'Derived' AND Status = 'Recorded'`; `VerifiedTotal = Σ … = 'Verified'`; `RefundTotal = Σ Amount WHERE Kind = 'Refund'`; `UnverifiedGap = DerivedTotal − VerifiedTotal`; `NetVerified = VerifiedTotal − RefundTotal` |
| Dimensions | `organizationId`, `kind`, `sourceKind`, `chargeBasis`, `q` (reason / `sourceRef` contains), window |
| Granularity | real-time |
| Freshness | `0 s` |
| Retention | indefinite — append-only, never pruned |
| Source | `IncomeLedgerEntries` |
| Storage | on-the-fly, paged |
| Endpoint | `GET /api/v1/admin/revenue/ledger` |
| Access | `revenue:read` |
| Notes | **The three totals are always returned as three, never summed into one.** `Amount` is stored positive and the sign is derived from `Kind`, so a refund cannot be mistaken for a charge. A `PriceLkr = 0` derived charge is a real row of `0` with `subscriptionPricesConfigured = false` naming why. An entry is never updated or deleted: a correction is a new row, and a nulled row is marked via `SupersedesEntryId` |

### S-51 · `revenueAccounts`

| Field | Value |
| --- | --- |
| Description | Per-organization derived, verified and net revenue over a window |
| Formula | `GROUP BY OrganizationId` over S-50's rows, with the same three totals plus `NetVerified` per organization |
| Dimensions | window; `organizationId` filter also accepted |
| Granularity | window aggregate |
| Freshness | `≤ 60 s` (cache TTL) |
| Retention | indefinite |
| Source | `IncomeLedgerEntries` ⋈ `Organizations` |
| Storage | on-the-fly |
| Endpoint | `GET /api/v1/admin/revenue/accounts` |
| Access | `revenue:read` |
| Notes | An organization whose every subscription has `PriceLkr = 0` appears with `derivedTotal = 0` **and** a `notes` entry explaining it, so a zero is not read as "this tenant is free by design" |

### S-52 · `revenueOverview`

| Field | Value |
| --- | --- |
| Description | MRR, ARR, ARPU and the paying-organization count |
| Formula | `Mrr = Σ OrganizationSubscriptions.PriceLkr WHERE Status IN ('Active','Trialing')` normalised to a month (an `Annual` row divides by 12); `Arr = Mrr × 12`; `PayingOrganizations = COUNT(DISTINCT OrganizationId) WHERE PriceLkr > 0`; `Arpu = Mrr / PayingOrganizations` |
| Dimensions | `asOf` instant |
| Granularity | snapshot |
| Freshness | `≤ 60 s` |
| Retention | not historical |
| Source | `OrganizationSubscriptions` |
| Storage | on-the-fly |
| Endpoint | `GET /api/v1/admin/statistics/revenue/overview` |
| Access | `revenue:read` |
| Notes | **`null` is not `0` here.** When `PayingOrganizations = 0`, `Arpu` is `null` rather than a division by zero, and when every `PriceLkr` is `0`, `Mrr` and `Arr` are `null` with `subscriptionPricesConfigured = false`. This is **list-price scheduled revenue**, not recognised or collected revenue — the response states `revenueProviderSettlementAvailable = false` and the console must not label it "collected" |

### S-53 · `revenueTimeseries`

| Field | Value |
| --- | --- |
| Description | Derived, verified and refunded amounts per bucket |
| Formula | `Σ Amount` over `IncomeLedgerEntries` grouped by bucket and `ChargeBasis`/`Kind`; `Refunded` is the `Refund` kind's magnitude |
| Dimensions | `granularity` (`day`\|`week`\|`month`), `organizationId` |
| Granularity | caller-selected |
| Freshness | `≤ 60 s` |
| Retention | indefinite |
| Source | `IncomeLedgerEntries` |
| Storage | on-the-fly |
| Endpoint | `GET /api/v1/admin/statistics/revenue/timeseries` |
| Access | `revenue:read` |
| Notes | The bucket axis is **dense**: a bucket with no rows is `0`, because a month with no charges is a real zero. A bucket *before* the earliest entry is absent, and the response names `observedFrom`. Every point carries `isPartial`, true for the leading bucket clipped by `from` and the trailing bucket the window's `to` falls inside. The derived and verified series are returned side by side and never merged into one line |

### S-54 · `revenueCollections`

| Field | Value |
| --- | --- |
| Description | Collection rate per period: verified receipts against derived charges |
| Formula | Per period, `DerivedTotal` and `VerifiedTotal` as S-50; `CollectionRate = VerifiedTotal / DerivedTotal`, `null` when `DerivedTotal = 0`; `Outstanding = DerivedTotal − VerifiedTotal` |
| Dimensions | `granularity`, `organizationId` |
| Granularity | caller-selected |
| Freshness | `≤ 60 s` |
| Retention | indefinite |
| Source | `IncomeLedgerEntries` |
| Storage | on-the-fly |
| Endpoint | `GET /api/v1/admin/statistics/revenue/collections` |
| Access | `revenue:read` |
| Notes | `CollectionRate` is `null`, never `0` or `∞`, when `DerivedTotal = 0` — a window with nothing billed has no collection rate. A rate above `100 %` is reported as-is rather than clipped, because it means a receipt arrived without a matching derived charge, which is exactly what an operator needs to see. `Outstanding` may be negative for the same reason |

### S-55 · `revenueBlossomSales`

| Field | Value |
| --- | --- |
| Description | Blossom top-up packs sold in a window: count, Blossoms granted, list-price value and conversion |
| Formula | From `IncomeLedgerEntries` where `Kind = 'TopUpPurchase'`: `PacksSold = COUNT(*)`, `BlossomsGranted = Σ` the matching `BlossomLedgerEntries.BlossomDelta WHERE EntryType = 'TopUpGrant'`, `ListPriceLkr = Σ Amount`, `VerifiedLkr = Σ Amount WHERE ChargeBasis = 'Verified'`; `Conversion = VerifiedLkr / ListPriceLkr`, `null` when `ListPriceLkr = 0` |
| Dimensions | `granularity`, `organizationId`, `skuCode` |
| Granularity | caller-selected |
| Freshness | `≤ 60 s` |
| Retention | indefinite |
| Source | `IncomeLedgerEntries` ⋈ `BlossomLedgerEntries` |
| Storage | on-the-fly |
| Endpoint | `GET /api/v1/admin/statistics/revenue/blossoms` |
| Access | `revenue:read` |
| Notes | A top-up **without** a `paymentReference` writes no income row at all, so `PacksSold` counts only referenced purchases. That is a deliberate undercount of *grants* and an accurate count of *sales*; the response states `grantedWithoutReference` so the difference between the two is visible rather than inferred |

### S-56 · `billingReconciliation`

| Field | Value |
| --- | --- |
| Description | Per-account Blossom reconciliation drift across every open account |
| Formula | For each open `UsageAccounts` row: `ledgerDerivedBalance = MonthlyBlossomLimit + Σ deltas excluding PeriodAllocation − BlossomUsed`; `drift = BlossomRemaining − ledgerDerivedBalance`; the response ranks accounts by `abs(drift)` descending |
| Dimensions | `limit` |
| Granularity | snapshot |
| Freshness | `≤ 60 s` |
| Retention | not stored; the same computation feeds the 30 s `aveline.blossom.reconciliation.drift` sample |
| Source | `UsageAccounts` ∪ `BlossomLedgerEntries` ∪ `AiUsageRecords` |
| Storage | on-the-fly |
| Endpoint | `GET /api/v1/admin/statistics/billing/reconciliation` |
| Access | `stats:system` |
| Notes | **Uses `BlossomService.LedgerDerivedBalance` and `ReconciliationDrift` unchanged**, because those two helpers are also what `SystemMetricCollector` emits for the `blossom.ledger.drift` alert; a second formula here would let the console and the alarm disagree. A non-zero drift is a **Critical** condition (S-3). This endpoint exists so a drift is findable from the console rather than only from Grafana |

---


## 8. Data quality warnings (must be returned to clients)

Because several underlying data points are not yet instrumented, every
statistics response carries a `dataQuality` object. A frontend that ignores it
will display zeros that look like real measurements.

| Flag | `true` means | Depends on |
| --- | --- | --- |
| `costIsEstimated` | `ActualCostUsd` is always 0 on the wire, so cost figures are placeholders | Gap G-5 / defect D-8 |
| `latencyInstrumented` | Agent latency is measured end-to-end | Gap G-3 / G-1 |
| `nodeFailuresObserved` | Node-level failures are reported, not swallowed | Gap G-2 |
| `perStepAttribution` | Tokens are attributed to the correct agent and node | Gap G-4 |
| `toolInstrumented` | Tool calls are recorded | Gap G-6 |
| `retryInstrumented` | Retries are counted | Gap G-7 |
| `materialisedCounts` | `UsageAccounts.StaffCount` and `ActiveCustomerCount` are populated | §4 addition |
| `streamingRunsIncluded` | Streaming runs report usage | Gap G-10 |
| `unattributedRunsExcluded` | Runs with no resolvable org are excluded from org-scoped numbers | Gap G-14 / D-7 |

Until every flag is `true`, the requirements document labels the corresponding
requirement as partially satisfied. This is deliberate: the catalog describes the
**target** statistic and states exactly what is missing, rather than silently
shrinking the definition to what happens to work today.

> **Shipped note (Phase 4, issues #210–#213).** The implemented `dataQuality`
> object on the agent statistics responses carries a deliberately smaller set than
> the table above: `latencyInstrumented`, `nodeFailuresObserved`,
> `perStepAttribution`, `toolInstrumented` and `costInstrumented` (the last named
> `costIsEstimated` here). All five are `false` until the Python instrumentation
> G-1…G-14 lands; `retryInstrumented`, `materialisedCounts`,
> `streamingRunsIncluded` and `unattributedRunsExcluded` are not emitted yet. A
> false `latencyInstrumented` makes `GET /latency` return a null series rather than
> zeros. See [README.md](README.md#implementation-status-phase-4--agentic-statistics).

### Home focus feed metrics (proposed — no `S-n` allocated here)

The Home focus feed is derived on every read (`GET /orgs/{id}/stats/home`), so
these are computed from the same generators rather than from a new table. Their
identifiers are allocated centrally at merge, per the series decision to stop
per-plan `S-n` numbering (plan strategy §5.7).

| Metric | Formula | Dimensions | Granularity | Endpoint | Access |
| --- | --- | --- | --- | --- | --- |
| `focusTaskBacklog` | `COUNT(*)` of the derived feed, grouped by `domain`; `COUNT(*) WHERE dueAtUtc < now` as `overdue` | `organizationId`, `domain`, `overdue` | real-time | `GET /api/v1/orgs/{organizationId}/stats/home` (`counts`) | `catalog:view` on an active membership (the feed's own gate) |
| `focusTaskCompletionRate` | `dismissals / (dismissals + dockets that left the feed without one)` over a window; `p50`/`p95` of `DismissedAtUtc − first seen`. Percentiles must be `null` with a `reason` below `Telemetry:MinSampleForPercentile` (default 20) | `organizationId`, `domain`, `window` | day | derived from `Focus_Dismissals` + the feed | `stats:view` |
| `homeFeedLatency` | p50/p95 server duration of the feed route | `organizationId`, `route` | hour | telemetry-derived; prefer the generic S-26 `apiLatency` unless a budget is needed | `stats:view` |
| `customerVisitCount` | `COUNT(*) FROM Customer_Interactions WHERE Channel = 'in_person' AND Direction = 'inbound' AND CreatedAt ∈ [from, to)` | `organizationId`, `channel`, `staffMemberId`, `day` | hour | derived from `Customer_Interactions` | `customers:view` |
| `customerVisitRecencyDistribution` | histogram of `now − LastVisitAt` bucketed (≤ 7 d, 8–30, 31–90, > 90) | `organizationId`, `bucket` | day | derived from `Customers` | `customers:view` |
| `walkInCreationCount` | `COUNT(*)` of customers whose create source is `counter_walkin` | `organizationId`, `day` | day | derived from `Customers.CreatedAt` + the create audit | `customers:view` |

`Focus_Dismissals` is the only new storage the feed adds; see
[domain-model.md](domain-model.md). It is not a metric source on its own beyond
the completion-rate numerator above.

### Conversations inbox metrics (proposed — no `S-n` allocated here)

Recorded by **name and formula only**; identifiers are allocated centrally at
merge, per the same series decision as the Home block above. The inbox releases
(`docs/architecture/inbox.md` §5.2) ships the read model; **no statistics route is
built yet**, so the endpoint column names the intended exposure rather than a
route that exists.

| Metric | Formula | Dimensions | Granularity | Endpoint | Access |
| --- | --- | --- | --- | --- | --- |
| `conversationOpenCount` | `COUNT(*) FROM Conversations WHERE OrganizationId = @org AND Status NOT IN ('Resolved','Archived')` | `organizationId`, `kind` | real-time | `GET /api/v1/orgs/{organizationId}/stats/conversations` (intended) | `stats:view` |
| `inboxFirstResponseLatency` | percentile of `firstStaffOrAgentReplyAt − firstClientMessageAt`, per conversation. `firstClientMessageAt` is the first message with `Kind = ClientMessage`; the reply is the first later `User` or `Agent` message. Percentiles must be `null` with a `reason` below `Telemetry:MinSampleForPercentile` (default 20) | `organizationId`, `kind`, `p50`/`p90` | day | same | `stats:view` |
| `conversationSignOffWaitTime` | percentile of `SignOffDecision.DecidedAt − Message.CreatedAt` for `Kind = SignOff`. **Revoked decisions are excluded**: a revoked-then-re-approved SignOff measures the final approval's wait, so the `revoked` rows added by the oversight path (domain-model §8.11) must be filtered out rather than counted as decisions. **Blocked on the producer**: nothing emits a `SignOff` yet (ADR-018); the write path that stages it is complete, so this needs no redefinition when the commerce flow ships. The agent-side analogue is `S-21 agentApprovalWaitTime` | `organizationId`, `approved`/`rejected` | day | same | `stats:view` |
| `conversationMessageVolume` | `COUNT(*)` of `Messages` joined to their conversation, grouped by the message's `AuthorKind` and the first meaningful content-block type (`lastMessageBlock`'s vocabulary). **Extend with a `direction` dimension**: `inbound = Kind = 'ClientMessage'`, `outbound = Kind <> 'ClientMessage'` | `organizationId`, `authorKind`, `blockType`, `direction` | hour | same | `stats:view` |
| `conversationRealtimeDeliveries` | `COUNT(*)` of `ReceiveConversationChanged` sends, grouped by event, target group (`org:{id}` vs `user:{id}`) and routing outcome | `organizationId`, `event`, `group` | hour | same | `stats:system` (admin only) |
| Duplicate-send rate | `COUNT(*) FROM Messages GROUP BY ConversationId, ClientMessageId HAVING COUNT(*) > 1` — must be **impossible** after the `clientMessageId` filtered unique index (domain-model §8.9), so a non-zero value is an alert rather than a metric | `organizationId`, day | day | same | `stats:view` |

**Attachment metrics** (thread-only; their tables land with the attachment slice, so
these are definitions the compute can be added to rather than live queries today):

| Metric | Formula | Dimensions | Access |
| --- | --- | --- | --- |
| Attachment volume | `COUNT(*) FROM MessageAttachments`, grouped by `ContentType`, `StorageProvider` and the bound message's direction | `organizationId`, `contentType`, `storageProvider`, `direction` | `stats:view` |
| Upload failures | `COUNT(*)` of rejected uploads, by reason (`type`, `size`) | `organizationId`, `reason` | `stats:view` |
| Orphaned attachments | `COUNT(*) FROM MessageAttachments WHERE MessageId IS NULL AND CreatedAtUtc < now() - TTL` — must return to zero as the sweep runs, so a flat non-zero value is the signal | `organizationId`, day | `stats:view` |

**Alerts worth defining** (need an alert-catalog entry, not invented here):

- `conversationRealtimeDeliveries` dropping to zero while `conversationMessageVolume`
  is non-zero — the signal that the inbox's or the thread's broadcast has stopped
  working.
- A duplicate-send rate above zero — the signal that the idempotency key is being
  reused or the index has been dropped.

**Read state adds no exposed metric yet.** The thread-owned
`ConversationReadStates` table now exists (domain-model §8.10), but the inbox draws
no badge, so there is no `conversationUnreadTotal` to expose. When the inbox wants
one it reads that table's aggregate (`unread = messages after the marker`, an absent
row = all unread) rather than growing a second read model; the metric would then be
recorded here by name, with no `S-n` allocated in this document.

### Notification gateway metrics (proposed — no `S-n` allocated here)

Recorded by **name and formula only**; identifiers are allocated centrally at merge,
per the same series decision as the Home and Conversations blocks above. The
notification gateway ([ADR-013](../ADR/ADR-013-notification-service-architecture.md))
ships the dispatcher, the channel adapters and the per-user inbox; **no statistics
route is built yet**, so the `Endpoint` column names the intended exposure rather
than a route that exists. Nothing is exposed until it appears here **and** in
[`docs/api/openapi.yaml`](../api/openapi.yaml) (§1).

| Metric | Description | Formula | Dimensions | Granularity | Freshness | Retention | Source | Storage | Endpoint (intended) | Access |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `notificationDeliverySuccessRate` | Share of delivery attempts that succeeded, per channel. | `COUNT(Status = 'Delivered') ÷ COUNT(*)` over `NotificationDeliveries` | `organizationId`, `channel` | day | ≤ 1 day | deliveries kept indefinitely (audit trail) | `NotificationDeliveries` | on-the-fly | `GET /api/v1/orgs/{organizationId}/stats/notifications` | `stats:view` |
| `notificationInboxReadRate` | Share of inbox rows the recipient has read. | `COUNT(ReadAt IS NOT NULL) ÷ COUNT(*)` over visible `UserNotifications` in the window | `organizationId`, `type` | day | ≤ 1 day | inbox rows: dismissed 30 d, read 180 d (S6); `NotificationRecords` kept | `UserNotifications` ⋈ `NotificationRecords` | on-the-fly | same | `stats:view` |
| `notificationDismissRate` | Share of inbox rows the recipient dismissed. | `COUNT(DismissedAt IS NOT NULL) ÷ COUNT(*)` over `UserNotifications` | `organizationId`, `type` | day | ≤ 1 day | inbox rows: 30 d after dismissal (S6) | `UserNotifications` ⋈ `NotificationRecords` | on-the-fly | same | `stats:view` |
| `notificationTimeToReadP50` | Median time from arrival to read, in minutes. | `percentile_cont(0.5)` of `ReadAt − CreatedAt` over read rows | `organizationId`, `type` | day | ≤ 1 day | inbox rows: 180 d after read (S6) | `UserNotifications` | on-the-fly | same | `stats:view` |
| `notificationFailureReasons` | Failed deliveries grouped by channel and error class. | `COUNT(*)` of failed rows, grouped by `Channel` and a normalised `ErrorMessage` class | `organizationId`, `channel`, `errorClass` | hour | ≤ 1 hour | deliveries kept indefinitely | `NotificationDeliveries` | on-the-fly | same | `stats:view` |
| `notificationVolumeByType` | How many notifications were dispatched, by kind. | `COUNT(*)` over `NotificationRecords` grouped by `Type` | `type`, `organizationId` | hour + day | ≤ 1 hour | records kept indefinitely | `NotificationRecords` | on-the-fly | same | `stats:view` |
| `notificationInboxBacklog` | **Open work items** at window end: inbox rows a user has not acted upon. Distinct from S-36's `notification_backlog`, which counts delivery rows with `Status = 'Pending'` (§2.5, S-36). | `COUNT(*)` of visible rows with `ReadAt IS NULL AND DismissedAt IS NULL` | `organizationId`, and per-`userId` only for an admin | snapshot | real-time | inbox rows only (S6) | `UserNotifications` | on-the-fly | same; the per-user cut is `GET /api/v1/admin/statistics/system/notifications` | `stats:view`; `stats:system` for the per-user cut |
| `pushDispatchFailures` | Failed push deliveries across every organisation. | `COUNT(*)` of `NotificationDeliveries WHERE Channel = 'Push' AND Status = 'Failed'` | `organizationId`, `platform` | hour | ≤ 1 hour | deliveries kept indefinitely | `NotificationDeliveries` ⋈ `UserDeviceTokens` | on-the-fly | `GET /api/v1/admin/statistics/system/notifications` | `stats:system` |
| `fcmCredentialConfigured` | Whether the real FCM channel was selected at startup, or the logging fallback. Push is a silent no-op otherwise. | `1` when `FirebaseConfiguration.IsConfigured` selected `FcmPushChannel`, else `0` | — | snapshot | real-time | n/a (configuration) | `NotificationsModule` channel selection | on-the-fly | `GET /api/v1/admin/statistics/system/notifications` | `stats:system` |

**Definitional notes.**

1. **`notificationInboxBacklog` is not S-36's `notification_backlog`.** S-36 counts
   delivery rows that have not been attempted (`NotificationDeliveries.Status = 'Pending'`,
   `stats:system`); the inbox backlog counts **work items a user has not acted upon**.
   The two have different denominators and different audiences; neither replaces the other.
2. **`notificationTimeToReadP50` is `null` below the sample floor.** Every percentile in
   this family is `null` with a `reason` when the sample count is below
   `Telemetry:MinSampleForPercentile` (default 20), per §1.
3. **`notificationInboxReadRate`, `notificationDismissRate` and
   `notificationInboxBacklog` are projections of per-user state**, not of the shared
   `NotificationRecord`. A fan-out kind writes one inbox row per recipient, so these
   denominators are per-recipient; the count's forward-compatible meaning ("open work
   under the model in force") is recorded in the notifications plan and needs no rename
   when the fan-out producer redefines it.

**Proposed alert thresholds** (recorded, not seeded — a seeded rule is itself a
`SystemAlert` producer, and routing it is the alert path's own concern):

- push failures > 20 % of attempts over 30 min → **Critical**;
- realtime delivery success < 95 % over 15 min → **Warning** (usually a reconnect problem);
- `notificationInboxBacklog` growing for 7 days with `notificationInboxReadRate < 10 %` →
  **Warning**;
- `fcmCredentialConfigured == 0` in production → **Critical** (push is a silent no-op).

---

### 8b. The business family's `dataQuality` (S-44…S-49)

The business reads carry their own flags rather than borrowing another family's vocabulary. A false
flag is named, never absorbed; a `null` measure plus the matching false flag means **"not measured"**,
which is not `0`.

| Flag | `true` means | Present on |
| --- | --- | --- |
| `userAttributionAvailable` | At least one request in the window carried a resolved user id, so the active-user measures are meaningful. `false` accompanies a **`null`** series, never a zero one | S-45 |
| `unresolvedAttributionCount` | How many requests carried a Clerk id that was present but not yet in the claim map. A non-zero value is a visible **undercount**, not a low DAU | S-45, all |
| `subscriptionHistoryBackfilled` | Some buckets were reconstructed from `AuditLogEntries[Action='org.plan.changed']` rather than snapshotted, so they are **approximate** | S-47 |
| `lastActivityIsReconstructed` | `LastActivityAt` is a greatest-of reconstruction over conversation, agent-run, API-metric and audit timestamps, not a recorded fact. Always `true` on S-49 | S-49 |
| `agentMetricsUninstrumented` | The `DailyAgentMetrics` rollup has no user dimension, so `AgentRuns` is an organization-level total and per-user agent activity is not measured | S-48 |
| `notes` | The server's own sentences about this answer. The console merges the notes from every endpoint it read rather than showing one and dropping the rest | all |

**What is deliberately not available, and is therefore never offered as a KPI.** Staff attendance
(the `TimeEntries` table has zero writers), per-user agent runs
(`AgentWorkflowRun.InitiatedByUserId` is always `null`), per-user AI usage (`AiUsageRecords` has no
user column) and login/session history (Aveline stores no session state). Each would need new
instrumentation and none is in this catalog.

---

## 9. Retention and aggregation summary

| Data | Raw retention | Rollup retention | Rollup job | Rollup table |
| --- | --- | --- | --- | --- |
| `AiUsageRecords` | 400 days | 400 days (daily) | `BillingRollupJob` hourly | `DailyBillingMetrics` |
| `BlossomLedgerEntries` | 400 days | none (low volume) | — | — |
| `UsageAccounts` | indefinite (one row per period) | — | — | — |
| `AgentWorkflowRuns` | 400 days | 400 days (daily) | `AgentStatsRollupJob` hourly | `DailyAgentMetrics` |
| `AgentStepRuns` | 90 days | none (rolled into the run aggregate) | — | — |
| `ApiRequestMetrics` | 90 days (hour), 400 days (day) | same table, `WindowSize` distinguishes | `ApiStatsRollupJob` hourly | `ApiRequestMetrics` |
| `ApiRequestLogs` | 7 days | n/a (source for rollups) | `ApiTelemetryWriter` continuous | `ApiRequestMetrics` |
| `SystemMetricSamples` | 30 days | 400 days (hourly) | `SystemMetricCollector` + hourly compaction | `SystemMetricSamples` |
| `AuditLogEntries` | 400 days minimum; configurable to indefinite | none | — | — |
| `SystemAlerts` | 400 days | none | — | — |
| `OrganizationSubscriptionSnapshots` | 400 days | n/a (already daily) | `OrganizationSubscriptionSnapshotJob` daily 02:00 UTC | `OrganizationSubscriptionSnapshots` |

Two rollup tables are implied but not defined in
[domain-model.md](domain-model.md) because they are derivable and their shape is
trivial: `DailyBillingMetrics` and `DailyAgentMetrics`, both keyed on
`(OrganizationId, Day, <dimensions>)` with pre-computed count/sum/percentile
columns. The implementation plan schedules them in Phase 4, because the
on-the-fly path is sufficient for the volumes this system will see in the first
two phases — and adding them early would be premature optimisation at the cost of
a migration.
