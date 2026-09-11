# Aveline Backend — Implementation Plan

**Status:** Proposed
**Companion:** [Requirements](backend-requirements.md) · [Domain Model](domain-model.md) · [Statistics Catalog](statistics-catalog.md) · [API Catalog](../api/README.md)

---

## 1. Architecture overview

### 1.1 Where this fits

`ADR-001` chose a **modular monolith**. The runtime is one ASP.NET Core process
(`net10.0`) plus one Python FastAPI process, one PostgreSQL 16 instance with
pgvector, and Redis. This plan changes none of that: every new capability is a new
module inside `Aveline.Api/Modules/`, following the existing
`Add{Name}Module` / `Map{Name}Endpoints` pattern
(`Aveline.Api/Modules/Billing/BillingModule.cs:12-24`).

```
                    ┌────────────────────────────────────────────────────┐
   React / Flutter  │  Clerk (identity)                                  │
        │           └────────────────────────────────────────────────────┘
        │  Bearer JWT                    JWKS
        ▼                                    │
┌───────────────────────────────────────────────────────────────────────┐
│  Aveline.Api  (net10.0, modular monolith)                             │
│                                                                       │
│  Cross-cutting pipeline (Program.cs:82-88)                            │
│    SecurityHeaders → CORS → CorrelationId* → Authentication           │
│    → Authorization → AuthAudit → Onboarding → ApiTelemetry* → routes  │
│    (* new)                                                            │
│                                                                       │
│  Existing modules            New / extended modules                   │
│  ─────────────────           ────────────────────                     │
│  CustomerConcierge           Billing        (extended → pricing,      │
│  VisualIntelligence                          ledger, entitlements)    │
│  Commerce                    Statistics     (new — agent, api, system)│
│  Conversations               Audit          (new)                     │
│  Notifications               ApiAccess      (new — API keys)          │
│  Organizations  ──────────►  extended: subscription, settings, members│
│  Shared (Users)  ─────────►  extended: profile, admin, sessions       │
│  Integrations                                                         │
└───┬───────────────────────────┬───────────────────────┬───────────────┘
    │ EF Core / Npgsql          │ X-Internal-Token      │ Redis
    ▼                           ▼                       ▼
┌──────────────┐   ┌────────────────────────┐   ┌──────────────────────┐
│ PostgreSQL16 │   │ agnet-service (FastAPI) │   │ Redis                │
│ + pgvector   │◄──┤ LangGraph workflows     │   │ event bus, cache,    │
│ + btree_gist │   │ + new run/step telemetry│   │ rate limits, quotas  │
└──────────────┘   └────────────────────────┘   └──────────────────────┘
```

### 1.2 Architectural decisions this plan makes

| # | Decision | Rationale | Alternative rejected |
| --- | --- | --- | --- |
| A-1 | Keep the modular monolith; add modules, do not split services | `ADR-001`; the statistics tables are joined to business tables in every query, and a service split would turn every dashboard read into a distributed join | Separate statistics service |
| A-2 | `UsageAccount` remains the billing-period ledger; **no** separate `BillingPeriod` entity | It already has `PeriodStart`/`PeriodEnd`, a unique index, and an endpoint contract. A second period entity creates two sources of truth for one boundary | `BillingPeriod` + `UsageAccount` |
| A-3 | Entitlement ledger is a **new append-only table** (`BlossomLedgerEntries`), with the projection on `UsageAccount` | Preserves the `ADR-010` two-table decision (immutable log + mutable ledger) while making add/deduct/remove auditable | Recompute `BlossomRemaining` from the ledger on every read (loses the O(1) property); or widen `UsageAccount` with ad-hoc columns (no audit trail) |
| A-4 | Consumption is **not** duplicated into the entitlement ledger | `AiUsageRecord` is already the consumption log and is the hot path; duplicating doubles write volume for no query benefit | Unified ledger with `Consumption` entries |
| A-5 | All request telemetry is aggregated **in-process into hourly rollups**; the raw log is sampled and partitioned | Avoids a per-request INSERT on the hot path; keeps 5 000 req/s feasible on one Postgres | Per-request row insert; or an external metrics backend (none is deployed) |
| A-6 | Agent run/step telemetry is reported **by the agent service over the existing internal HTTP channel**, in one batched call per workflow | Consistent with `ADR-010`'s "the .NET API owns persistence" decision, and with the existing `report_usage` contract. The alternative — direct DB writes from Python — has two migration owners | Direct SQLAlchemy writes from Python; or gRPC now |
| A-7 | `POST /internal/usage/record` is **kept and remains functional**; the richer `POST /internal/agent-runs` is added alongside | The existing endpoint has passing tests and a live client (`usage_reporter.py`). Breaking it would break the running system for no benefit | Replace the endpoint |
| A-8 | All percentages/aggregates are computed **at query time** in Phase 1–3; rollup tables come in Phase 4 | The data volumes are small (three boutiques, one university deployment), and rollups would be premature optimisation costing two extra migrations | Rollups from day one |
| A-9 | A **single generic `AuditLogEntries` table** serves every module | Every module needs the same shape; one table makes cross-module audit queries possible | Per-module audit tables |
| A-10 | API-key authentication is a **new authentication scheme**, not a policy | API keys are not JWTs; they must be resolved into a principal per request. Policies operate on an already-authenticated principal | Middleware that injects a synthetic JWT (fragile, bypasses the standard pipeline) |
| A-11 | Correlation IDs use a **new middleware** and the `X-Request-Id` header | None exists (defect D-11), and it is the join key between API statistics, agent runs, and the audit log | Relying on `HttpContext.TraceIdentifier` alone (not surfaced to clients, not propagated) |

### 1.3 What this plan deliberately does **not** do

- No request **blocking** when Blossoms are exhausted. `ADR-010` §Decision 6
  defers enforcement, and this plan does not reverse that. It makes the balance
  correct, which is a prerequisite.
- No payment-provider integration. `OrganizationSubscriptions` carries
  `ExternalProvider`/`ExternalSubscriptionId` so a provider can be attached later,
  but no provider client is written.
- No second database, no message broker beyond the existing Redis pub/sub.
- No changes to `backend/Aveline.Domain`, `Aveline.Application`, or
  `Aveline.Infrastructure`. Those directories are empty scaffolding that
  `Program.cs` does not reference.

---

## 2. Module and service breakdown

### 2.1 Extended: `Modules/Billing/`

Current contents: `AiUsageRecord`, `UsageAccount`, `PlanTier`,
`BillingDomainExceptions`, `IUsageTrackerService`/`UsageTrackerService`,
`IUsageRepository`/`UsageRepository`, `UsageEndpoints`, `OrgUsageEndpoints`,
`BillingModule`.

```
Modules/Billing/
  Models/
    AiUsageRecord.cs                 [EXTEND  +7 columns]
    UsageAccount.cs                  [EXTEND  +6 columns]
    PlanTier.cs                      [unchanged]
    PlanEntitlement.cs               [NEW]
    PlanEntitlementOverride.cs       [NEW]
    OrganizationSubscription.cs      [NEW]
    BlossomLedgerEntry.cs            [NEW]
    BlossomLedgerEntryType.cs        [NEW]
    IdempotencyRecord.cs             [NEW]
    BlossomConversionRule.cs         [NEW]
    BlossomPriceEntry.cs             [NEW]
    BillingDomainExceptions.cs       [EXTEND  +8 exception types]
  Repositories/
    IUsageRepository.cs              [EXTEND]
    UsageRepository.cs               [EXTEND]
    IBlossomLedgerRepository.cs      [NEW]  append-only; no update/delete
    BlossomLedgerRepository.cs       [NEW]
    IPricingRepository.cs            [NEW]
    PricingRepository.cs             [NEW]
    IEntitlementRepository.cs        [NEW]
    EntitlementRepository.cs         [NEW]
    IIdempotencyRepository.cs        [NEW]
    IdempotencyRepository.cs         [NEW]
    ISubscriptionRepository.cs       [NEW]
    SubscriptionRepository.cs        [NEW]
  Services/
    IUsageTrackerService.cs          [EXTEND  RecordWorkflowUsageAsync gains the pricing snapshot]
    UsageTrackerService.cs           [EXTEND  versioned pricing instead of a hardcoded formula]
    IBlossomService.cs               [NEW]  credit/debit/revoke/plan-change
    BlossomService.cs                [NEW]
    IPricingService.cs               [NEW]  rule resolution, price book, recompute
    PricingService.cs                [NEW]
    IEntitlementResolver.cs          [NEW]  the single source of plan truth
    EntitlementResolver.cs           [NEW]
    IIdempotencyService.cs           [NEW]
    IdempotencyService.cs            [NEW]
  Domain/
    BlossomCalculator.cs             [NEW]  pure functions: rounding, clamping
    IBlossomCalculator.cs            [NEW]
  Jobs/
    BlossomExpiryJob.cs              [NEW]
    BillingPeriodRolloverJob.cs      [NEW]
    IdempotencyRecordCleanupJob.cs   [NEW]
    PricingRuleCacheWarmer.cs        [NEW]
    PricingRecomputeJob.cs           [NEW]
  Endpoints/
    UsageEndpoints.cs                [unchanged contract]
    OrgUsageEndpoints.cs             [unchanged]
    BlossomEndpoints.cs              [NEW]  org + admin balance, statement, ops
    PricingEndpoints.cs              [NEW]  admin rules + price book
    SubscriptionEndpoints.cs         [NEW]  plan, change-plan, cancel, entitlements
  BillingModule.cs                   [EXTEND]
```

