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
| Notes | `UsageAccount.StaffCount` and `ActiveCustomerCount` are **currently never written** (`UsageAccounts` columns exist but no writer updates them). This plan adds the counting job. Until then `dataQuality.materialisedCounts: false` |

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

Two rollup tables are implied but not defined in
[domain-model.md](domain-model.md) because they are derivable and their shape is
trivial: `DailyBillingMetrics` and `DailyAgentMetrics`, both keyed on
`(OrganizationId, Day, <dimensions>)` with pre-computed count/sum/percentile
columns. The implementation plan schedules them in Phase 4, because the
on-the-fly path is sufficient for the volumes this system will see in the first
two phases — and adding them early would be premature optimisation at the cost of
a migration.