`BlossomCalculator` is deliberately a pure static/instance class with no
dependencies: rounding and clamping are the highest-risk arithmetic in the system
and must be unit-testable without a database or a configuration provider.

### 2.2 New: `Modules/Statistics/`

```
Modules/Statistics/
  Models/
    AgentWorkflowRun.cs              [NEW]
    AgentStepRun.cs                  [NEW]
    ApiRequestMetric.cs              [NEW]
    ApiRequestLog.cs                 [NEW]   (raw-SQL table; model is read-only)
    ApiQuotaUsage.cs                 [NEW]
    SystemMetricSample.cs            [NEW]
    SystemAlertRule.cs               [NEW]
    SystemAlert.cs                   [NEW]
    StatisticsDomainExceptions.cs    [NEW]
  Repositories/
    IAgentRunRepository.cs           [NEW]
    AgentRunRepository.cs            [NEW]
    IApiMetricRepository.cs          [NEW]
    ApiMetricRepository.cs           [NEW]
    ISystemMetricRepository.cs       [NEW]
    SystemMetricRepository.cs        [NEW]
    IAlertRepository.cs              [NEW]
    AlertRepository.cs               [NEW]
  Telemetry/
    ApiTelemetryMiddleware.cs        [NEW]
    ApiRequestSample.cs              [NEW]
    TelemetryChannel.cs              [NEW]   bounded Channel<ApiRequestSample>
    ApiTelemetryWriter.cs            [NEW]   BackgroundService
    RouteTemplateResolver.cs         [NEW]
    MetricDimensionHasher.cs         [NEW]
  Services/
    IAgentStatisticsService.cs       [NEW]
    AgentStatisticsService.cs        [NEW]
    IApiStatisticsService.cs         [NEW]
    ApiStatisticsService.cs          [NEW]
    ISystemStatisticsService.cs      [NEW]
    SystemStatisticsService.cs       [NEW]
    IQuotaService.cs                 [NEW]
    QuotaService.cs                  [NEW]
    IAlertService.cs                 [NEW]
    AlertService.cs                  [NEW]
    PercentileCalculator.cs          [NEW]   pure; bucket interpolation
  Jobs/
    ApiStatsRollupJob.cs             [NEW]
    AgentStatsRollupJob.cs           [NEW]
    ApiRequestLogPartitionJob.cs     [NEW]
    AgentStatsRetentionJob.cs        [NEW]
    SystemMetricCollector.cs         [NEW]
    AlertEvaluationJob.cs            [NEW]
    SystemMetricRetentionJob.cs      [NEW]
    StaleAgentRunJob.cs              [NEW]
  Endpoints/
    AgentStatisticsEndpoints.cs      [NEW]
    ApiStatisticsEndpoints.cs        [NEW]
    SystemStatisticsEndpoints.cs     [NEW]
    InternalAgentRunEndpoints.cs     [NEW]
  StatisticsModule.cs                [NEW]
```

### 2.3 New: `Modules/Audit/`

```
Modules/Audit/
  Models/AuditLogEntry.cs            [NEW]
  Models/AuditAction.cs              [NEW]   constant strings
  Repositories/IAuditRepository.cs   [NEW]   append-only
  Repositories/AuditRepository.cs    [NEW]
  Services/IAuditService.cs          [NEW]
  Services/AuditService.cs           [NEW]
  Services/IAuditRedactor.cs         [NEW]
  Services/AuditRedactor.cs          [NEW]
  Endpoints/AuditEndpoints.cs        [NEW]   GET only, admin
  AuditModule.cs                     [NEW]
```

`IAuditService.RecordAsync(...)` must never throw into the caller's path for a
non-critical action; failures are logged at Error and counted as a metric. The
one exception is a billing adjustment, where the audit write is part of the same
transaction and a failure aborts the operation — an unaudited money movement is
worse than a failed request.

### 2.4 Extended: `Modules/Organizations/`

| File | Change |
| --- | --- |
| `Models/Organization.cs` | +5 columns (`BillingEmail`, `ContactEmail`, `Currency`, `TimeZone`, `SuspendedAt`) |
| `Endpoints/OrganizationEndpoints.cs` | + `PATCH /orgs/{organizationId}`, `GET /orgs/{organizationId}/settings`, `GET /orgs/{organizationId}/members`, `PATCH /orgs/{organizationId}/members/{userId}` |
| `Services/IOrganizationService.cs`, `OrganizationService.cs` | + `UpdateAsync`, `ListMembersAsync`, `ChangeMemberRoleAsync`, `GetSettingsAsync` |
| `Services/OnboardingService.cs` | Replace `PlanBlossomLimits` with `IEntitlementResolver`; replace the literal `"org:principal"` with `Roles.BoutiqueOwner` (defect D-9) |
| `Webhooks/ClerkWebhookEndpoints.cs` | **NEW** — `POST /api/v1/webhooks/clerk` to keep the user/org read model in sync |

### 2.5 Extended: `Modules/Shared/` (Users) and new `Modules/ApiAccess/`

| File | Change |
| --- | --- |
| `Endpoints/UserEndpoints.cs` | + `PATCH /users/me`, `DELETE /users/me`, `GET /users/me/sessions`, `POST /users/me/sessions/revoke-all` |
| `Endpoints/AdminUserEndpoints.cs` | **NEW** — `GET /admin/users`, `PATCH /admin/users/{userId}/state` |
| `Modules/ApiAccess/**` | **NEW** — `ApiKey` model, repository, `ApiKeyService` (generate/hash/verify/revoke), `ApiKeyAuthenticationHandler`, `ApiKeyAuthenticationScheme`, `ApiKeyEndpoints` |

### 2.6 New: `Modules/SystemHealth/`

| File | Change |
| --- | --- |
| `HealthChecks/DatabaseHealthCheck.cs` | **NEW** |
| `HealthChecks/AgentServiceHealthCheck.cs` | **NEW** |
| `HealthChecks/ClerkJwksHealthCheck.cs` | **NEW** |
| `HealthChecks/HealthCheckResponseWriter.cs` | **NEW** — JSON shape for `/health/ready` |
| `Endpoints/HealthEndpoints.cs` | **NEW** — `/health/live`, `/health/ready`, `/health` |
| `Endpoints/MetricsEndpoints.cs` | **NEW** — `/metrics`, authenticated |

### 2.7 Cross-cutting: `Configurations/` and `Common/`

| File | Change |
| --- | --- |
| `Configurations/TelemetryConfiguration.cs` | **NEW** — registers the middleware, channel, writer, metrics |
| `Configurations/ObservabilityConfiguration.cs` | **NEW** — OTel meters, Prometheus exporter, EF command interceptor |
| `Configurations/IdempotencyConfiguration.cs` | **NEW** — the endpoint filter |
| `Common/Middleware/CorrelationIdMiddleware.cs` | **NEW** |
| `Common/Idempotency/IdempotencyEndpointFilter.cs` | **NEW** |
| `Common/Extensions/EndpointConventions.cs` | **NEW** — `.RequireIdempotency()` and `.WithTelemetryResource(...)` helpers |
| `Authorization/Permissions.cs` | **EXTEND** — 14 new permissions |
| `Authorization/Roles.cs` | **EXTEND** — grant the new permissions per role |
| `Configurations/AuthorizationConfiguration.cs` | **EXTEND** — always-available billing/stats policies |
| `Infrastructure/Data/AppDbContext.cs` | **EXTEND** — 15 new `DbSet`s |
| `Program.cs` | **EXTEND** — module registration and pipeline insertion |
| `Aveline.Api.csproj` | **EXTEND** — OTel + Prometheus packages, `Npgsql.Bulk`-free (uses `NpgsqlBinaryImporter`) |

---

## 3. Database schema and migrations

The full schema is in [domain-model.md](domain-model.md). The migration order,
safety properties, and command are in
[domain-model.md §10](domain-model.md#10-migration-sequence). Summary:

| Migration | Adds | Risk |
| --- | --- | --- |
| M1 `AddAuditLogEntries` | audit table | none |
| M2 `AddBlossomPricingRules` | rules, price book, `btree_gist`, exclusion constraint | none |
| M3 `AddBlossomLedgerAndUsageAccountBalance` | ledger, idempotency, 6 `UsageAccounts` columns, **backfill** | medium — the only data-mutating migration; backfill leaves `BlossomRemaining` numerically unchanged and is verified by a pre/post assertion test |
| M4 `AddPlanEntitlements` | entitlements, overrides, subscriptions + seed | none |
| M5 `AddApiKeys` | API keys | none |
| M6 `AddAgenticStatistics` | runs, steps, 7 `AiUsageRecords` columns | none |
| M7 `AddApiConsumptionStatistics` | metrics, quota usage, partitioned raw log + SQL functions | medium — requires the only hand-edited migration |
| M8 `AddSystemStatistics` | metrics, alert tables, 5 `Organizations` columns | none |

**Required PostgreSQL extensions** (added in M2/M3):

```sql
CREATE EXTENSION IF NOT EXISTS btree_gist;   -- exclusion constraint on time ranges
CREATE EXTENSION IF NOT EXISTS pgcrypto;     -- gen_random_uuid() in the M3 backfill
```

Both are available on standard managed PostgreSQL (Railway, Render, Azure
Flexible Server) and on the `pgvector/pgvector:pg16` image used by the
Testcontainers fixtures. **Verified as available** for pgvector images; for a
managed provider this must be confirmed before M2 is deployed — `CREATE
EXTENSION` requires superuser or a pre-approved extension allow-list. This is
tracked as [OQ-8](assumptions-and-open-questions.md).

**Migration safety gates.** Each migration must pass, in CI:

1. Apply against an empty database.
2. Apply against a database seeded with the Phase-0 schema plus representative
   rows (a fixture the plan adds).
3. For M3, assert `BlossomRemaining` is unchanged for every pre-existing
   `UsageAccounts` row.
4. `Down` must succeed and re-`Up` must be idempotent for M1–M6, M8. M7's `Down`
   drops the table and its functions; documented as destructive.

---

## 4. API route groups

Absolute paths include the `/api/v1` prefix. `{organizationId:guid}` is
mandatory on every tenant-scoped route because
`OrganizationScopeAuthorizationHandler` reads **only** that route value
(`Authorization/OrganizationScopeAuthorizationHandler.cs:38-43`).

| Group | Base | Policy family | Endpoints |
| --- | --- | --- | --- |
| Auth | `/api/v1/auth` | authenticated | `GET /claims` (existing) |
| Users | `/api/v1/users` | authenticated | `GET /me` (existing), `PATCH /me` (new), `DELETE /me` (new), `POST /onboarding` (existing), sessions (new), devices (existing) |
| Admin users | `/api/v1/admin/users` | `admin:users:*` | `GET ""`, `PATCH /{userId}/state` |
| Organizations | `/api/v1/orgs` | mixed | `POST ""` (existing), `GET /my` (existing), `GET /by-slug/{slug}` (existing), `GET /{organizationId}` (existing), `PATCH /{organizationId}` (new), `GET /{organizationId}/settings` (new), `GET /{organizationId}/members` (new), `PATCH /{organizationId}/members/{userId}` (new), existing suspend/activate/remove |
| Invitations | `/api/v1/orgs/{organizationId}/invitations`, `/api/v1/invitations` | `BoutiqueMembershipManage` | existing + no change |
| Subscription | `/api/v1/orgs/{organizationId}/subscription` | `billing:*` | `GET ""`, `POST /change-plan`, `POST /cancel` |
| Entitlements | `/api/v1/orgs/{organizationId}/entitlements` | `billing:view` | `GET ""`, `GET /usage` |
| Blossoms | `/api/v1/orgs/{organizationId}/blossoms` | `billing:*` | `GET /balance`, `GET /usage`, `GET /statement`, `POST /top-ups` |
| **Admin** Blossoms | `/api/v1/admin/orgs/{organizationId}/blossoms` | `billing:adjust` | `POST /credit`, `POST /debit`, `POST /revoke`, `GET /statement` |
| API keys | `/api/v1/orgs/{organizationId}/api-keys` | `apikeys:*` | `GET ""`, `POST ""`, `POST /{keyId}/revoke`, `DELETE /{keyId}` |
| Agent stats | `/api/v1/orgs/{organizationId}/statistics/agents` | `stats:view:agent` | `GET /runs`, `GET /runs/{workflowRunId}`, `GET /reliability`, `GET /latency`, `GET /steps`, `GET /tokens`, `GET /cost`, `GET /tools`, `GET /failures`, `GET /approvals` |
| API stats | `/api/v1/orgs/{organizationId}/statistics/api` | `stats:view` | `GET /requests`, `GET /errors`, `GET /latency`, `GET /endpoints`, `GET /users`, `GET /quota`, `GET /slow-requests`, `GET /billable` |
| API key stats | `/api/v1/orgs/{organizationId}/statistics/api-keys` | `stats:view` | `GET ""` |
| Billing stats | `/api/v1/orgs/{organizationId}/statistics/billing` | `billing:view` | `GET /burn-rate` |
| Customer/staff stats | `/api/v1/orgs/{organizationId}/statistics/{customers/staff}` | `billing:view` | `GET /customers/active`, `GET /staff/seats` |
| Admin stats | `/api/v1/admin/statistics` | `stats:system` | `GET /system/overview`, `/system/metrics`, `/system/queues`, `/system/errors`, `/system/throughput`, `/system/eventbus`, `/system/alerts`, `POST /system/alerts/{alertId}/acknowledge`, `/billing/profitability`, `/billing/org-usage`, `/billing/adjustments`, `/billing/plan-changes`, `/billing/downgrades` |
| Pricing (admin) | `/api/v1/admin/pricing` | `pricing:*` | `GET /rules`, `POST /rules`, `GET /rules/{ruleId}`, `PATCH /rules/{ruleId}`, `POST /rules/{ruleId}/activate`, `POST /rules/{ruleId}/cancel`, `POST /rules/{ruleId}/recompute`, price-book CRUD |
| Audit | `/api/v1/admin/audit` | `audit:view` | `GET ""`, `GET /{entryId}` |
| Internal — usage | `/internal/usage` | `InternalServicePolicy` | existing three, contract preserved |
| Internal — agent runs | `/internal/agent-runs` | `InternalServicePolicy` | `POST ""`, `POST /{workflowId}/steps`, `GET /{workflowId}` |
| Internal — telemetry | `/internal/telemetry` | `InternalServicePolicy` | `POST /alerts`, `GET /quota/{organizationId}` |
| Health | `/health`, `/health/live`, `/health/ready` | anonymous | |
| Metrics | `/metrics` | `InternalServicePolicy` or scrape token | |

### 4.1 New authorization policy constants

Add to `AuthorizationConfiguration.cs` (mirroring the existing constants at
`:12-43`) so endpoint code never contains a raw string:

```csharp
public const string BillingViewPolicy            = "BillingView";             // billing:view
public const string BillingManagePolicy          = "BillingManage";           // billing:manage, org-scoped
public const string BillingAdjustPolicy          = "BillingAdjust";           // billing:adjust, team-only
public const string PricingManagePolicy          = "PricingManage";           // pricing:manage
public const string ApiKeysViewPolicy            = "ApiKeysView";             // apikeys:view, org-scoped
public const string ApiKeysManagePolicy          = "ApiKeysManage";           // apikeys:manage, org-scoped
public const string StatsViewPolicy              = "StatsView";               // stats:view, org-scoped
public const string StatsAgentPolicy             = "StatsAgent";              // stats:view:agent, org-scoped
public const string StatsSystemPolicy            = "StatsSystem";             // stats:system, team-only
public const string AuditViewPolicy              = "AuditView";               // audit:view
```

Every org-scoped one is a new `OrganizationScopeRequirement(<permission>)`, exactly
as `BoutiqueAccess`, `BoutiqueMembershipManage`, and `BoutiqueConversationAccess`
already are (`AuthorizationConfiguration.cs:73-89`). Team-only ones use
`RequireRole(Roles.Owner, Roles.Admin)` mirroring `AdminReviewPolicy`
(`:68-69`).

There is still **no `RequirePermission` helper**, and this plan does not add one —
the established convention is `.RequireAuthorization(<policyConstant>)`.

---

## 5. Background jobs and queues

### 5.1 Jobs

| Job | Type | Cadence | Responsibility | Idempotent? |
| --- | --- | --- | --- | --- |
| `ApiTelemetryWriter` | `BackgroundService` (continuous) | batch of 500 or every 2 s | Drain `TelemetryChannel`; upsert `ApiRequestMetrics`; sample into `ApiRequestLogs` | Yes — upsert is additive per batch, and duplicate batches are prevented by an in-memory sequence |
| `ApiRequestLogPartitionJob` | Hosted, timer | daily 00:05 UTC | Create tomorrow's partition; drop partitions past 7 days | Yes |
| `ApiStatsRollupJob` | Hosted, timer | hourly at :05 | Recompute the completed hour; compact hour→day at 90 days | Yes — recompute-and-replace |
| `AgentStatsRollupJob` | Hosted, timer | hourly at :10 | Roll `AgentWorkflowRuns` into `DailyAgentMetrics` | Yes |
| `AgentStatsRetentionJob` | Hosted, timer | daily 03:00 | Delete `AgentStepRuns` > 90 d, `AgentWorkflowRuns` > 400 d | Yes |
| `SystemMetricCollector` | `BackgroundService`, timer | 30 s | Sample process, DB, cache, queue, throughput metrics | Yes |
| `AlertEvaluationJob` | Hosted, timer | 60 s | Evaluate `SystemAlertRules`; fire/aggregate/resolve alerts | Yes — cooldown makes re-fire a no-op |
| `SystemMetricRetentionJob` | Hosted, timer | daily 03:30 | Delete raw samples > 30 d | Yes |
| `StaleAgentRunJob` | Hosted, timer | hourly | Mark `PausedForApproval` runs past timeout as `TimedOut` | Yes |
| `BlossomExpiryJob` | Hosted, timer | hourly | Write `Expiry` entries for grants past `ExpiresAt` | Yes — `NOT EXISTS` guard on `SupersedesEntryId` |
| `BillingPeriodRolloverJob` | Hosted, timer | hourly | Open the next `UsageAccount`; close the previous period | Yes — unique index on `(OrganizationId, PeriodStart)` |
| `IdempotencyRecordCleanupJob` | Hosted, timer | daily 04:00 | Delete expired replay records | Yes |
| `PricingRuleCacheWarmer` | Hosted, timer | 60 s | Pre-resolve active conversion rules into L1 | Yes |
| `PricingRecomputeJob` | On-demand, queued | admin-triggered | Recompute affected `AiUsageRecord` rows; write `CorrectionRecompute` entries | Yes — pinned `asOf` |

### 5.2 Hosting pattern

All jobs follow the existing `BackgroundService` pattern already used by
`EventingMetricsExporter` (`Infrastructure/Eventing/EventingMetricsExporter.cs:14-47`),
with three additions this plan requires:

1. A `PeriodicTimer` rather than `Task.Delay`, so a slow run does not drift.
2. A `JobRun` structured log at start and end with `job`, `durationMs`,
   `processed`, `failed`.
3. A distributed lock via `IDistributedCache` (`SET NX PX`) so that a
   multi-instance deployment does not run the same job twice. This is the one
   piece of infrastructure that does not exist and must be written
   (`Common/Jobs/DistributedJobLock.cs`).

### 5.3 Queues

There is no message broker. Two in-process queues are introduced, both bounded
`Channel<T>`:

| Queue | Capacity | Full behaviour | Consumer |
| --- | --- | --- | --- |
| `TelemetryChannel` | `Telemetry:BufferCapacity` (10 000) | Drop oldest, increment `Telemetry:DroppedSamples` | `ApiTelemetryWriter` |
| `PricingRecomputeChannel` | 100 | Reject the enqueue and return 503 to the admin | `PricingRecomputeJob` |

Redis pub/sub remains the cross-instance transport, used for cache invalidation
and module events, exactly as `ADR-014` established.

---

## 6. Caching strategy

| Layer | Cache | TTL | Invalidation | Notes |
| --- | --- | --- | --- | --- |
| L1 (in-process) | `IMemoryCache` | 5–60 s | On the corresponding `*.{changed}` event | Already available via `Microsoft.Extensions.Caching.Memory`, transitively present |
| L2 (distributed) | `IDistributedCache` → Redis, `InstanceName = "aveline:"` | minutes to hours | Event-driven | Already configured (`Configurations/CacheConfiguration.cs:24-47`) |
| L3 (persisted) | Postgres rollup tables | n/a | Job recompute | Phase 4 |

| Cached item | Key | TTL | Invalidated by |
| --- | --- | --- | --- |
| Resolved conversion rule | `pricing:rule:{provider}:{model}:{yyyyMMdd}` | 5 s L1 / 5 min L2 | `pricing.rule.activated`, `pricing.rule.cancelled` |
| Entitlements for an org | `entitlements:{organizationId}` | 60 s | `org.plan.changed`, `entitlement.override.changed` |
| Plan entitlement rows | `entitlements:plan:{planTier}` | 5 min | `PlanEntitlement` write |
| API key by prefix | `apikey:{prefix}` | 60 s | `apikey.revoked`, `apikey.deleted` |
| User authorization snapshot | existing `UserCacheService` | existing | existing invalidation calls; **extend to role and account-state changes** |
| Usage summary for an org | `usage:summary:{organizationId}` | **not cached** | Deliberately uncached: the balance is the most correctness-sensitive read in the product and a stale balance is worse than a database round-trip |
| System overview | `stats:system:overview` | 15 s | TTL only |
| Latency percentiles | `stats:latency:{hash}` | 60 s | TTL only |
| Quota counters | `quota:{org}:{key}:{period}` | period end | Redis `INCR`; durable copy written at rollover |

**Cache key hygiene.** Every key is prefixed `aveline:` by the existing
`InstanceName` configuration. Keys must never embed customer PII — org and user
ids only.

---

## 7. Observability

### 7.1 Logs

- Structured logging with named placeholders, matching the existing convention
  (`WebhookEndpoints.cs:82-84`). JSON console is already supported via
  `Logging:UseJsonConsole` (`Configurations/LoggingConfiguration.cs:22-32`).
- **New:** `CorrelationIdMiddleware` reads `X-Request-Id` (or generates a UUIDv7),
  validates the format (FR-6.11), pushes it into a `using`-scoped log scope, and
  echoes it on the response. This is the join key for API statistics, agent runs,
  and the audit log.
- **New:** every log entry in a request scope gains `requestId`, `traceId`,
  `organizationId`, `userId`, `apiKeyPrefix`.
- **Never logged:** secrets, tokens, API key hashes, prompt text, message bodies,
  raw IP addresses, integration credential plaintext or ciphertext.
- Log levels: `Information` for lifecycle (job start/end, request completion at
  `Debug`), `Warning` for recoverable anomalies and every 401/403 (existing
  `UseAvelineAuthAudit`, `LoggingConfiguration.cs:38-59`), `Error` for unhandled
  exceptions and failed jobs.

### 7.2 Metrics

- **New:** the .NET API gains OpenTelemetry (`OpenTelemetry.Extensions.Hosting`,
  `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`,
  `OpenTelemetry.Instrumentation.EntityFrameworkCore`,
  `OpenTelemetry.Exporter.Prometheus.AspNetCore`,
  `OpenTelemetry.Exporter.OpenTelemetryProtocol`). Version pins must match the
  .NET 10 line; see [OQ-9](assumptions-and-open-questions.md).
- **Decision:** Prometheus exporter on `/metrics` **and** OTLP export to the
  existing `otel-collector` so the .NET and Python services land in one trace view.
  The collector config already exists (`otel-collector-config.yaml`) and is wired
  to the Python service only (`docker-compose.yml:155-159,171-194`).
- Metric naming: `aveline.<subsystem>.<measure>`, e.g.
  `aveline.http.server.request.duration`, `aveline.blossom.consumed`,
  `aveline.agent.run.duration`, `aveline.api.telemetry.dropped`.
- Existing `EventBusMetrics` counters are kept and additionally bridged into the
  OTel meter, so history is not lost
  (`Infrastructure/Eventing/EventBusMetrics.cs`).

### 7.3 Traces

- ASP.NET Core instrumentation is automatic once OTel is registered.
- **New:** `ActivitySource` named `Aveline.Api` with spans for
  `blossom.adjust`, `pricing.resolve`, `agent.run.ingest`, and `stats.query`.
- **Correlation across services:** the internal `AgentServiceClient` propagates
  `traceparent` (W3C) and `X-Request-Id`. The Python service must accept
  `traceparent`; the `opentelemetry-instrumentation-httpx` package is already
  installed (`agnet-service/requirements.txt:38`) but no exporter is configured
  because `OTEL_EXPORTER_OTLP_ENDPOINT` defaults to empty
  (`agnet-service/app/core/config.py:47`). Setting that variable is a Phase-2
  deliverable, not new code.
- The `Message.TraceId` column already exists
  (`Modules/Conversations/Models/Message.cs:42`) and `EventEnvelope` already
  carries `TraceId` (`Infrastructure/Eventing/EventEnvelope.cs:24,27`) — this plan
  populates them instead of leaving them `null`.

### 7.4 Alerts

Alert rules are data (`SystemAlertRules`), evaluated by `AlertEvaluationJob`, and
delivered through the existing notification module via `IRecipientResolver`
(`Modules/Notifications/Services/OrganizationRecipientResolver.cs`). Seeded
rules:

| Name | Metric | Condition | Severity |
| --- | --- | --- | --- |
| `blossom.balance.negative` | `blossom.balance` | `< 0` for 5 min | Critical |
| `blossom.ledger.drift` | `blossom.reconciliation.drift` | `!= 0` | Critical |
| `blossom.runaway.org` | `blossom.consumed.rate` | `> 5× org baseline for 15 min` | Warning |
| `api.error.rate` | `api.error_rate` | `> 5 % for 10 min` | Critical |
| `api.latency.p95` | `api.latency.p95` | `> 2000 ms for 10 min` | Warning |
| `agent.failure.rate` | `agent.success_rate` | `< 80 % for 15 min` | Warning |
| `agent.run.stuck` | `agent.paused.count` | `> 10 for 60 min` | Warning |
| `agent.step.runaway` | `agent.steps.per_run` | `> 200` | Warning |
| `telemetry.dropped` | `aveline.api.telemetry.dropped` | `> 0 for 5 min` | Warning |
| `db.pool.saturated` | `aveline.db.pool_in_use` | `> 90 % for 5 min` | Critical |
| `queue.telemetry.backlog` | `aveline.queue.telemetry_channel` | `> 8000 for 5 min` | Warning |
| `eventbus.failed` | `aveline.eventbus.failed` | `> 10/min` | Critical |

`blossom.ledger.drift` is the single most important rule in this list: it is the
integrity check for the entire Blossom system.

---

## 8. Security

### 8.1 Authentication

Three schemes after this plan, all registered on the default `AuthenticationOptions`:

| Scheme | Mechanism | Evidence / change |
| --- | --- | --- |
| `Bearer` (default) | Clerk JWT via JWKS, `ValidateIssuer=true`, `ValidateAudience=false`, `ValidateLifetime=true`, `ValidateIssuerSigningKey=true` | Existing (`Configurations/AuthenticationConfiguration.cs:92-100`) |
| `InternalToken` | `X-Internal-Token` shared secret, constant-time compare, role `InternalService` | Existing (`Infrastructure/Integrations/InternalTokenAuthenticationHandler.cs:24-57`) |
| `ApiKey` | **New.** `X-Api-Key: avl_live_<32 chars>`; prefix lookup, SHA-256 hash compare in constant time, resolves to a principal with a fixed `OrganizationId` | New |

**No second credential type for the same route without an explicit policy.** A
route declares exactly one scheme. The `ApiKey` scheme is never applied to
`/api/v1/admin/**`, `/api/v1/users/**`, or `/internal/**`.

### 8.2 Authorization

- Fallback policy remains `options.DefaultPolicy` (authenticated), so new routes
  are closed by default (`AuthorizationConfiguration.cs:98`).
- Two-layer tenant enforcement is preserved and extended: the
  `OrganizationScopeAuthorizationHandler` policy decides *access*, and every
  repository query filters `OrganizationId` because **no EF global tenant filter
  exists**. New repositories must not rely on a filter that is not there.
- The `ApiKey` principal sets `OrganizationId` from the key row, and the handler
  additionally asserts that the route's `{organizationId}` equals the key's org —
  a key for org A on a route for org B returns **404**, not 403, to avoid leaking
  org existence.
- Permission catalog grows by 14 entries (§4.1 of the requirements). The
  affected roles and their grants must be re-derived in `Permissions.cs`, and
  `docs/architecture/authorization.md` must be **regenerated from the code**, not
  hand-edited — it is already stale (defect D-9).

### 8.3 Secrets

| Secret | Dev | CI | Prod |
| --- | --- | --- | --- |
| `Clerk:Authority` | `appsettings.Development.json` (non-secret URL) | env `Clerk__Authority` | env / platform secret store |
| `Clerk:SecretKey` (Backend API) | user-secrets | env `Clerk__SecretKey` | platform secret store |
| `AgentService:InternalToken` | `appsettings.Development.json` | env `AgentService__InternalToken` | platform secret store |
| `ConnectionStrings:Default` | `.env.local` | Testcontainers / in-memory | platform secret store |
| `Redis:Configuration` | `docker-compose.yml` | fakeredis / Moq | platform secret store |
| Integration credentials | AES-256-GCM via `ICredentialEncryptionService`, key from `Encryption:Key` | existing | existing — `ADR-011` |
| `Metrics:ScrapeToken` | unset (scheme disabled) | unset | platform secret store |
| `Embeddings:ApiKey`, `Vision:ApiKey`, `PaymentGateway:ApiKey`, `Courier:ApiKey` | env only, absent in dev means the feature is inert | env | platform secret store |

Rules, enforced by CI (the existing `ci.yml` already checks for committed `.env`
files):

1. No secret in `appsettings*.json`.
2. Required secrets fail **at startup** with `InvalidOperationException`, matching
   `AuthenticationConfiguration.cs:29-30` and `CorsConfiguration.cs:28`.
3. API key plaintext is never logged, never stored, never returned after creation.
4. The audit `BeforeJson`/`AfterJson` passes through `IAuditRedactor`.

### 8.4 Rate limiting and quotas

Two distinct mechanisms, deliberately:

| Mechanism | Where | Implementation | Fails |
| --- | --- | --- | --- |
| **Rate limit** (per minute) | Existing `IRateLimiter` for edge/abuse endpoints; **new atomic Redis Lua counter** for per-API-key limits | `DistributedRateLimiter` is a non-atomic read-modify-write and **fails open** (`Infrastructure/RateLimiting/DistributedRateLimiter.cs:42-66`). It is kept for the two existing call sites (WhatsApp webhook at 120/min, invitation accept at 10/min) but **must not** be used for billing-relevant key limits | Open (documented) |
| **Quota** (per month/day) | New `IQuotaService` over Redis `INCR` with a period-scoped key; durable `ApiQuotaUsage` at rollover | Atomic | Closed (429 when exhausted) |

Nuance: a *rate* limiter failing open is acceptable (availability wins); a
*quota* counter failing open would give away unbilled usage, so the quota path
must fail closed — if Redis is unreachable, the request is allowed but the
shortfall is recorded and reconciled from `ApiRequestMetrics`, which is exact and
unsampled. This asymmetry is intentional and must be documented in code comments.

### 8.5 Input validation and injection

- Request DTOs are `record`s; validation uses `System.ComponentModel.DataAnnotations`
  with the existing `Results.ValidationProblem` shape
  (`OnboardingEndpoints.cs:41-47`).
- All data access goes through EF Core or Npgsql parameterised commands. The
  `pgvector` raw-SQL path already established in
  `Modules/CustomerConcierge/Repositories/CustomerMemoryRepository.cs` is the
  precedent for raw SQL and must keep parameterising.
- The new `/metrics` Prometheus text output must not echo request paths,
  organisation ids, or user ids into metric labels — a label set derived from
  user input is an unbounded-cardinality denial of service and an information
  leak. Only the enumerated dimension sets in the statistics catalog are allowed
  as labels.
- Statistics endpoints must validate that every group-by dimension is in the
  allow-list; an unvalidated `groupBy` reaching EF's dynamic LINQ path is an
  injection vector. **The plan requires a compile-time allow-list, not a
  string-to-expression converter.**

### 8.6 Privacy

- Telemetry tables store ids, counts, durations, hashes, and status codes only —
  never prompt text, tool arguments, message bodies, raw IPs, or user agents.
- `ApiRequestLog.ClientIpHash` is a SHA-256 of the IP with a server-side salt
  (`Telemetry:IpHashSalt`, required in production). Without a salt, an IP hash is
  trivially reversible over the IPv4 space.
- Data subject deletion (`DELETE /api/v1/users/me`) must not orphan telemetry: the
  rows are retained because they are aggregate and non-identifying, but the
  `UserId` is replaced with the tombstone value `00000000-0000-0000-0000-000000000000`
  in `AuditLogEntries`, `ApiRequestLogs`, and `ApiRequestMetrics`, and the user's
  relationship to the org is severed. This is a deliberate, documented choice: it
  preserves billing integrity while removing the identifier.

---

## 9. Testing strategy

### 9.1 Layers, matching what already exists

`docs/tests/README.md` documents the four existing layers. This plan adds to each
and adds one new layer.

| Layer | Stack | Location | Existing gate | New gate |
| --- | --- | --- | --- | --- |
| Backend unit | xUnit + Moq | `Aveline.Api.Tests/` | — | — |
| Backend integration | xUnit + `WebApplicationFactory<Program>` + in-memory EF | `Aveline.Api.Tests/` | — | — |
| Backend Postgres | xUnit + `Testcontainers.PostgreSql` | `Aveline.Api.Tests/` | two pgvector classes | all constraint/concurrency/partition tests |
| Agent service | pytest + respx + fakeredis | `agnet-service/tests/` | 90 % coverage | new telemetry cases |
| **Contract** | **NEW** — OpenAPI schema assertions | `Aveline.Api.Tests/Contract/` | — | must pass |
| Load | **NEW** — NBomber or k6 script | `tests/load/` | — | report only in Phase 1–3; gate in Phase 4 |

### 9.2 What must be tested, by feature area

Concrete test classes this plan expects. The names follow the existing
`{Subject}Tests` convention.

**Billing and pricing**

| Class | Level | Key cases |
| --- | --- | --- |
| `BlossomCalculatorTests` | unit | every rounding mode; minimum clamp; zero tokens; large values; 4-dp storage |
| `PricingRuleResolutionTests` | unit | precedence; window inclusivity; fallback; ties |
| `PricingRuleEndpointTests` | integration | overlap → 409; activation trims predecessor; past date without permission → 403 |
| `PricingRecomputeTests` | integration | recompute writes corrections, never mutates `AiUsageRecord` |
| `PricingRuleConstraintTests` | Postgres | the GiST exclusion constraint actually rejects overlap |
| `BlossomLedgerTests` | unit | sign per `EntryType`; cap; reason length |
| `BlossomServiceConcurrencyTests` | Postgres | 20 parallel credits produce the arithmetic sum (targets **D-3**) |
| `BlossomIdempotencyTests` | integration | replay returns the identical body; different body → 409 |
| `BlossomDowngradeGuardTests` | integration | each of the three limits produces a `violations` entry |
| `UsageAccountBalanceConstraintTests` | Postgres | the `CHECK` rejects a desynchronised projection |
| `BillingMigrationBackfillTests` | Postgres | M3 leaves `BlossomRemaining` unchanged |
| `EntitlementResolverTests` | unit | tier lookup; override precedence; effective dating; the value matches the old hardcoded maps (targets **D-1**, **D-2**, **D-12**) |

**Users, orgs, API keys**

| Class | Level | Key cases |
| --- | --- | --- |
| `ApiKeyServiceTests` | unit | format; hash; constant-time compare; scope validation rejects `pricing:*` |
| `ApiKeyAuthenticationTests` | integration | end-to-end principal; cross-org → 404; revoked key fails within TTL; suspended org → 401 |
| `ApiKeyEntitlementTests` | integration | creation on a non-Rose plan → 403 |
| `MembershipRoleChangeTests` | integration | self-change → 409; last owner → 409; owner grant by non-owner → 403 |
| `UserAccountStateTests` | integration | transition matrix; cache invalidation |
| `ClerkWebhookTests` | integration | signature verification; idempotent; syncs the read model |
| `OrganizationSettingsTests` | integration | entitlement-gated fields; slug collision on rename → 409 |

**Agentic statistics**

| Class | Level | Key cases |
| --- | --- | --- |
| `AgentRunIngestTests` | integration | idempotent on `(org, workflowId)`; terminal conflict → 409; step cap → 413 |
| `AgentRunUnattributedTests` | integration | a run with no org is stored and excluded from org scope (targets **D-7**) |
| `AgentStatisticsQueryTests` | integration | each dimension; percentile `null` below the sample floor |
| `AgentRunPrivacyTests` | integration | the ingest DTO contains no forbidden fields; hashes are one-way |
| `AgentTelemetryPythonTests` | pytest | every concierge node emits a step; the visual agent sends the real thread id (targets **D-4**); `cached_tokens`/`cost` are populated when available (targets **D-8**) |

**API consumption statistics**

| Class | Level | Key cases |
| --- | --- | --- |
| `ApiTelemetryMiddlewareTests` | unit | route template for every registered route; excluded paths; invalid `X-Request-Id` → 400 |
| `ApiTelemetryWriterTests` | integration | batch upsert; buffer-full drop counter; restart loss bounds |
| `ApiMetricRepositoryTests` | Postgres | the `NULLS NOT DISTINCT` upsert increments correctly; partition switching |
| `QuotaServiceTests` | unit | window arithmetic incl. month-end; warning threshold fires once |
| `ApiStatisticsQueryTests` | integration | each dimension; rollup matches an exact synthetic count of 1 000 |
| `PercentileCalculatorTests` | unit | interpolation against known distributions |

**System statistics**

| Class | Level | Key cases |
| --- | --- | --- |
| `HealthCheckTests` | integration | each dependency down → the specified status |
| `MetricsEndpointAuthTests` | integration | anonymous → 401 |
| `AlertEvaluationTests` | integration | fire, cooldown aggregation, auto-resolve after 3 OKs, notification created |
| `SystemMetricCollectorTests` | unit | unknown metric is omitted, not zero |
| `SystemStatisticsEndpointsTests` | integration | every response matches the OpenAPI schema |

**Cross-cutting**

| Class | Level | Key cases |
| --- | --- | --- |
| `CorrelationIdMiddlewareTests` | unit | generate, echo, reject invalid, propagate to the agent client |
| `IdempotencyFilterTests` | integration | replay, body-mismatch conflict, expiry |
| `AuditRedactionTests` | unit | a secret placed in a payload never reaches the table |
| `TenantIsolationTests` | integration | **a matrix over every new tenant endpoint**: a valid JWT for org A returns 404/403 (never 200) for org B's resources |
| `PermissionsCatalogTests` | unit | every permission in `Permissions.All` has a registered policy and at least one role grant |
| `ContractSchemaTests` | contract | every documented endpoint exists; every response matches the OpenAPI schema; no undocumented endpoint is registered |

`TenantIsolationTests` is the single most valuable test in this plan. There is no
EF global tenant filter, so isolation depends entirely on each repository
remembering to filter — and that is exactly the class of bug that is invisible in
review and catastrophic in production.

### 9.3 Coverage gates

| Layer | Current | Proposed |
| --- | --- | --- |
| Backend | line ≥ 30 % (CI) | **Raise to 60 % for `Modules/Billing/**`, `Modules/Statistics/**`, `Modules/Audit/**`, and `Modules/ApiAccess/**`** via a per-assembly threshold, leaving the repo-wide gate at 30 % so existing code does not have to be retrofitted |
| Agent service | 90 % | unchanged; new telemetry code must meet it |
| Web / Mobile | unchanged | unchanged |

A repo-wide jump to 60 % is not proposed because it would force unrelated work on
the three vertical slices; a per-module gate targets exactly the new, high-risk
code.

### 9.4 Test data and fixtures

New fixtures required:

| Fixture | Purpose |
| --- | --- |
| `PostgresFixture` | One Testcontainers instance shared across the Postgres test collection, with `btree_gist` and `pgvector` extensions pre-created and migrations applied once. The existing two Testcontainers classes each start their own container; consolidating is a prerequisite for keeping the new suite's runtime acceptable |
| `TelemetryFixture` | Boots the API with telemetry enabled against a real Postgres so rollup tests exercise real upserts |
| `AgentRunBuilder` | Fluent builder for realistic run/step payloads |
| `SeededOrgFixture` | An org with membership, usage account, ledger entries, and API keys for query tests |
| `LoadBaseline.json` | The 1 000-request synthetic dataset whose rollup totals must match exactly |

### 9.5 Load tests

| Scenario | Target | Gate |
| --- | --- | --- |
| Sustained API traffic | 5 000 req/s for 60 s | p99 latency increase from telemetry ≤ 1 ms (FR-6.3); no sample-buffer saturation |
| Blossom write contention | 1 000 sequential + 50 concurrent adjustments on one org | zero drift between ledger sum and projection |
| Workflow ingest | 200 concurrent runs × 50 steps | within the ingest budget; no deadlocks |
| Statistics read | 100 concurrent dashboard queries over 90 days | p95 < 500 ms |
| Migration | M3 against 1 M `UsageAccounts` rows | completes in < 5 min |

---

## 10. Deployment, rollout, and migration

### 10.1 Environments

Existing: local `docker-compose.yml` (Postgres + Redis + agent + otel-collector),
CI (GitHub Actions `ci.yml`), and a cloud target per `ADR-006`. This plan adds no
new infrastructure beyond what `docker-compose.yml` already runs.

### 10.2 Required new configuration keys

| Key | Default | Purpose |
| --- | --- | --- |
| `Telemetry:Enabled` | `true` | Kill switch for request telemetry |
| `Telemetry:ExcludedPaths` | `["/health","/health/live","/health/ready","/openapi"]` | |
| `Telemetry:SuccessSampleRate` | `0.1` | Raw-log sampling for 2xx |
| `Telemetry:SlowRequestMs` | `1000` | Always-log threshold |
| `Telemetry:BufferCapacity` | `10000` | |
| `Telemetry:MaxWindowDays` | `92` | |
| `Telemetry:MinSampleForPercentile` | `20` | |
| `Telemetry:QuotaWarningPercent` | `80` | |
| `Telemetry:IpHashSalt` | *(required in prod)* | IP hashing salt |
| `Billing:MaxAdjustmentBlossoms` | `10000` | |
| `Billing:IdempotencyRetentionHours` | `24` | |
| `Billing:LowBalanceThresholdPercent` | `20` | |
| `Billing:AllowCrossPeriodTopUps` | `false` | |
| `Billing:AbnormalCostThresholdUsd` | `1.00` | Existing key, already read at `UsageTrackerService.cs:145-146` |
| `Pricing:RuleCacheTtlSeconds` | `5` | L1 TTL |
| `AgentStats:MaxStepsPerRun` | `200` | |
| `AgentStats:PausedRunTimeoutHours` | `72` | |
| `Observability:InstanceId` | hostname | |
| `Observability:SlowQueryMs` | `500` | |
| `Observability:RedisIsCritical` | `false` | |
| `Observability:AgentIsCritical` | `true` | |
| `Observability:AlertEvaluationSeconds` | `60` | |
| `Observability:AutoResolveConsecutiveOk` | `3` | |
| `Observability:OtlpEndpoint` | `""` | Empty disables OTLP, matching the Python service's behaviour |
| `Metrics:ScrapeToken` | unset | Enables `/metrics` token auth |
| `ApiKeys:SchemeEnabled` | `true` | |
| `Jobs:DistributedLockSeconds` | `300` | |
| `Clerk:WebhookSecret` | *(required if webhooks enabled)* | |

### 10.3 Rollout order

Each phase is independently deployable and independently revertible. A phase is
"done" only when its migrations are applied **and** its observability is live.

| Phase | Content | Duration | Depends on | Rollback |
| --- | --- | --- | --- | --- |
| **0 — Foundations** | `CorrelationIdMiddleware`; `AuditLogEntries` (M1); `IAuditService`; permissions catalog extension; policy constants; `DistributedJobLock`; OTel registration + `/metrics`; `/health/live` + `/health/ready` split; fix D-9 doc drift; fix the `"org:principal"` literal | 4–5 d | — | Drop the middleware and M1; no data loss |
| **1 — Pricing** | M2; `BlossomConversionRule`/`BlossomPriceEntry`; `PricingService`; `BlossomCalculator`; pricing snapshot columns decision deferred to Phase 2; admin pricing endpoints; `PricingRuleCacheWarmer` | 4–5 d | Phase 0 | M2 `Down`; the fallback formula keeps working because `PricingService` falls back to 1000/ceiling/0.1 |
| **2 — Ledger** | M3; `BlossomLedgerEntries`; `IdempotencyRecords`; `BlossomService`; `IEntitlementResolver` + M4 entitlements; fix **D-1, D-2, D-3, D-12**; org and admin Blossom endpoints; plan-change endpoints; `BlossomExpiryJob`, `BillingPeriodRolloverJob`, `IdempotencyRecordCleanupJob`; **AiUsageRecord pricing snapshot columns** | 6–8 d | Phase 1 | M3 `Down` drops the new tables and columns; the backfill is not reversible but `BlossomRemaining` was never altered |
| **3 — API access and users** | M5; API keys; `ApiKey` scheme; entitlement gating; user profile/admin endpoints; org settings; member list and role change; Clerk webhooks | 5–6 d | Phase 2 | M5 `Down`; disable `ApiKeys:SchemeEnabled` |
| **4 — Agentic statistics** | M6; run/step ingest endpoint; Python instrumentation for **G-1…G-14**; fix **D-4, D-5, D-6, D-7, D-8**; agent statistics endpoints; `AgentStatsRollupJob`, retention, `StaleAgentRunJob` | 7–9 d | Phase 2 (needs `BlossomConversionRule` and the run↔usage link) | M6 `Down`; `/internal/agent-runs` is additive, so the old path still works |
| **5 — API consumption statistics** | M7; telemetry middleware, channel, writer; metrics/quota tables; API statistics endpoints; rollups, partitions, retention; quota enforcement | 8–10 d | Phase 0 (correlation ids) and Phase 3 (API keys) | M7 `Down` is destructive for raw logs; the rollup tables drop cleanly |
| **6 — System statistics and alerts** | M8; health checks and readiness; collectors; alert rules and evaluation; system statistics endpoints; seeded alert rules | 5–6 d | Phase 0 (OTel) and Phase 5 (API error rate) | M8 `Down`; disable alerts by setting all rules `IsEnabled = false` |

Total: **39–49 developer-days**. With three students working in parallel on
independent slices, wall-clock is roughly 5–7 weeks if Phases 0–1 are done
together and then Phases 2–4 parallelise across slices. See the risk note in
§11 about Phase 4's cross-team dependency.

### 10.4 Deployment mechanics

Deploy order per phase: **migrate → deploy → verify → enable**.

1. Run migrations from CI (a documented `dotnet ef database update` step), not
   from application startup. Startup migration already exists for Development only
   (`Program.cs:118-126`) and must **not** be enabled in Production: it races when
   more than one instance starts.
2. Deploy the new build with any new feature flag off.
3. Verify `/health/ready` reports the new dependencies, and that the new endpoints
   return 401 anonymously.
4. Enable the flag and watch the seeded alert rules for 30 minutes.

### 10.5 Backfill and data-migration operations

| Operation | When | On failure |
| --- | --- | --- |
| M3 ledger backfill | During the M3 migration | Aborts the migration transaction; the database is unchanged |
| Pricing snapshot backfill | Phase 2, as a job, not a migration | `AiUsageRecord.PricingRuleId` stays `NULL`; the read path already handles `NULL` as "assume 1000" |
| `PlanEntitlements` seed | M4, as data in the migration | Entitlement resolution falls back to the hardcoded maps until the seed lands — so the seed must ship in the same migration, not later |
| `ApiRequestLogs` initial partitions | M7 raw-SQL step | Telemetry writes to the rollup only; the raw path degrades, the aggregate path does not |
| Anomaly/pricing recompute | Phase 2, on demand | Writes nothing; the request is rejected and logged |

### 10.6 Feature flags

All flags live in configuration, not in a database table, because there is no
feature-flag service. Each flag defaults to the **safe** value.

| Flag | Phase | Default |
| --- | --- | --- |
| `Telemetry:Enabled` | 5 | `true` (safe: it is best-effort and cannot fail a request) |
| `ApiKeys:SchemeEnabled` | 3 | `true` |
| `Billing:EnforcementEnabled` | — | `false` and **not implemented**, per `ADR-010` |
| `Observability:RedisIsCritical` | 6 | `false` |
| `Observability:AgentIsCritical` | 6 | `true` |
| `Pricing:UseLegacyFormula` | 1 | `true` on first deploy, flipped to `false` after the rule cache is verified warm |

`Pricing:UseLegacyFormula` is the escape hatch that makes Phase 1 safe: if rule
resolution misbehaves, one configuration change restores the exact current
behaviour.

---

## 11. Risks and dependencies

### 11.1 Risks

| # | Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- | --- |
| R-1 | **The agent service work (Phase 4) is a hard dependency on a different vertical slice.** Phases 4's statistics are worthless without 14 instrumentation changes in Python (G-1…G-14), which is Slice-1/2 work, not billing work | High | High | Split Phase 4: the .NET ingest and storage ship first with `dataQuality` flags set false; the Python instrumentation is scheduled with the owning slice and tracked as explicit sub-tasks. The API is honest about what it does not know |
| R-2 | **The `btree_gist` extension may not be installable** on the managed Postgres provider | Medium | Medium | Ship M2 with a plain unique index and an application-level overlap check as the fallback, and raise a Critical alert if an overlap is ever detected. Documented in [OQ-8](assumptions-and-open-questions.md). **Verify before Phase 1 starts** |
| R-3 | **Per-request telemetry degrades p99 latency** | Medium | High | The middleware only stamps a timestamp and enqueues a struct — no allocation of a sample object unless the channel accepts it. The 1 ms p99 gate is a CI load-test assertion, not an aspiration |
| R-4 | **Tenant isolation regression.** No EF global filter exists, so a forgotten `.Where(OrganizationId == ...)` leaks data across boutiques | Medium | Critical | `TenantIsolationTests` is a required test that iterates every new tenant endpoint with a token for the wrong org. This is the single highest-value control in the plan |
| R-5 | **Scope creep into enforcement and payments.** The requirements mention upgrade/downgrade and billing, and it is tempting to also implement blocking and Stripe | High | Medium | `ADR-010` §Decision 6 is explicitly preserved. `OrganizationSubscriptions` stores provider fields but no provider client is written |
| R-6 | **Migration M3 on a live database.** It adds a `CHECK` constraint and backfills | Low | High | The backfill is written to leave `BlossomRemaining` numerically unchanged, asserted by a pre/post test. The `CHECK` is added only after the backfill, in the same transaction |
| R-7 | **The GiST exclusion constraint plus the unique index may reject legitimate data** — e.g. two `Draft` rules for the same scope being prepared concurrently | Medium | Low | Drafts are excluded from the exclusion constraint's predicate; only `Draft` and `Active` are covered, so two concurrent drafts still collide. Fix: allow only one `Draft` per scope via a partial unique index, and require explicit cancellation before creating another |
| R-8 | **Percentile accuracy is limited by bucket granularity** | Certain | Low | Stated in the response (`precision: "bucket-interpolated"`) and in the statistics catalog. Acceptable for a dashboard; not suitable for an SLA report, and no SLA report is in scope |
| R-9 | **`d4a5325e` and `64ba46e2` research workstreams failed** during this investigation, so the org/user endpoint inventory and the build/deployment conventions rest on direct reading rather than a second independent pass | Certain | Low | Both were recovered by direct reading of `OrganizationEndpoints.cs`, the `.csproj` files, `appsettings.json`, `.github/workflows/ci.yml`, and `dotnet-tools.json`. Anything not directly read is marked `INFERRED` |
| R-10 | **`docs/api/openapi.yaml` conflicts with an existing repo rule** (`docs/OpenApi/README.md:27` says never hand-edit the exported spec) | Certain | Medium | The reconciliation procedure is specified in [api/README.md §1.3](../api/README.md): the hand-authored spec is the source of truth for planned endpoints, and generated output wins for implemented ones. A CI check should diff them once the endpoints exist |

### 11.2 Dependencies

| Dependency | Owner | Needed by | Status |
| --- | --- | --- | --- |
| Python run/step instrumentation (G-1…G-14) | Slice 1 / Slice 2 | Phase 4 | Not started |
| `AsyncPostgresSaver` actually wired into `graph.compile(...)` (fixes D-5) | Slice 1 / Slice 2 | Phase 4 (S-21) | Not started; the defect is confirmed |
| Managed Postgres with `btree_gist` and `pgcrypto` | Infrastructure | Phase 1 | Unverified |
| Clerk Backend API access for session listing and role sync | Infrastructure | Phase 3 | `ClerkAdminClient` exists (`Infrastructure/Integrations/ClerkAdminClient.cs`) |
| OTel packages for .NET 10 | This plan | Phase 0 | Package versions must be confirmed, see OQ-9 |
| `otel-collector-config.yaml` extended to accept .NET OTLP | Infrastructure | Phase 0 | Config file exists; the .NET service is not yet a sender |
| Frontend consumption of the new statistics endpoints | React/Flutter owners | After Phase 6 | Out of scope for this plan |

### 11.3 Open questions blocking implementation

Eleven questions, listed in full in
[assumptions-and-open-questions.md](assumptions-and-open-questions.md). Three of
them block a specific phase:

| Question | Blocks |
| --- | --- |
| **OQ-1** — does "Blossom-to-token price" mean the normalisation rate, the LKR price, or both? | Phase 1 scope |
| **OQ-8** — is `btree_gist` installable on the target database? | Phase 1 mechanism |
| **OQ-7** — should exceeding a plan quota return 402 or 403? | Phase 2 response contract |

---

## 12. Phased milestones — acceptance criteria

A milestone is complete only when every criterion is met.

### M0 — Foundations
- `X-Request-Id` is generated, validated, echoed, logged, and propagated to the
  agent service.
- `AuditLogEntries` exists and every org/user configuration change writes a row.
- The permission catalog has 22 entries, every one has a registered policy, and
  `PermissionsCatalogTests` passes.
- `/health/live` and `/health/ready` return the documented JSON; `/metrics`
  requires credentials.
- `docs/architecture/authorization.md` is regenerated and matches `Permissions.cs`.
- The `"org:principal"` literal is replaced with `Roles.BoutiqueOwner`.

### M1 — Pricing
- An admin can create, activate, and cancel a conversion rule through the API.
- Overlapping rules are rejected by the database, proven by a constraint test.
- `BlossomCalculatorTests` covers every rounding mode and the minimum clamp.
- A rule change does not alter any existing `AiUsageRecord.BlossomUnits`.
- `Pricing:UseLegacyFormula = true` reproduces today's exact numbers.

### M2 — Ledger
- Credit, debit, revoke, upgrade, and downgrade all work, are idempotent, and are
  audited.
- 20 concurrent credits produce the exact arithmetic sum.
- `BlossomRemaining` equals the ledger-derived value for every org.
- The M3 backfill is proven to leave `BlossomRemaining` unchanged.
- The three hardcoded plan-limit maps are deleted; `IEntitlementResolver` is the
  only source.

### M3 — API access and users
- An API key authenticates, is scoped, is rate-limited, and is revocable within
  60 seconds.
- A key for org A returns 404 for org B's resources on every tenant route.
- An owner can list members and change a role, with all guards enforced.
- `TenantIsolationTests` passes across every tenant endpoint in the system.

### M4 — Agentic statistics
- One concierge workflow produces exactly one run and one step per graph node.
- Every statistic in [statistics-catalog.md §5](statistics-catalog.md) is
  returned, with `dataQuality` flags accurately reflecting instrumentation state.
- The visual agent's usage rows carry the real workflow id (D-4 fixed).
- Streaming runs appear in statistics (D-6 fixed).
- Runs with no resolvable org are recorded and flagged (D-7 fixed).

### M5 — API consumption statistics
- A request appears in the rollup within 5 seconds with the correct org, user,
  and key.
- A synthetic run of exactly 1 000 requests produces rollup totals of exactly
  1 000.
- p99 latency increase from telemetry is ≤ 1 ms at 5 000 req/s.
- The raw log is partitioned, sampled, and pruned at 7 days.
- Quotas enforce at the period boundary with a documented 429 body.

### M6 — System statistics and alerts
- `/health/ready` correctly reports each dependency being down, per §8.7 of the
  requirements.
- Every metric in [statistics-catalog.md §7](statistics-catalog.md) is collected.
- Every seeded alert rule fires, aggregates under cooldown, auto-resolves, and
  creates a notification.
- `blossom.ledger.drift` demonstrably fires when the projection is desynchronised
  in a test.
