# Aveline Backend — Requirements Specification

**Status:** Proposed — for review before implementation
**Scope:** Backend only (`Aveline.Api`, `agnet-service`). No frontend, no screens, no UX flows.
**Baseline commit:** `902f27f` (merge of `integration/slice-2-to-slice-1`)
**Companion documents:**
[Domain Model](domain-model.md) ·
[Statistics Catalog](statistics-catalog.md) ·
[Implementation Plan](implementation-plan.md) ·
[Assumptions & Open Questions](assumptions-and-open-questions.md) ·
[API Catalog](../api/README.md) ·
[OpenAPI 3.0.3](../api/openapi.yaml)

---

## 0. How this document was produced

Everything here was derived by reading the repository, not from memory. The
load-bearing sources are:

| Area | Primary evidence |
| --- | --- |
| Billing / Blossom | `Aveline.Api/Modules/Billing/**`, `Aveline.Api/Infrastructure/Data/Configurations/BillingConfigurations.cs` |
| Pricing intent | `docs/architecture/pricing_plan.md`, `docs/ADR/ADR-010-usage-tracking-architecture.md`, `pricing_implementation_plan.ignore.md` |
| Auth / authz | `Aveline.Api/Configurations/AuthorizationConfiguration.cs`, `Aveline.Api/Authorization/**`, `docs/architecture/authorization.md` |
| Org / user / invitations | `Aveline.Api/Modules/Organizations/**`, `Aveline.Api/Modules/Shared/**`, `Aveline.Api/Endpoints/OrganizationEndpoints.cs` |
| Agent service | `agnet-service/app/**` |
| Conventions | `docs/tests/README.md`, `.github/`, `Aveline.Api/Program.cs` |

Where a claim is an inference rather than a verified reading, it is marked
`INFERRED`. Where a required capability does not exist, it is marked
`NOT FOUND — gap`.

---

## 1. Current state and gap analysis

### 1.1 What exists today (verified)

**Blossom usage recording** — two tables and one write path:

- `AiUsageRecords` — append-only, one row per completed agent workflow
  (`Aveline.Api/Modules/Billing/Models/AiUsageRecord.cs:16-64`).
- `UsageAccounts` — one mutable ledger row per organisation per calendar month,
  unique on `(OrganizationId, PeriodStart)`
  (`Aveline.Api/Infrastructure/Data/Configurations/BillingConfigurations.cs:86-88`).
- `UsageTrackerService.RecordWorkflowUsageAsync` calculates
  `blossom_units = ceil((input + output + cached) / 1000, 1 dp)`, minimum `0.1`,
  then inserts the record and increments the ledger inside one transaction
  (`Aveline.Api/Modules/Billing/Services/UsageTrackerService.cs:37-72`,
  `:110-117`).
- Ingest endpoint `POST /internal/usage/record` (internal-token only), read
  endpoints `GET /internal/usage/summary/{orgId}` and
  `GET /internal/usage/records/{orgId}`, and one boutique-facing endpoint
  `GET /api/v1/orgs/{organizationId}/usage`
  (`Aveline.Api/Modules/Billing/Endpoints/UsageEndpoints.cs:18-41`,
  `Aveline.Api/Modules/Billing/Endpoints/OrgUsageEndpoints.cs:19-26`).

**Users, organisations, invitations** — a working Clerk-backed model:
`Organizations`, `OrganizationMemberships`, `OrganizationInvitations`, `Users`,
with role-based and organisation-scoped authorization enforced by
`OrganizationScopeAuthorizationHandler` and a middleware account-state gate
(`Aveline.Api/Authorization/OrganizationScopeAuthorizationHandler.cs:38-74`,
`Aveline.Api/Common/Middleware/OnboardingMiddleware.cs:74-111`).

### 1.2 Gap analysis per required feature area

| # | Feature area | What exists | Gap |
| --- | --- | --- | --- |
| 1 | Blossom-to-token price adjustment | Formula hardcoded as a private static; `UnitsPerBlossom` fixed at 1000; minimum fixed at 0.1 (`UsageTrackerService.cs:110-117`) | **No rate entity, no history, no effective dating, no rounding configuration, no audit, no permissions, no endpoints.** `NOT FOUND — gap` |
| 2 | Org Blossom account operations | Ledger row is incremented only by consumption; `MonthlyBlossomLimit` is a plain column (`UsageAccount.cs:51-61`) | **No add/deduct/remove, no adjustment ledger, no idempotency, no concurrency control, no plan-change endpoint.** `NOT FOUND — gap`. Two live defects found, see §3.2.4 |
| 3 | User management | `GET /users/me`, `POST /users/onboarding`, invitations, membership suspend/activate/remove (`Aveline.Api/Endpoints/UserEndpoints.cs`, `OrganizationEndpoints.cs`) | **No user list/CRUD, no role change, no soft delete endpoint, no profile update, no session listing, no API keys.** API keys: `NOT FOUND — gap` |
| 4 | Organization management | Create, read, by-slug, my-orgs, invitations, integrations, health (`Aveline.Api/Endpoints/OrganizationEndpoints.cs`, `IntegrationEndpoints.cs`) | **No update/settings PATCH, no plan change, no subscription/billing, no Blossom balance management, no API key management, no org list for team admins.** `NOT FOUND — gap` |
| 5 | Agentic statistics | One token/cost row per workflow, with `WorkflowId`, `Provider`, `Model` (`AiUsageRecord.cs`) | **No agent/node runs, no success/failure, no latency, no tool calls, no retries, no run status.** `NOT FOUND — gap`. Instrumentation gaps enumerated in §6.2 |
| 6 | API consumption statistics | None. No request-logging middleware exists (`Program.cs:82-88`); no correlation ID; no quota/rate accounting per key | **Entire feature absent.** `NOT FOUND — gap` |
| 7 | System statistics | `GET /health` (Redis check only), `EventBusMetrics` counters logged as JSON by `EventingMetricsExporter` (`Aveline.Api/Infrastructure/Eventing/EventbusMetrics.cs`, `Configurations/EventingConfiguration.cs:22-33`) | **No readiness/liveness split, no resource metrics, no queue-depth, no DB/cache metrics, no error-rate API, no alerts.** `NOT FOUND — gap` |

### 1.3 Defects found that the plan must fix

These are verified behaviours that will corrupt Blossom accounting if left in
place. Each is addressed by a requirement below.

| ID | Defect | Evidence |
| --- | --- | --- |
| **D-1** | **Plan limit is ignored on lazy ledger creation.** `GetOrCreateCurrentAccountAsync` always uses the *Seed* limit, regardless of the org's tier. | `Aveline.Api/Modules/Billing/Services/UsageTrackerService.cs:34,80` — `DefaultBlossomLimits[DefaultNewAccountTier]` where `DefaultNewAccountTier = PlanTier.Seed` |
| **D-2** | **Same defect hardcoded in the repository.** When the ledger row is auto-created during a usage write it is seeded at 150 Blossoms with a comment admitting it. | `Aveline.Api/Modules/Billing/Repositories/UsageRepository.cs:44-53` |
| **D-3** | **Lost-update race on the Blossom balance.** The balance update is a read-modify-write with no concurrency token and no row lock; concurrent workflows for one org will lose increments. No `IsConcurrencyToken`, `RowVersion`, `xmin`, or `FromSql` usage exists anywhere in the API. | `Aveline.Api/Modules/Billing/Repositories/UsageRepository.cs:36-61`; grep for concurrency tokens returns zero matches |
| **D-4** | **The `visual_insight` usage report is mis-attributed.** It hardcodes `workflow_id="visual_insight"`, `provider="openai"`, `model="visual-llm"`, and passes `customer_id` as `request_id`. Those rows are unattributable and unusable for statistics. | `agnet-service/app/agents/visual_insight/nodes.py:371-388` |
| **D-5** | **The LangGraph checkpointer is not effective.** `build_concierge_graph` documents itself as "(no checkpointer)" and returns `graph.compile()` with no checkpointer; the saver is instead created at the call site and passed as a call-time kwarg, which `langgraph 1.2.11` silently discards. Persisted workflow state — and therefore cross-approval pause/resume — does not survive. | Structural defect **confirmed by direct read**: `agnet-service/app/workflows/concierge_workflow.py:348-367` (`"""... (no checkpointer)."""`, `return graph.compile()`) and `:408-431` (`checkpointer=checkpointer` passed to `ainvoke`/`run_graph_with_states`). The claim that the kwarg is *discarded* rather than honoured was confirmed by an in-memory runtime repro in this investigation's agent-service workstream, not re-run by the author |
| **D-6** | **Streaming runs emit no usage at all.** `POST /agents/query/stream` has no thread config, no checkpointer, and no usage report. | `agnet-service/app/api/agents.py:227-244` |
| **D-7** | **Usage reporting is skipped when the org id is absent or not a UUID**, so those workflows produce zero telemetry. | `agnet-service/app/api/agents.py:120,139-149` |
| **D-8** | **`cached_tokens` and `actual_cost_usd` are never sent by any caller**, so both are permanently zero on the wire and the cost-anomaly detector can never fire. | `agnet-service/app/services/usage_reporter.py:19-20`; no caller supplies them |
| **D-9** | **Doc/code drift in the permission catalog.** `docs/architecture/authorization.md:51-62` omits `conversations:view` from two role rows and from the catalog list; the code grants it. `Aveline.Api/Common/Exceptions/README.md` documents a `GlobalExceptionHandler.cs` that does not exist. `OnboardingService.cs:266` writes the literal `"org:principal"`, a value absent from `Aveline.Api/Authorization/Roles.cs` and therefore granted nothing. | Three separate files |
| **D-10** | **Inconsistent error envelope.** `/internal/*` endpoints return `{ "error": ... }` while every other endpoint returns `{ "message": ... }`; 401/403 return empty bodies. | `Aveline.Api/Modules/Billing/Endpoints/UsageEndpoints.cs:58` vs `Aveline.Api/Endpoints/OrganizationEndpoints.cs:49` |
| **D-11** | **No request correlation ID exists.** `HttpContext.TraceIdentifier` is never read and no correlation header is emitted or accepted, so API-consumption statistics cannot correlate a request to a workflow. | grep across `Aveline.Api` |
| **D-12** | **Three copies of the plan-limit table** are maintained independently: `UsageTrackerService.cs:24-31`, `OnboardingService.cs:19-25`, and the tier gating in `OnboardingService.SaveAiCustomizationAsync`. They can drift. | Three files |

---

## 2. Cross-cutting conventions this plan obeys

Every new endpoint and module must follow the conventions already in the
codebase. These are **confirmed by reading the code**, not proposed.

| Concern | Existing convention | Evidence |
| --- | --- | --- |
| Module shape | static `Add{Name}Module(IServiceCollection)` + `Map{Name}Endpoints(IEndpointRouteBuilder)`, wired in `Program.cs` | `Modules/Billing/BillingModule.cs:12-24`; `Program.cs:42-43,110-112` |
| Endpoint shape | static `XEndpoints` class, `MapGroup`, `.WithTags/.WithName/.WithSummary/.Produces<T>/.Produces(status)`, `Results.*` | `Endpoints/ConversationEndpoints.cs:20-74` |
| Route prefix | user-facing routes under the shared `/api/v1` group; internal routes at root `/internal/*`, unversioned; no versioning library installed | `Program.cs:95,110-112` |
| Authorization | named policy constants, or the permission string itself as the policy name. **There is no `RequirePermission` helper — do not invent one.** | `Configurations/AuthorizationConfiguration.cs:91-95`; `Endpoints/AuthPolicyDemoEndpoints.cs:29` |
| Tenant scoping | route **must** contain `{organizationId:guid}`; the handler reads only that route value; repositories must filter `OrganizationId` manually because **no EF global tenant filter exists** | `Authorization/OrganizationScopeAuthorizationHandler.cs:38-43`; `Common/MultiTenancy/ITenantEntity.cs:6-11` |
| DTO style | `sealed record` response DTOs with a static `From(entity)` mapper; never expose EF entities | `Modules/Conversations/DTOs/ConversationDtos.cs:6-28` |
| Pagination | `page` (default 1), `pageSize` (default 50), `Math.Max(page,1)`, `Math.Clamp(pageSize,1,200)`, envelope `*Page(Items, Total, Page, PageSize)` — the property is `Total`, not `totalCount` | `Endpoints/ConversationEndpoints.cs:80-88` |
| Errors | `Results.BadRequest/NotFound/Conflict(new { message = "..." })`; `Results.ValidationProblem` for field validation; empty `Results.Unauthorized()` / `Results.Forbid()` | `Endpoints/OrganizationEndpoints.cs:49,201,406`; `Endpoints/UserEndpoints.cs:53` |
| Rate limiting | custom `IRateLimiter.TryAllowAsync(scopeKey, limit, window, ct)` over `IDistributedCache`; limits from config, not hardcoded | `Infrastructure/RateLimiting/IRateLimiter.cs:6-12`; `appsettings.json:6-8` |
| Internal endpoints | `.RequireAuthorization(AuthorizationConfiguration.InternalServicePolicy)`; org id carried explicitly | `Modules/Billing/Endpoints/UsageEndpoints.cs:20` |
| Config | `configuration["Section:Key"]`, throw at startup for required values | `Configurations/AuthenticationConfiguration.cs:29-30` |
| Events | `IEventBus.PublishAsync(eventType, organizationId, payload, traceId, ct)`; event names in `dot.case` | `Infrastructure/Eventing/IEventBus.cs`; `Modules/Conversations/Services/ConversationEvents.cs` |

### 2.1 Proposed additions to the conventions (and why)

Two new conventions are required by these feature areas and have no precedent.
Both are justified because the existing pattern cannot express the requirement.

| Proposal | Why the existing pattern fails |
| --- | --- |
| **`RequireApiKey(scope)` authentication scheme** as a third scheme beside `Bearer` and `InternalToken` | Rose-tier "API Access" needs non-interactive credentials. There is currently **no API-key scheme, handler, or header** anywhere in the API. Adding a scheme (rather than a policy) is required because API keys are not JWTs and must be resolved per request into a principal. |
| **`Idempotency-Key` request-header processing** as a filter, with an `IdempotencyRecords` table | Blossom add/deduct/remove are money-shaped operations. The repo has *descriptive* idempotency in two services (`IAdminApprovalService.cs:15`, `IConversationRepository.cs:20,31`) but **no mechanism**. A database-backed replay store is the only way to make this correct across restarts and instances. |

---

## 3. Feature area 1 — Blossom-to-token price adjustment

### 3.1 Interpretation and scope

"Blossom-to-token price adjustment" is ambiguous between two conversions. Both
are required by the pricing model, and this specification covers both, with the
first as the primary:

- **(A) Token → Blossom normalisation rate.** How many normalised AI units buy
  one Blossom. Today: `1 Blossom = 1000 units`, hardcoded. This is what
  `ADR-010` calls the "initial normalisation" and what must become adjustable.
- **(B) Blossom → LKR commercial price.** What one Blossom costs a customer.
  Today: implied only by the plan table in `docs/architecture/pricing_plan.md`
  (e.g. Bloom: LKR 3,500 for 750 Blossoms). This must become an effective-dated
  price book.

See [Open Question OQ-1](assumptions-and-open-questions.md).

### 3.2 Functional requirements

| ID | Requirement |
| --- | --- |
| FR-1.1 | The normalisation rate must be data, not code. An administrator must be able to create a new rate that takes effect from a chosen UTC timestamp without a deployment. |
| FR-1.2 | A rate may be scoped globally, per provider, or per provider+model. Resolution precedence is provider+model > provider > global. |
| FR-1.3 | Every rate change must be recorded with the actor, the reason, and the before/after values. |
| FR-1.4 | The rate applied to a usage record must be **snapshotted onto that record** so historical Blossom values remain reproducible even after the rate changes. |
| FR-1.5 | Effective windows for the same scope must not overlap. Overlap is a validation error, not a runtime precedence decision. |
| FR-1.6 | Rounding mode and decimal places must be configurable per rule, defaulting to the current behaviour (ceiling to 1 dp). |
| FR-1.7 | A minimum charge per workflow must be configurable per rule, defaulting to the current behaviour (0.1 Blossom). |
| FR-1.8 | A rate must be immutable once it has priced at least one usage record. Corrections are made by superseding, never by editing. |
| FR-1.9 | A backdated rate (effective date in the past) must require an elevated permission and must produce a reconciliation report of already-priced records that would change. |
| FR-1.10 | The active rate for any (provider, model, timestamp) must be resolvable in O(1) from an in-process cache with a bounded staleness window. |
| FR-1.11 | The commercial Blossom→LKR price book must be effective-dated and scoped by plan tier, with optional per-organisation contract overrides. |
| FR-1.12 | Changing a rate must never retroactively alter a customer's already-reported Blossom balance unless an explicit recompute is requested and audited. |

### 3.3 Data model

Two new entities. Full columns in [domain-model.md §2](domain-model.md).

**`BlossomConversionRule`** (`BlossomConversionRules`)
`Id`, `ScopeKind` (`Global|Provider|ProviderModel`), `Provider?`, `Model?`,
`UnitsPerBlossom` (int, default 1000), `MinimumChargeBlossoms` (decimal(18,4),
default 0.1), `RoundingMode` (`Ceiling|HalfUp|Down|Up`, default `Ceiling`),
`RoundingDecimals` (smallint, default 1), `EffectiveFrom`, `EffectiveTo?`,
`Status` (`Draft|Active|Superseded|Cancelled`), `Version` (int, monotonic per
scope), `ChangeReason` (required), `CreatedByUserId`, `ApprovedByUserId?`,
`CreatedAt`, `UpdatedAt`, `ConcurrencyToken`.

**`BlossomPriceEntry`** (`BlossomPriceEntries`)
`Id`, `PlanTier?`, `OrganizationId?`, `SkuKind` (`PlanAllowance|TopUpPack|
OverageUsage`), `SkuCode?`, `BlossomQuantity` (decimal(18,4)),
`PriceLkr` (decimal(18,2)), `EffectiveFrom`, `EffectiveTo?`, `Status`,
`ChangeReason`, `CreatedByUserId`, `CreatedAt`, `UpdatedAt`.

### 3.4 Business rules and validation

| ID | Rule |
| --- | --- |
| BR-1.1 | `UnitsPerBlossom > 0`. `MinimumChargeBlossoms >= 0`. `RoundingDecimals` in [0, 6]. `RoundingMode = HalfUp` requires `RoundingDecimals >= 1`. |
| BR-1.2 | `EffectiveTo IS NULL OR EffectiveTo > EffectiveFrom`. |
| BR-1.3 | Non-overlap per scope, enforced in the database with a GiST exclusion constraint over `tstzrange(EffectiveFrom, EffectiveTo, '[)')`, requiring the `btree_gist` extension. This is enforced at the database level because an application-only check races under concurrent admin writes. |
| BR-1.4 | Scope consistency: `Global` ⇒ `Provider IS NULL AND Model IS NULL`; `Provider` ⇒ `Provider IS NOT NULL AND Model IS NULL`; `ProviderModel` ⇒ both set. |
| BR-1.5 | `ChangeReason` is required (min 10 characters) on create and on cancel. |
| BR-1.6 | A rule may be edited only while `Status = Draft`. It is normalised to `EffectiveFrom = max(requested, now)` at activation unless the actor holds `pricing:backdate`. |
| BR-1.7 | A rule is locked (`Status -> Superseded` on edit path) as soon as any `AiUsageRecord.PricingRuleId` references it. |
| BR-1.8 | Activation atomically trims the predecessor's `EffectiveTo` to the new rule's `EffectiveFrom` and sets the predecessor to `Superseded`, in one transaction. |
| BR-1.9 | **Resolution:** filter `Status = Active`, `EffectiveFrom <= t`, and (`EffectiveTo IS NULL OR EffectiveTo > t`); order by scope specificity (`ProviderModel`=3, `Provider`=2, `Global`=1) descending, then `EffectiveFrom` descending; take the first. If nothing matches, the hard-coded fallback `UnitsPerBlossom = 1000`, `Ceiling`, 1 dp, minimum 0.1 is used and a `pricing.rule.missing` warning is logged at Warning level. |
| BR-1.10 | **Rounding is applied once per workflow, to the workflow token total — never per LLM call.** Sum-then-round avoids rounding inflation and matches the existing implementation. |
| BR-1.11 | `NormalizedUnits = InputTokens + OutputTokens + CachedTokens` (integer). `BlossomUnits = max(round(NormalizedUnits / UnitsPerBlossom, RoundingDecimals), MinimumChargeBlossoms)`, stored at 4 dp. A workflow with zero tokens therefore still costs `MinimumChargeBlossoms`. This is existing behaviour and is preserved. |
| BR-1.12 | The pricing timestamp is the **API ingest time** (`DateTime.UtcNow` inside `UsageTrackerService`), not a caller-supplied timestamp. Callers cannot choose their price. |
| BR-1.13 | `BlossomUnits` continues to be computed server-side and must never be accepted from a caller. |

### 3.5 Permissions

Three new permissions, added to `Aveline.Api/Authorization/Permissions.cs`:

| Permission | Grant to | Purpose |
| --- | --- | --- |
| `pricing:view` | `admin`, `owner`, `org:boutique_owner`, `org:boutique_manager` | Read the price book and rule history |
| `pricing:manage` | `admin`, `owner` | Create, activate, cancel conversion rules and price entries |
| `pricing:backdate` | `owner` | Create a rule with an `EffectiveFrom` in the past, and run a recompute |

Boutique roles must **never** hold `pricing:manage`: a boutique changing its own
conversion rate is a direct revenue-integrity risk.

Because `Permissions.All` is a static set and the policy loop registers one
policy per member (`AuthorizationConfiguration.cs:91-95`), adding to
`Permissions.All` automatically registers the new policies. No new policy
constant is required.

> **Admin surface is team-only (#237).** The `pricing:view` grant to
> `org:boutique_owner` / `org:boutique_manager` above describes the permission
> catalog only. The `/admin/pricing/**` read routes are gated by the team-only
> `PricingAdminRead` policy (Admin or Owner), matching
> [../api/README.md §C.1](../api/README.md), which states these permissions are
> never available to boutique roles. The price-entry lookup is additionally scoped
> to the caller's organization: a global entry remains readable, while a
> per-organization override is readable only within its own organization.

### 3.6 Events, jobs, and webhooks

| Kind | Name | Trigger | Payload |
| --- | --- | --- | --- |
| Event | `pricing.rule.activated` | Rule activated | `{ ruleId, scopeKind, provider, model, unitsPerBlossom, effectiveFrom, actorUserId }` |
| Event | `pricing.rule.cancelled` | Draft rule cancelled | `{ ruleId, actorUserId, reason }` |
| Job | `PricingRuleCacheWarmer` (hosted service) | Every 60 s | Pre-resolves all Active rules into the in-process cache |
| Job | `PricingRecomputeJob` | On demand, admin-triggered | Recomputes affected `AiUsageRecord` rows; writes `BlossomLedgerEntry` corrections |
| Webhook | none | — | No outbound webhook is required for pricing. |

Cache invalidation: activation publishes `pricing.rule.activated` (and cancellation
publishes `pricing.rule.cancelled`) on the existing Redis event bus (`ADR-014`), but
**no subscriber clears the L1 cache cross-instance yet** (#240). The 5-second
`PricingRuleCache` TTL is therefore the effective safety net on every instance other
than the one that handled the write; within a process the generation counter
invalidates immediately. Wiring an event subscriber remains open.

Atomicity: activation trims the predecessor and activates the successor inside one
database transaction, so a failed successor write rolls the trim back (BR-1.8). The
optional `{ "effectiveFrom": "<iso8601>" }` request body is honoured; omitting it
keeps the rule's stored `EffectiveFrom`.

### 3.7 Edge cases

| Case | Behaviour |
| --- | --- |
| No rule matches | Fallback defaults + Warning log + `X-Pricing-Rule-Fallback: true` on the internal ingest response |
| Two rules tie on specificity and `EffectiveFrom` | Impossible: the exclusion constraint (BR-1.3) plus the uniqueness of `(ScopeKind, Provider, Model, EffectiveFrom)` prevent it |
| Rule activated while a workflow is mid-flight | The workflow uses the ingest-time rule; a workflow spanning an activation boundary is priced entirely at ingest time (deterministic) |
| Rate change makes historical usage more expensive | Historical rows are unaffected. Only the reconciliation report changes. |
| `UnitsPerBlossom` increased 10× mid-month | Remaining Blossoms stretch further; used Blossoms do not change. Documented and surfaced in the reconciliation report. |
| Backdated rule overlapping an existing window | Rejected with 409 by the exclusion constraint |
| `EffectiveFrom` in the past with no `pricing:backdate` | 403 |
| Recompute while usage is still arriving | The job pins a `asOf` timestamp and processes only `CreatedAt <= asOf` rows |

### 3.8 Audit logging

Every create/activate/cancel writes an `AuditLogEntry` with `EntityType =
"BlossomConversionRule"`, `Action` in `pricing.rule.created|activated|cancelled`,
and `BeforeJson`/`AfterJson` snapshots. Recompute runs write one entry per
affected record plus one summary entry. See [domain-model.md §4](domain-model.md).

### 3.9 Testing requirements

| Level | Cases |
| --- | --- |
| Unit | Resolution precedence (model > provider > global); window boundary inclusivity (`EffectiveFrom` inclusive, `EffectiveTo` exclusive); each rounding mode; minimum-charge clamping; zero-token workflow; fallback when no rule matches |
| Integration | Exclusion constraint rejects overlap (409); activation trims the predecessor in one transaction; a rate change does not alter existing `AiUsageRecord.BlossomUnits`; a new record picks up the new rate; cache invalidation lands within the TTL |
| Contract | `POST /internal/usage/record` response gains pricing snapshot fields without breaking the existing response shape |
| Load | 500 concurrent ingest calls for one org at a rate-change boundary — no lost updates, no wrong-price rows (directly targets D-3) |
| Migration | Existing rows backfilled with `PricingRuleId = NULL` and `UnitsPerBlossom = 1000`, and a recompute of those rows is a no-op |

---

## 4. Feature area 2 — Organization Blossom account operations

### 4.1 Interpretation

Four operations: **upgrade** (change plan, changing the allowance), **add**
(credit Blossoms), **deduct** (debit Blossoms), **remove** (revoke a previously
granted credit).

### 4.2 Functional requirements

| ID | Requirement |
| --- | --- |
| FR-2.1 | An organisation's Blossom entitlement and consumption must be reconstructible from an append-only ledger plus the consumption log. Today the balance is a single mutable column. |
| FR-2.2 | An administrator must be able to credit Blossoms to an organisation with a mandatory reason, an audit record, and an optional expiry. |
| FR-2.3 | An administrator must be able to debit Blossoms from an organisation with a mandatory reason and an audit record. A debit may take the balance negative only if `allowNegative = true` is explicitly passed; otherwise it is rejected with 409. |
| FR-2.4 | A previously granted credit must be revocable (full reversal only; partial reversal = one revoke + one new smaller credit). |
| FR-2.5 | A plan upgrade must take effect immediately: `MonthlyBlossomLimit` is replaced, the difference is written to the ledger as a proration adjustment, and the organisation can consume immediately. |
| FR-2.6 | A plan downgrade must be validated against current usage and must be rejected with 409 if any of the target plan's limits (Blossoms, staff seats, active customers) would be exceeded. |
| FR-2.7 | Add/deduct/remove/upgrade must be idempotent under retry via a client-supplied `Idempotency-Key`. Replaying the same key returns the original response with `Idempotency-Replayed: true`. |
| FR-2.8 | Concurrent balance mutations for one organisation must not lose updates. |
| FR-2.9 | Every mutation must be readable as a full statement of account for any period. |
| FR-2.10 | A closed period must not accept further entitlement mutations. |
| FR-2.11 | Deduct/remove must alert when the resulting available balance is negative. |

### 4.3 Data model

Three new entities and two extended ones. Full columns in
[domain-model.md §3](domain-model.md).

- **`BlossomLedgerEntry`** (new, append-only) — the entitlement statement.
  `EntryType` ∈ `PeriodAllocation | TopUpGrant | AdminCredit | AdminDebit |
  TopUpRevocation | Expiry | PlanUpgradeProration | PlanDowngradeAdjustment |
  CorrectionRecompute`. Signed `BlossomDelta`, `BlossomBalanceAfter`,
  `Reason`, `SourceKind`, `SourceRef?`, `ExpiresAt?`, `IdempotencyKey?`,
  `CreatedByUserId?`, `CreatedAt`.
- **`UsageAccount`** (extended, still the O(1) balance projection) —
  adds `BlossomGranted`, `BlossomAdjusted`, `PlanTierSnapshot`, `IsClosed`,
  `ClosedAt`, `ConcurrencyToken`.
- **`IdempotencyRecord`** (new) — replay store.
- **`OrganizationSubscription`** (new) — current plan, cycle, status, period
  bounds, provider linkage.
- **`PlanEntitlement`** (new) — effective-dated plan limits, replacing the three
  hardcoded copies (D-12).
- **`PlanEntitlementOverride`** (new) — per-organisation Enterprise exceptions.

### 4.4 Balance definition (normative)

```
available_blossoms(org, period)
  = UsageAccount.MonthlyBlossomLimit
  + UsageAccount.BlossomGranted       -- sum of positive non-consumption entries
  - UsageAccount.BlossomAdjusted      -- sum of negative non-consumption entries
  - UsageAccount.BlossomUsed          -- sum of AiUsageRecord.BlossomUnits
```

`BlossomRemaining` is maintained as exactly this expression and is asserted by a
reconciliation test. `BlossomGranted`/`BlossomAdjusted` are written **in the same
transaction** as the corresponding `BlossomLedgerEntry`, preserving the
`ADR-010` decision that the ledger is written atomically.

`BlossomUsed` is deliberately **not** duplicated into `BlossomLedgerEntry`.
Consumption already has an authoritative append-only log (`AiUsageRecord`), and
duplicating it would double the write volume on the hot path for no query
benefit. See [Open Question OQ-3](assumptions-and-open-questions.md) for the
alternative.

### 4.5 Business rules and validation

| ID | Rule |
| --- | --- |
| BR-2.1 | `BlossomDelta != 0`. Credit amounts `> 0`, debit amounts `< 0` (sign is derived from `EntryType`, the API takes a positive magnitude and a direction). |
| BR-2.2 | Precision is `decimal(18,4)`; the API rejects more than 4 decimal places with 400. |
| BR-2.3 | A single operation is capped by `Billing:MaxAdjustmentBlossoms` (default `10000`). Above the cap, 400 with the configured ceiling named in the message. |
| BR-2.4 | `Reason` is required, 10–500 characters. |
| BR-2.5 | A debit that would make `available_blossoms < 0` is rejected with 409 unless `allowNegative: true`. |
| BR-2.6 | `ExpiresAt`, when present, must be strictly after `CreatedAt` and, for a top-up tied to a subscription, must not exceed the subscription's `CurrentPeriodEnd` unless `Billing:AllowCrossPeriodTopUps` is `true` (default `false`). |
| BR-2.7 | Revocation amount must be `<=` the remaining un-expired, un-revoked amount of the referenced grant; otherwise 409 with the available amount in the response. |
| BR-2.8 | Idempotency key uniqueness is scoped to `(OrganizationId, Endpoint, IdempotencyKey)`. A replay with a *different* request body returns 409 `idempotency-key-reuse`. |
| BR-2.9 | Idempotency records expire after `Billing:IdempotencyRetentionHours` (default 24). |
| BR-2.10 | Upgrade: `newLimit > currentLimit` OR `PlanTier` changes to a higher tier. Proration delta = `newLimit - oldLimit` for the current period, written as `PlanUpgradeProration`. |
| BR-2.11 | Downgrade: takes effect at `CurrentPeriodEnd` by default. An immediate downgrade requires `effective: "immediate"` and passes the BR-2.12 guard. |
| BR-2.12 | Downgrade guard: reject with 409 if `BlossomUsed > targetLimit`, or `active membership count > target.staff.max`, or `active customer count > target.customers.active.max`. The response lists each violated limit with observed and allowed values. |
| BR-2.13 | Period close: a `UsageAccount` with `IsClosed = true` rejects all entitlement mutations with 409 `period-closed`. Consumption writes after close are allowed but flagged and included in the next period's reconciliation. |
| BR-2.14 | Concurrency: `UsageAccount.ConcurrencyToken` is an Npgsql `xmin` system-column token. On `DbUpdateConcurrencyException` the operation retries up to 5 times with jittered backoff, then returns 409 `concurrent-modification`. |
| BR-2.15 | `PlanEntitlement` lookups follow the same effective-dating rules as FR-1.1–FR-1.5, with `PlanEntitlementOverride` taking precedence over the tier row. |
| BR-2.16 | Plan limits must no longer be hardcoded. `UsageTrackerService` and `OnboardingService` both read from `IEntitlementResolver` (fixes D-1, D-2, D-12). |

### 4.6 Permissions

| Permission | Grant to | Purpose |
| --- | --- | --- |
| `billing:view` | `admin`, `owner`, `moderator`, `org:boutique_owner`, `org:boutique_manager` | Read balance, statement, and usage |
| `billing:view:self` | `org:boutique_staff`, `org:boutique_manager`, `org:boutique_supervisor`, `org:boutique_owner` | Read the shop's Blossom balance only (the self-service read). Deliberately separate from `billing:view` so the associate who needs to know what the shop has left does not become a reader of statements and burn-rate. |
| `billing:manage` | `admin`, `owner`, `org:boutique_owner` | Change own plan, purchase top-ups |
| `billing:adjust` | `admin`, `owner` | Credit, debit, revoke, close a period, override entitlements |

`org:boutique_supervisor` and below hold `billing:view` **not at all** — the
management read (statement, burn-rate) is commercially sensitive and there is no
operational need. The **balance** is different: an associate needs to know how
many Blossoms the shop has left before asking the assistant for something
expensive, so the balance route is gated by `billing:view:self` on a named
org-scoped policy (`BoutiqueBillingSelfView`), not by `billing:view`.

### 4.7 Events, jobs, and webhooks

| Kind | Name | Trigger |
| --- | --- | --- |
| Event | `blossom.ledger.credited` | Any positive entry |
| Event | `blossom.ledger.debited` | Any negative entry |
| Event | `blossom.balance.threshold` | Available balance crosses `Billing:LowBalanceThresholdPercent` (default 20 %) |
| Event | `org.plan.changed` | Upgrade or downgrade committed |
| Event | `blossom.balance.exhausted` | Available balance reaches 0 |
| Job | `BlossomExpiryJob` (hosted service, hourly) | Writes `Expiry` entries for grants past `ExpiresAt` |
| Job | `BillingPeriodRolloverJob` (hosted service, hourly, idempotent) | Opens the next `UsageAccount`, snapshots the plan tier, closes the previous period |
| Job | `IdempotencyRecordCleanupJob` (daily) | Deletes expired replay records |
| Webhook | `blossom.balance.threshold` forwarded to the existing notification dispatcher | Low-balance notification to org owners and Aveline admins |

Note: `ADR-010` §Decision 6 states no enforcement happens in this slice. These
requirements do not add request blocking either — enforcement remains deferred.
What changes is that the *balance becomes correct and auditable*, which is a
prerequisite for enforcement. See [Open Question OQ-5](assumptions-and-open-questions.md).

### 4.8 Edge cases

| Case | Behaviour |
| --- | --- |
| Duplicate `Idempotency-Key`, same body | Original response replayed, 200/201, `Idempotency-Replayed: true` |
| Duplicate `Idempotency-Key`, different body | 409 `idempotency-key-reuse` |
| Debit larger than available, `allowNegative = false` | 409 with `available` and `requested` in the body |
| Debit larger than available, `allowNegative = true` | Accepted; `blossom.balance.exhausted` event; `SystemAlert` at Warning |
| Revoke a grant that already expired | 409; the expiry entry is the authoritative reversal |
| Upgrade to the same tier | 400 `no-op-plan-change` |
| Upgrade mid-period when the new plan is cheaper | Accepted; `BlossomGranted` delta may be negative, written as `PlanDowngradeAdjustment` |
| Two admins credit simultaneously | Both succeed; ledger has two entries; balance is the sum (no lost update, thanks to BR-2.14) |
| Ledger and projection disagree | `GET .../statement` includes a `reconciliation` block computed from the ledger; a mismatch raises a Critical `SystemAlert` |
| Org deleted / deactivated mid-operation | 404 / 409 `organization-inactive` |
| Period rollover while a request is in flight | Rollover job takes the same row lock; the late request lands in the new period and is flagged |

### 4.9 Audit logging

Every mutation writes an `AuditLogEntry`. `BlossomLedgerEntry` is itself a
business-grade audit record, so the `AuditLogEntry` for adjustments carries a
reference rather than a duplicate payload. Plan changes additionally write
`BeforeJson`/`AfterJson` over the subscription row.

### 4.10 Testing requirements

| Level | Cases |
| --- | --- |
| Unit | Balance formula; each `EntryType`'s sign; cap enforcement; reason validation; downgrade guard for each of the three limits; expiration computation |
| Integration | Concurrent credit/debit from 20 parallel tasks yields the exact arithmetic sum (targets D-3); idempotent replay returns the byte-identical body; `period-closed` rejection; reconciliation mismatch raises an alert |
| Database | Exclusion/uniqueness constraints; `xmin` concurrency token actually raises `DbUpdateConcurrencyException`; filtered unique index on `IdempotencyKey` |
| Migration | Backfill: every existing `UsageAccount` gets a synthesised `PeriodAllocation` ledger entry equal to its `MonthlyBlossomLimit`, and `BlossomGranted = BlossomAdjusted = 0`, leaving `BlossomRemaining` unchanged |
| Load | 1 000 sequential consumption writes plus 50 concurrent adjustments against one org — zero drift between the ledger sum and the projection |

---

## 5. Feature areas 3 and 4 — User and Organization management

These are grouped because they share entities, permissions, and endpoints.

### 5.1 What exists (verified inventory)

| Method | Path | Policy | Source |
| --- | --- | --- | --- |
| `GET` | `/api/v1/users/me` | authenticated | `Endpoints/UserEndpoints.cs:16` |
| `POST` | `/api/v1/users/onboarding` | authenticated | `Endpoints/UserEndpoints.cs:35` |
| `GET` | `/api/v1/auth/claims` | authenticated | `Endpoints/AuthEndpoints.cs:16` |
| `POST` | `/api/v1/orgs` | authenticated | `Endpoints/OrganizationEndpoints.cs:146` |
| `GET` | `/api/v1/orgs/my` | authenticated | `Endpoints/OrganizationEndpoints.cs:209` |
| `GET` | `/api/v1/orgs/by-slug/{slug}` | authenticated | `Endpoints/OrganizationEndpoints.cs:240` |
| `GET` | `/api/v1/orgs/{organizationId:guid}` | `BoutiqueAccess` | `Endpoints/OrganizationEndpoints.cs:274` |
| `POST` | `/api/v1/orgs/{organizationId:guid}/invitations` | `BoutiqueMembershipManage` | `Endpoints/OrganizationEndpoints.cs:36` |
| `GET` | `/api/v1/orgs/{organizationId:guid}/invitations` | `BoutiqueMembershipManage` | `Endpoints/OrganizationEndpoints.cs:107` |
| `POST` | `/api/v1/orgs/{organizationId:guid}/invitations/{invitationId:guid}/revoke` | `BoutiqueMembershipManage` | `Endpoints/OrganizationEndpoints.cs:117` |
| `POST` | `/api/v1/invitations/accept` | authenticated + IP rate limit 10/min | `Endpoints/OrganizationEndpoints.cs:349,368` |
| `POST` | `/api/v1/orgs/{organizationId:guid}/members/{userId:guid}/suspend` | `BoutiqueMembershipManage` | `Endpoints/OrganizationEndpoints.cs:286` |
| `POST` | `/api/v1/orgs/{organizationId:guid}/members/{userId:guid}/activate` | `BoutiqueMembershipManage` | `Endpoints/OrganizationEndpoints.cs:308` |
| `DELETE` | `/api/v1/orgs/{organizationId:guid}/members/{userId:guid}` | `BoutiqueMembershipManage` | `Endpoints/OrganizationEndpoints.cs:326` |
| `GET`/`PUT`/`POST`/`DELETE` | `/api/v1/orgs/{organizationId:guid}/integrations...` | `BoutiqueMembershipManage` | `Endpoints/IntegrationEndpoints.cs:24-105` |
| `GET`/`POST` | `/api/v1/onboarding/{status,owner,plan,customize,complete}` | authenticated | `Endpoints/OnboardingEndpoints.cs:16-124` |
| `GET`/`POST`/`DELETE` | `/api/v1/users/me/devices...` | authenticated | `Endpoints/DeviceTokenEndpoints.cs:18-55` |
| `GET` | `/api/v1/orgs/{organizationId:guid}/stats/home` | `BoutiqueAccess` (`catalog:view` on an active membership) | `Modules/Home/Endpoints/HomeEndpoints.cs` |
| `POST` | `/api/v1/orgs/{organizationId:guid}/focus/dismissals` | `BoutiqueAccess` + required `Idempotency-Key` | `Modules/Home/Endpoints/HomeEndpoints.cs` |
| `GET` | `/api/v1/orgs/{organizationId:guid}/customers` | `BoutiqueCustomerAccess` (`customers:view` on an active membership) | `Endpoints/CustomerTenantEndpoints.cs` |
| `GET` | `/api/v1/orgs/{organizationId:guid}/customers/highlights` | `BoutiqueCustomerAccess` | `Endpoints/CustomerTenantEndpoints.cs` |
| `POST` | `/api/v1/orgs/{organizationId:guid}/customers` | `BoutiqueCustomerAccess` + required `Idempotency-Key` | `Endpoints/CustomerTenantEndpoints.cs` |
| `POST` | `/api/v1/orgs/{organizationId:guid}/customers/{customerId:guid}/interactions` | `BoutiqueCustomerAccess` + required `Idempotency-Key` | `Endpoints/CustomerTenantEndpoints.cs` |

### 5.1b Functional requirements — Home focus surface

| ID | Requirement |
| --- | --- |
| FR-8.1 | The focus feed is **derived on every read** from facts that already exist: `wardrobe` from inventory at or below the reorder line (`Home:LowStockThreshold`, default 5), `patron` from active customer events inside `Home:PatronWindowDays` (default 7), and `commerce` from `AgentWorkflowRuns` paused for approval — the last only when the caller's role holds `stats:view:agent`. |
| FR-8.2 | `logistics` has no writer in the product. The feed reports `dataQuality.logisticsAvailable = false` rather than counting the domain as zero, so an absent column is explained rather than measured. |
| FR-8.3 | The day boundary comes from `Organization.TimeZone` (IANA), never from the device. The window actually used is echoed as `window.localDate` / `window.timeZone`. |
| FR-8.4 | A sign-off persists a **dismissal**, not a task state: `(OrganizationId, UserId, Domain, SourceKey)` with the decision and a server-computed content hash. The feed suppresses a docket only while the source key matches and the content hash is unchanged, so a changed fact reappears. |
| FR-8.5 | A dismissal must name a docket present in the caller's own feed. A docket from another organization returns `404`, indistinguishable from one that does not exist, and an outsider gets `403` from the org-scope requirement before the lookup. |
| FR-8.6 | A dismissal is idempotent by nature (dismissing twice is the same end state) and requires `Idempotency-Key`; a replay with the same body returns the stored response, and an unreachable lease store is `503`, never a silent apply. |

### 5.1c Functional requirements — tenant customer surface

| ID | Requirement |
| --- | --- |
| FR-8.7 | `GET /orgs/{organizationId}/customers` returns the whole narrowing in one call (the alphabet index must reach every letter), under `BoutiqueCustomerAccess`. The route is **not** in `/internal/customers`: that group accepts only the internal-token scheme and is for the agent service. |
| FR-8.8 | `GET /orgs/{organizationId}/customers/highlights` returns the clients with something happening, with `activity` generated from a real `Customer_Interactions` row. There is **no** `hasNewActivity` flag: no read marker exists, and a dot that can never clear must not ship. |
| FR-8.9 | `Customer.Level` is a nullable grade (`vip | level3 | level2 | level1`) with no default and no backfill; consumers omit the badge when it is null. |
| FR-8.10 | `POST /orgs/{organizationId}/customers` creates a counter walk-in from a name alone (phone optional), de-duplicating on the normalised name within the organization. A duplicate answers `200` with `duplicateOfCustomerId` rather than creating a second client, and requires `Idempotency-Key`. |
| FR-8.11 | `POST /orgs/{organizationId}/customers/{customerId}/interactions` records the interaction and, for an **inbound in-person** one, moves `VisitCount`, `LastVisitAt` and (when a purchase is given) `TotalSpent`, then recomputes `Status` through `CustomerLoyaltyService.RecommendStatus`. A message on another channel is recorded but does not count as a visit. |
| FR-8.12 | The counter increment is a single atomic statement on a relational provider (`ExecuteUpdateAsync`), so two concurrent visits at the counter cannot lose one. `CustomerRepository.SaveAsync`'s read-modify-write must not be used for it. |
| FR-8.13 | A visit is **not billable**: `blossomsCharged` is always `0`, and the response states it so the client cannot invent a charge. Consumption is an `AiUsageRecord` written after an agent workflow, and no rule debits a Blossom for a visit. |

### 5.2 Functional requirements — user management

| ID | Requirement |
| --- | --- |
| FR-3.1 | `GET /api/v1/orgs/{organizationId}/members` — paginated, filterable by `status`, `role`, `q`. Currently **absent**; owners cannot list their own team via the API. |
| FR-3.2 | `PATCH /api/v1/users/me` — update `firstName`, `lastName`, `displayName`, `phoneNumber`, `profileImageUrl`, `contactPreference`, `pushNotificationsEnabled`. | 
| FR-3.3 | `PATCH /api/v1/orgs/{organizationId}/members/{userId}` — change `boutiqueRole`. Guarded by `settings:manage`. |
| FR-3.4 | Role-change rules: only `org:boutique_owner` may grant/revoke `org:boutique_owner`; an owner may not demote the last active owner; a user may not change their own role. |
| FR-3.5 | `DELETE /api/v1/users/me` — soft delete: sets `DeletedAt`, `IsActive = false`, `AccountState = Suspended`, revokes all memberships, revokes all API keys, invalidates the cached authorization snapshot. |
| FR-3.6 | `POST /api/v1/users/me/sessions/revoke-all` and `GET /api/v1/users/me/sessions` — proxied to the Clerk Backend API through the existing `ClerkAdminClient`. Clerk owns sessions; Aveline does not store them. |
| FR-3.7 | `GET /api/v1/admin/users` — Aveline-team-only paginated user search across organisations, for support. Guarded by `admin:users:read`. |
| FR-3.8 | `PATCH /api/v1/admin/users/{userId}/state` — set `AccountState` to `Active`/`Suspended`. Guarded by `admin:users:manage`. Suspension immediately invalidates the auth cache. |
| FR-3.9 | Account state transitions must be validated: `OnboardingPending -> Active` (onboarding completes), `Active -> Suspended`, `Suspended -> Active`. `OnboardingPending -> Suspended` is allowed (abuse). No other transitions. |

### 5.3 Functional requirements — API keys

Rose tier advertises "API Access" (`docs/architecture/pricing_plan.md:303`) and
**no API key mechanism exists**. This is new.

| ID | Requirement |
| --- | --- |
| FR-3.10 | `POST /api/v1/orgs/{organizationId}/api-keys` creates a key. The plaintext secret is returned **exactly once** in the 201 response and is never retrievable again. |
| FR-3.11 | `GET /api/v1/orgs/{organizationId}/api-keys` lists keys with `prefix`, `name`, `scopes`, `status`, `lastUsedAt`, `createdAt`, `expiresAt`. Never the secret or its hash. |
| FR-3.12 | `POST /api/v1/orgs/{organizationId}/api-keys/{keyId}/revoke` revokes immediately. |
| FR-3.13 | `DELETE /api/v1/orgs/{organizationId}/api-keys/{keyId}` hard-deletes a key that has never been used; 409 otherwise. |
| FR-3.14 | Key format: `avl_` + environment (`live`/`test`) + `_` + 32 base62 random characters. The stored `Prefix` is the first 16 characters and is the lookup index; the remaining secret is verified by constant-time comparison of a SHA-256 hash. |
| FR-3.15 | Scopes are a subset of the permission catalog. An API key can never hold `pricing:*`, `billing:adjust`, or `admin:*`. Attempting to grant one is 400. |
| FR-3.16 | API keys are gated by entitlement `api.access` (true only on Rose and Enterprise). Creating one on a lower tier is 402/403 per [Open Question OQ-7](assumptions-and-open-questions.md). |
| FR-3.17 | An API key authenticates as a third scheme and produces a principal with `OrganizationId` fixed to the owning org, so `OrganizationScopeAuthorizationHandler` can evaluate it. |
| FR-3.18 | `LastUsedAt` is updated at most once per minute per key (write amortisation), via the API-consumption statistics pipeline rather than a per-request update. |

### 5.4 Functional requirements — organization management

| ID | Requirement |
| --- | --- |
| FR-4.1 | `PATCH /api/v1/orgs/{organizationId}` — update `name`, `address`, `phoneNumber`, `description`, `logoUrl`, `brandVoice`, `businessRules`, `preferredColorsFabrics`, `customerPreferences`. Guarded by `settings:manage`; the AI-context fields additionally require the plan entitlement that already gates them (`OnboardingService.SaveAiCustomizationAsync`). |
| FR-4.2 | `GET /api/v1/orgs/{organizationId}/settings` — read the settings block plus the resolved entitlements for the current plan. |
| FR-4.3 | `GET /api/v1/orgs/{organizationId}/subscription` — current plan, cycle, status, period bounds, seats, price, `cancelAtPeriodEnd`. |
| FR-4.4 | `POST /api/v1/orgs/{organizationId}/subscription/change-plan` — upgrade or downgrade. Delegates to the Blossom account operation in §4 (same transaction, same idempotency key). |
| FR-4.5 | `POST /api/v1/orgs/{organizationId}/subscription/cancel` — sets `CancelAtPeriodEnd = true`. |
| FR-4.6 | `GET /api/v1/orgs/{organizationId}/entitlements` — resolved effective entitlements, each with `key`, `valueType`, `value`, `source` (`Plan`/`Override`), `effectiveFrom`. |
| FR-4.7 | `GET /api/v1/orgs/{organizationId}/entitlements/usage` — current observed consumption against each limited entitlement (`staff`, `activeCustomers`, `blossoms.monthly`). |
| FR-4.8 | `GET /api/v1/admin/orgs` — Aveline-team paginated org search. Guarded by `admin:orgs:read`. |
| FR-4.9 | `PATCH /api/v1/admin/orgs/{organizationId}/entitlement-overrides` — set per-org entitlement overrides for Enterprise. Guarded by `billing:adjust`. |
| FR-4.10 | `POST /api/v1/admin/orgs/{organizationId}/blossoms/credit`, `/debit`, `/revoke` — the admin Blossom operations of §4. |
| FR-4.11 | `GET /api/v1/admin/orgs/{organizationId}/blossoms/statement` — the statement of account for a period. |

### 5.5 Data model deltas

| Entity | Change |
| --- | --- |
| `User` | No schema change required for FR-3.2–FR-3.5. `DeletedAt`, `IsActive`, `AccountState` already exist (`Modules/Shared/Models/User.cs`). |
| `Organization` | Optional `BillingEmail`, `ContactEmail`, `Currency` (default `LKR`), `TimeZone` (IANA, default `UTC`). See [OQ-4](assumptions-and-open-questions.md). |
| `OrganizationMembership` | No change. Add a unique index on `(UserId, OrganizationId)` — verify it exists; the current configuration was not confirmed to have one. |
| `OrganizationInvitation` | No change. |
| `ApiKey` | **New.** See [domain-model.md §5](domain-model.md). |
| `OrganizationSubscription` | **New.** |
| `PlanEntitlement`, `PlanEntitlementOverride` | **New.** |
| `AuditLogEntry` | **New.** |

### 5.6 Business rules and validation

| ID | Rule |
| --- | --- |
| BR-3.1 | Invitable roles are restricted to the boutique role set; the existing validation rejects anything else (`OrganizationEndpoints.cs:49`). Preserve the exact string values from `Authorization/Roles.cs:18-21`. |
| BR-3.2 | A suspension or removal must invalidate the member's cached authorization snapshot (existing behaviour, `docs/architecture/authorization.md:37-42`). Extend the same invalidation to role changes and account-state changes. |
| BR-3.3 | A membership removal must not remove the organisation owner's membership. Existing guard, preserve it (`docs/architecture/authorization.md:40-42`). |
| BR-3.4 | API-key scopes must be validated against `Permissions.All` at creation *and* at authorization time. A scope removed from the catalog silently stops granting — this is intentional fail-closed behaviour. |
| BR-3.5 | An API key whose owning organisation has `IsActive = false` or `AccountState = Suspended` must fail authentication with 401, not 403, to avoid leaking org existence. |
| BR-3.6 | Revoking an API key must take effect within 60 seconds across all instances. Publish `apikey.revoked` on the event bus and clear the key cache. |
| BR-4.1 | `POST /orgs` slug generation and uniqueness already return 409 on collision (`OrganizationEndpoints.cs:201`). Preserve. |
| BR-4.2 | Plan change must be rejected when the org is not `Active` or has not completed onboarding. |
| BR-4.3 | Entitlement resolution must be the single source of truth; `UsageTrackerService` and `OnboardingService` must both use it (fixes D-1, D-2, D-12). |
| BR-4.4 | A downgrade that violates a limit must return 409 with a `violations` array, never silently clamp. |

### 5.7 Permissions — proposed additions

| Permission | Grant to | Purpose |
| --- | --- | --- |
| `billing:view` | `admin`, `owner`, `moderator`, `org:boutique_owner`, `org:boutique_manager` | Balance, statement, subscription, usage |
| `billing:manage` | `admin`, `owner`, `org:boutique_owner` | Plan change, cancel, top-up purchase |
| `billing:adjust` | `admin`, `owner` | Admin credit/debit/revoke, entitlement overrides, period close |
| `pricing:view` / `pricing:manage` / `pricing:backdate` | see §3.5 | Conversion rules and price book |
| `apikeys:view` | `admin`, `owner`, `org:boutique_owner` | List/read API keys |
| `apikeys:manage` | `admin`, `owner`, `org:boutique_owner` | Create/revoke/delete API keys |
| `stats:view` | `admin`, `owner`, `moderator`, `org:boutique_owner`, `org:boutique_manager`, `org:boutique_supervisor` | All statistics endpoints |
| `stats:view:agent` | `admin`, `owner`, `moderator` | Agentic statistics (reveals prompt/cost internals). **Not held by any boutique role**, and the org-scoped route group is removed |
| `stats:system` | `admin`, `owner` | System statistics and alerts |
| `admin:users:read` / `admin:users:manage` | `admin`, `owner` | Cross-org user administration |
| `admin:orgs:read` | `admin`, `owner`, `moderator` | Cross-org org search |
| `audit:view` | `admin`, `owner` | Read the audit log |

**Permission catalog growth is a security-relevant change.** The full mapping
must be re-derived in `Permissions.cs`, and `docs/architecture/authorization.md`
must be regenerated from the code, not hand-edited — the current doc is already
stale (D-9).

### 5.8 Events, jobs, and webhooks

| Kind | Name | Trigger |
| --- | --- | --- |
| Event | `user.profile.updated` | FR-3.2 |
| Event | `user.state.changed` | FR-3.8, FR-3.5 |
| Event | `user.session.revoked` | FR-3.6 |
| Event | `membership.role.changed` | FR-3.3 |
| Event | `membership.removed` | existing removal endpoint |
| Event | `org.settings.updated` | FR-4.1 |
| Event | `org.subscription.changed` | FR-4.4, FR-4.5 |
| Event | `apikey.created` / `apikey.revoked` | FR-3.10, FR-3.12 |
| Job | `ApiKeyUsageAggregator` (5 min) | Flush aggregated `lastUsedAt` from the telemetry pipeline |
| Job | `SubscriptionRenewalJob` (hourly) | Advance `CurrentPeriodStart/End`; detects `PastDue` — deferred to Phase 3 per ADR-010 |
| Webhook | Clerk user/org webhooks → `POST /api/v1/webhooks/clerk` | New. Keeps the local `User`/`Organization` read models in sync with Clerk. **This endpoint does not exist today** and is a correctness gap for FR-3.7/FR-3.8. |

### 5.9 Edge cases

| Case | Behaviour |
| --- | --- |
| Invite an email that already has an active membership | 409, message names the existing role |
| Accept an expired invitation | 410 Gone (existing tests pin 4xx; see `OrganizationInvitationLifecycleTests`) |
| Accept an invitation twice | Idempotent success, no duplicate membership |
| Last owner attempts self-demotion | 409 |
| User in two organisations switches context | Existing `org_id` claim handling; no change. `User.OrganizationId` (legacy `string`) is *not* the tenant key — the `OrganizationMembership` row is. |
| API key used after org suspension | 401 |
| API key with a scope the org's plan no longer includes | 403 on the specific scope, other scopes still work |
| `PATCH /orgs/{id}` with a field the plan does not entitle | 400 naming the required plan, matching the existing onboarding behaviour |
| Slug collision on rename | 409 |

### 5.10 Audit logging

`AuditLogEntry` for: profile update, role change, membership suspend/activate/
remove, account-state change, API key create/revoke/delete (hash never recorded),
org settings change, subscription change, entitlement override. Invitation
lifecycle is already modelled in `OrganizationInvitation`
(`AcceptedAt`/`RevokedAt`/`RevokedByUserId`) — no duplicate audit row is written.

### 5.11 Testing requirements

| Level | Cases |
| --- | --- |
| Unit | Role-change guards (self, last-owner, owner-grant); account-state transition matrix; API key generation/format/hash/constant-time compare; scope validation against the catalog |
| Integration | API key authentication end-to-end through `OrganizationScopeAuthorizationHandler`; revoked key fails within one cache TTL; cross-org key use returns 404 not 200; suspended org key returns 401; `PATCH /orgs/{id}` entitlement gating; downgrade guard `violations` array shape |
| Security | A key from org A cannot read org B's resources via any route; the plaintext secret never appears in any response after creation, in logs, or in the audit table |
| Contract | Existing tests in `OrganizationEndpointsIntegrationTests`, `OrganizationAuthorizationIntegrationTests`, `UserEndpointsIntegrationTests`, `InvitationManagementEndpointsIntegrationTests` continue to pass unchanged |

---

## 6. Feature area 5 — Agentic statistics

### 6.1 What exists and what does not

The only agentic telemetry that exists is one aggregate row per completed
workflow: `AiUsageRecord` with `WorkflowId`, `Provider`, `Model`,
`InputTokens`, `OutputTokens`, `CachedTokens`, `ActualCostUsd`, `BlossomUnits`,
`CreatedAt` (`Aveline.Api/Modules/Billing/Models/AiUsageRecord.cs:16-64`).

Confirmed absent:

- Any per-agent or per-node run record — `NOT FOUND`.
- Any latency measurement. `AgentMetadata.duration_ms` is **declared but never
  populated** by production code (`agnet-service/app/schemas/response.py:28`; only
  tests set it).
- Any tool-call record.
- Any retry counting — no retry logic exists in the agent service at all.
- Any run status column.
- Any persisted workflow-run entity in either service. `Message.WorkflowRunId`
  is a dangling correlator: it is written from an event payload
  (`Modules/Conversations/Services/ConversationService.cs:211`) but no entity
  with that id exists.
- Any attribution of tokens to a specific agent: the concierge orchestrator folds
  every agent's usage into a single total
  (`agnet-service/app/workflows/concierge_workflow.py:267-287`).

### 6.2 Instrumentation gaps and their exact hook points

Each gap was located in the agent service. These are the changes the Python
service must make for the statistics to be real.

| # | Missing datum | Hook |
| --- | --- | --- |
| G-1 | Per-node run records (node name, start/end) | Wrap nodes in `build_concierge_graph` (`agnet-service/app/workflows/concierge_workflow.py:352-357`) or handle `on_chain_end` in `state_events.py:63-74`, which already observes `metadata.langgraph_node` per node |
| G-2 | Per-node success/failure | `state_events.py:65-74` handles only `on_chain_start`; node errors are currently swallowed (`customer_memory/nodes.py:407-409`, `visual_insight/nodes.py:223-239`) |
| G-3 | Per-node and per-workflow latency | Timers in `state_events.py:63-74`; populate the unused `AgentMetadata.duration_ms` in `formulate_response` (`concierge_workflow.py:227-264`) |
| G-4 | Per-LLM-call token usage including model and cached tokens | Capture at each `ainvoke` (`customer_memory/nodes.py:404-406`, `visual_insight/nodes.py:260`); carry through state (`state.py:36-37`) and `AgentMetadata` (`concierge_workflow.py:267-287`) |
| G-5 | `cached_tokens` and `actual_cost_usd` (currently always 0) | `usage_reporter.py:19-20` parameters are never supplied by any caller (D-8) |
| G-6 | Tool-call telemetry (name, args hash, status, latency, error) | `InternalApiClient.request` (`tools/client.py:32-59`) or the `ToolRegistry` methods (`tools/registry.py:28+`) |
| G-7 | Retry counts | No retry logic exists; wrap `httpx.AsyncClient` (`tools/client.py:55`) and the LLM invokes |
| G-8 | Correct attribution for the visual agent | Replace the hardcoded values at `visual_insight/nodes.py:376-384` (D-4) |
| G-9 | Stable run identity | `thread_id` is nullable and `workflow_id` falls back to `request_id` (`agents.py:124`); publishers never receive `workflow_run_id` (`message_publisher.py:107-119` vs `agents.py:203-208`) |
| G-10 | Visibility for streaming runs | `/agents/query/stream` emits only event kinds, no usage or state (`agents.py:227-244`) (D-6) |
| G-11 | Reconstructable state history | `checkpointer` must move from the call site into `graph.compile(...)` (`concierge_workflow.py:367` vs `:426-431`) (D-5) |
| G-12 | Structured lifecycle logging | `state_events.py` has no logging; `on_state` publishes with `trace_id=None` (`agents.py:95-103`) |
| G-13 | Metrics and tracing exporters | `OTEL_EXPORTER_OTLP_ENDPOINT` defaults to empty (`config.py:47`); `chain_of_thought_span` (`tracing.py:77-122`) has **no call sites** — manual GenAI spans never fire |
| G-14 | Telemetry for runs with no resolvable org | `agents.py:120,139-149` skips reporting entirely (D-7) |

### 6.3 Functional requirements

| ID | Requirement |
| --- | --- |
| FR-5.1 | Every agent workflow invocation must produce exactly one `AgentWorkflowRun` row with status, timing, participants, aggregate tokens, aggregate cost, and aggregate Blossoms. |
| FR-5.2 | Every node execution within a workflow must produce one `AgentStepRun` row with agent key, node name, step kind, attempt number, status, duration, and per-call tokens. |
| FR-5.3 | Tool invocations must be recorded as `AgentStepRun` rows with `StepKind = ToolCall` and the tool name. |
| FR-5.4 | A workflow run must be attributable to an organisation, and optionally to a user, a conversation, and a customer. |
| FR-5.5 | A workflow run must link to the `AiUsageRecord` it produced, one-to-one. |
| FR-5.6 | Statistics must be queryable per agent, per organisation, per user, per trigger kind, and per time window. |
| FR-5.7 | Success rate, failure rate, p50/p95/p99 latency, token usage, and cost must be computable for each of those dimensions. |
| FR-5.8 | Runs that fail before the org is resolvable must still be recorded, attributed to organisation `NULL`, and flagged `unattributed`. |
| FR-5.9 | No prompt text, tool arguments, tool results, or customer PII may be stored in telemetry. Only hashes and byte counts. |
| FR-5.10 | The run-record state machine must be `Running -> Succeeded | Failed | Cancelled | TimedOut | PausedForApproval`, and `PausedForApproval -> Running -> terminal`. Once terminal, a run is immutable. |
| FR-5.11 | A run paused for human approval must record `PausedAt`, `ResumedAt`, and `ApprovalWaitMs` so approval latency is measurable. |
| FR-5.12 | Ingestion must be idempotent on `(OrganizationId, WorkflowId)`; a re-report updates a non-terminal run, or is rejected if terminal and byte-identical. |

### 6.4 Data model

**`AgentWorkflowRun`** and **`AgentStepRun`**. Full columns in
[domain-model.md §6](domain-model.md).

Key design decisions:

- `AgentWorkflowRun` is the analytics fact table. Its uniqueness on
  `(OrganizationId, WorkflowId)` makes ingestion idempotent.
- `AgentStepRun` carries a denormalised `OrganizationId` so that per-org
  statistics never require a join to the run table.
- Latency is stored as `DurationMs` (integer) on both tables. Percentiles are
  computed at query time from a bounded window, or from the rollup (§7).
- `AgentsInvolved` is a Postgres `text[]`, matching the existing project's
  willingness to use array/jsonb types. See
  [OQ-6](assumptions-and-open-questions.md) for the normalised alternative.
- `AiUsageRecord` gains a nullable `AgentWorkflowRunId` FK, plus the pricing
  snapshot columns from §3.

### 6.5 Business rules and validation

| ID | Rule |
| --- | --- |
| BR-5.1 | `WorkflowId` is required, 1–128 characters, and unique per organisation. |
| BR-5.2 | `CompletedAt >= StartedAt`; `DurationMs` must be consistent with the two within 5 ms, else the server recomputes it. |
| BR-5.3 | `Status = Succeeded` requires `CompletedAt` and no `ErrorCode`. A non-terminal status must not carry `CompletedAt`. |
| BR-5.4 | `StepIndex` is unique per `(WorkflowRunId, AttemptNumber)`. |
| BR-5.5 | `AgentKey` must be one of the registered agent keys. The registered set as of this commit is `customer_memory`, `visual_insight`, `commerce`, plus an `orchestrator` pseudo-agent for the top-level graph. `commerce` currently has **no graph** — `run_commerce_agent` is a stub (`concierge_workflow.py:205-224`) — so it will emit orchestrator-attributed steps only until it is implemented. |
| BR-5.6 | Token counts are non-negative; the workflow aggregate must equal the sum of its steps within a tolerance of 0, else the discrepancy is logged at Warning and the aggregate wins. |
| BR-5.7 | `ActualCostUsd >= 0`, `decimal(18,8)`. A zero cost is valid and expected while G-5 is unfixed. |
| BR-5.8 | Ingestion payload size is capped at `AgentStats:MaxStepsPerRun` (default 200) steps. Beyond that, 413 with the limit named. |
| BR-5.9 | Run retention is 400 days; step retention is 90 days. See [statistics-catalog.md](statistics-catalog.md) for the full retention matrix. |

### 6.6 Permissions

| Endpoint group | Permission |
| --- | --- |
| ~~`GET /api/v1/orgs/{organizationId}/statistics/agents*`~~ | **removed** — a boutique reads its usage in Blossoms |
| `GET /api/v1/admin/statistics/agents*` | `stats:system` |
| `POST /internal/agent-runs` | `InternalServicePolicy` |

### 6.7 Events, jobs, and webhooks

| Kind | Name | Trigger |
| --- | --- | --- |
| Internal endpoint | `POST /internal/agent-runs` | Agent service reports a completed or paused run with its steps |
| Internal endpoint | `POST /internal/agent-runs/{workflowId}/steps` | Incremental step append for long-running or streaming runs (optional, Phase 3) |
| Event | `agent.run.started` / `agent.run.completed` / `agent.run.failed` / `agent.run.paused` / `agent.run.resumed` | Run lifecycle |
| Event | `agent.run.anomaly` | Duration or step count exceeds the configured threshold |
| Job | `AgentStatsRollupJob` (hourly) | Rolls `AgentWorkflowRun` into `DailyAgentMetrics` (see [statistics-catalog.md](statistics-catalog.md)) |
| Job | `AgentStatsRetentionJob` (daily) | Deletes steps older than 90 days, runs older than 400 days |
| Webhook | none | Statistics are pull-only |

### 6.8 Edge cases

| Case | Behaviour |
| --- | --- |
| Agent service reports a run twice | Idempotent on `(OrganizationId, WorkflowId)`; second report overwrites non-terminal state |
| Run reported after the workflow already failed | Accepted; terminal state is final, and a later conflicting report is rejected with 409 |
| No org resolvable | Recorded with `OrganizationId = NULL`, `IsUnattributed = true`; excluded from org-scoped statistics, included in system statistics (fixes D-7) |
| 500-step runaway loop | Truncated at the cap with a `SystemAlert` and `[ABNORMAL_USAGE]` log |
| A node throws and is swallowed by the agent | The step must still be recorded as `Failed` (G-2). Until G-2 lands, the run will under-report failures — this must be stated in the API response as a `dataQuality` warning. |
| Clock skew between Python and .NET | Timings come from the agent's monotonic clock; the server never recomputes latency from wall clocks across services |
| Workflow spans a period boundary | The run belongs to the period of `StartedAt` |
| Paused run never resumed | Remains `PausedForApproval`; a stale-run job marks it `TimedOut` after `AgentStats:PausedRunTimeoutHours` (default 72) |

### 6.9 Audit logging

Agent runs are operational, not administrative: they are logged as structured
logs and stored in `AgentWorkflowRun`, and are **not** duplicated into
`AuditLogEntry`. Administrative actions *about* agent statistics (retention
changes, anomaly threshold changes) do write `AuditLogEntry`.

### 6.10 Testing requirements

| Level | Cases |
| --- | --- |
| Unit | Run-state transition matrix; step ordering; aggregate-versus-step token reconciliation; retention cutoffs; percentile computation |
| Integration (C#) | Ingestion idempotency; FK link to `AiUsageRecord`; org-scoped queries return only that org; `stats:view:agent` enforcement; unattributed run handling |
| Integration (Python) | `report_usage` → new `report_run` payload shape; steps present for every node in the concierge graph; the visual agent's workflow id is the real thread id, not the literal `"visual_insight"` (targets D-4) |
| Golden case | One full concierge workflow produces exactly 1 run and N steps where N equals the node count of the graph, with no step missing |
| Load | 200 concurrent ingest calls with 50 steps each complete within the ingest budget |
| Migration | `AiUsageRecord.AgentWorkflowRunId` backfilled `NULL`; existing rows still readable by every endpoint |

---

## 7. Feature area 6 — API consumption statistics

### 7.1 Current state

**Nothing exists.** There is no request-logging middleware, no correlation ID,
no per-key accounting, no quota table, and no rate accounting beyond two
ad-hoc `IRateLimiter` call sites (WhatsApp webhook, invitation accept). The
pipeline is exactly (`Program.cs:82-88`):

```
UseHttpsRedirection → UseAvelineSecurityHeaders → UseCors → UseAuthentication
→ UseAuthorization → UseAvelineAuthAudit → UseAvelineOnboarding → endpoints
```

`UseAvelineAuthAudit` logs only 401/403 outcomes at Warning level
(`Configurations/LoggingConfiguration.cs:38-59`).

### 7.2 Functional requirements

| ID | Requirement |
| --- | --- |
| FR-6.1 | Every request to `/api/v1/**` and `/internal/**` must be measured for count, latency, status class, and bytes, and attributed to an organisation, user, and API key where resolvable. |
| FR-6.2 | Attribution must use the **route template** (`/api/v1/orgs/{organizationId}/usage`), never the raw path, so cardinality stays bounded and no ids leak into metrics. |
| FR-6.3 | Measurement must add no more than 1 ms to p99 request latency and must never fail a request. |
| FR-6.4 | Raw per-request records must be retained 7 days for forensics; hourly rollups must be retained 400 days. |
| FR-6.5 | Statistics must be queryable by org, user, API key, endpoint, status class, and time window (hour/day/month). |
| FR-6.6 | Latency percentiles (p50/p95/p99) must be available per endpoint per window, computed from histogram buckets rather than stored percentiles. |
| FR-6.7 | Per-API-key and per-org request quotas must be defined per plan tier and enforced with a documented response when exhausted. |
| FR-6.8 | Rate-limit rejections (429) must be counted separately from other 4xx so the frontend can distinguish "you are being throttled" from "you sent a bad request". |
| FR-6.9 | Billing usage must be derivable: billable request count per org per period, excluding `/health`, `/openapi`, and CORS preflight (`OPTIONS`). |
| FR-6.10 | Every response must carry `X-Request-Id` (echoing the client's value or generating one) and `X-Trace-Id` when a trace exists. This fixes D-11 and is the join key between API statistics and agent statistics. |
| FR-6.11 | An unrecognised `X-Request-Id` must be rejected with 400 if it is longer than 128 characters or contains characters outside `[A-Za-z0-9._:-]`. |

### 7.3 Data model

**`ApiRequestMetric`** (hourly rollup, the primary read path) and
**`ApiRequestLog`** (raw, sampled, partitioned, short retention). Full columns in
[domain-model.md §7](domain-model.md).

Design notes:

- The rollup's unique key is
  `(OrganizationId, ApiKeyId, UserId, RouteTemplate, HttpMethod, StatusCode, WindowStart)`
  with `NULLS NOT DISTINCT` (PostgreSQL 15+; the stack is PostgreSQL 16 per
  `docs/ADR/ADR-003`). This makes the incremental upsert correct without a
  surrogate key.
- Latency is stored as fixed histogram bucket counts
  (`[5,10,25,50,100,250,500,1000,2500,5000,10000]` ms plus overflow), so
  percentiles are computed by interpolating the buckets. This is the standard
  approach and avoids storing per-request rows in the read path.
- The raw log **never stores request or response bodies**, query strings, or IP
  addresses. It stores a SHA-256 hash of the client IP for abuse forensics.
- Partitioning: `ApiRequestLog` is range-partitioned by day on `OccurredAt`.
  Partitions are created by a daily job and dropped by the retention job.

### 7.4 Business rules and validation

| ID | Rule |
| --- | --- |
| BR-6.1 | A request with no resolvable org is attributed to `OrganizationId = NULL` and counted in system statistics only. |
| BR-6.2 | `OPTIONS` requests and paths in `Telemetry:ExcludedPaths` (default `/health`, `/health/ready`, `/health/live`, `/openapi`) are not recorded. |
| BR-6.3 | Sampling: 100 % of non-2xx, 100 % of requests slower than `Telemetry:SlowRequestMs` (default 1000), and `Telemetry:SuccessSampleRate` (default 0.1) of the rest are written to the raw log. Rollups are **never sampled** — they are exact. |
| BR-6.4 | The telemetry writer uses a bounded `Channel<ApiRequestSample>` (capacity `Telemetry:BufferCapacity`, default 10 000). When full, samples are dropped and `Telemetry:DroppedSamples` is incremented. Metrics are best-effort by design. |
| BR-6.5 | Quota evaluation happens before the endpoint and returns 429 with `{ message, quota: { metricKey, limit, used, resetsAt } }`. |
| BR-6.6 | Quota counters are stored in Redis with a period-scoped key and an atomic `INCR`; Postgres is the durable backfill via `ApiQuotaUsage`, written at period rollover. |
| BR-6.7 | Rate limits remain on the existing `IRateLimiter`. The known non-atomicity of `DistributedRateLimiter` (read-modify-write over `IDistributedCache`, fails open — `Infrastructure/RateLimiting/DistributedRateLimiter.cs:42-66`) must be documented as a known limitation, and the new per-API-key limits must use a Redis Lua atomic increment instead of the existing helper. |
| BR-6.8 | Percentiles are computed only when `RequestCount >= Telemetry:MinSampleForPercentile` (default 20); below that the response returns `null` with a `reason` field rather than a misleading number. |
| BR-6.9 | Time windows are UTC. `from` and `to` must be ISO 8601 with `Z`; a window longer than `Telemetry:MaxWindowDays` (default 92) returns 400. |
| BR-6.10 | Rollup aggregation is idempotent: re-running `ApiStatsRollupJob` for a window recomputes and replaces rather than adds. |

### 7.5 Permissions

| Endpoint group | Permission |
| --- | --- |
| `GET /api/v1/orgs/{organizationId}/statistics/api*` | `stats:view` |
| `GET /api/v1/orgs/{organizationId}/statistics/api-keys*` | `stats:view` |
| `GET /api/v1/admin/statistics/api*` | `stats:system` |
| `GET /api/v1/admin/statistics/api-keys*` | `stats:system` |

A boutique may see its own key-level statistics but never another org's, and
never the system-wide view.

### 7.6 Events, jobs, and webhooks

| Kind | Name | Trigger |
| --- | --- | --- |
| Event | `apiquota.exhausted` | Quota counter reaches its limit |
| Event | `apiquota.warning` | Counter crosses `Telemetry:QuotaWarningPercent` (default 80 %) |
| Event | `apikey.lastused` | Aggregated key usage flush (feeds FR-3.18) |
| Job | `ApiTelemetryWriter` (hosted service, continuous) | Drains the channel in batches of 500, every 2 s or when the batch fills |
| Job | `ApiStatsRollupJob` (hourly) | Rollup; also closes the previous hour exactly once |
| Job | `ApiRequestLogPartitionJob` (daily) | Creates tomorrow's partition, drops partitions past retention |
| Job | `ApiQuotaResetJob` (hourly) | Rolls quota counters at period boundaries |
| Webhook | none | Pull-only |

### 7.7 Edge cases

| Case | Behaviour |
| --- | --- |
| Client disconnects mid-request | The request is still counted with its actual status if headers were sent; otherwise counted as 499 (client closed) in the raw log and excluded from billing |
| Request rejected by authentication | Counted, attributed to org `NULL`, status class `4xx` |
| Request rejected by the account-state middleware | Counted, attributed to the resolved user and org |
| 429 from a rate limiter | Counted with a distinct `IsThrottled = true` flag on the rollup row |
| A single API key makes 10 000 requests in a minute | All counted; rollup is an upsert so there is no row explosion |
| Process restart mid-batch | Up to one batch (≤500 samples) is lost; `DroppedSamples` is not incremented because the loss is unknowable — documented |
| Clock jump | Windows are derived from the server clock; a backwards jump is detected and logged, and the rollup for the affected hour is recomputed |
| A new route is deployed | It appears in statistics on the next flush; no registration is needed because the route template is read from the endpoint metadata |
| `/internal/*` traffic | Counted and attributed to `OrganizationId = NULL` unless the payload carries an org; excluded from customer billing |

### 7.8 Audit logging

Telemetry is not administrative, so no `AuditLogEntry` is written per request —
that would defeat the purpose. Administrative actions on telemetry configuration
(retention, sampling, quota definitions) do write `AuditLogEntry`. API key
creation and revocation are audited in §5.10, and the raw request log provides
key-level forensics.

### 7.9 Testing requirements

| Level | Cases |
| --- | --- |
| Unit | Bucket assignment and percentile interpolation; route-template extraction for every existing route; sampling decision; quota window arithmetic incl. month-end |
| Integration | A request through the real pipeline lands in the rollup with the right org/user/key; excluded paths are excluded; `X-Request-Id` round-trips; a client-supplied invalid request id is 400; buffer-full drop path increments the counter |
| Contract | Every statistics response matches the OpenAPI schema, including the `null`-percentile case |
| Load | 5 000 req/s sustained for 60 s: the writer keeps up, the buffer does not saturate, and p99 latency increase stays under 1 ms (this is a hard acceptance criterion, BR-6.3/FR-6.3) |
| Data quality | Rollup totals equal the (unsampled) truth within 0 for a synthetic run of exactly 1 000 requests |

---

## 8. Feature area 7 — System statistics

### 8.1 Current state

> **Superseded by Phase 0/6.** The bullets below describe the baseline this plan
> started from. As shipped, `/health` and `/health/ready` share a JSON
> `HealthCheckResponseWriter` and run up to four checks (`database`, `redis`,
> `agent-service`, `clerk-jwks`); the earlier `Program.cs:93` citation was to a
> different line. See [api/README.md §B.12](../api/README.md).

- `GET /health` is mapped `AllowAnonymous` and checks Redis only
  (`Program.cs:93`; `Configuration/EventingConfiguration.cs:24,33`; the only
  check implementation is `Infrastructure/Eventing/RedisHealthCheck.cs`).
- `EventBusMetrics` records published/failed/latency counters and
  `EventingMetricsExporter` logs them as JSON every 30 s
  (`Infrastructure/Eventing/EventBusMetrics.cs`,
  `Infrastructure/Eventing/EventingMetricsExporter.cs:41-47`).
- The .NET API has **no OpenTelemetry SDK** — no OTel packages in
  `Aveline.Api.csproj`. OTLP/Jaeger is wired only to the Python service
  (`docker-compose.yml:155-159,171-194`, `otel-collector-config.yaml`).
- No correlation ID, no `HttpContext.TraceIdentifier` usage.
- No alerting of any kind.

### 8.2 Functional requirements

| ID | Requirement |
| --- | --- |
| FR-7.1 | Split `/health` into `/health/live` (process up, always 200 if the process answers) and `/health/ready` (all dependencies healthy). Keep `/health` as an alias of `/health/ready` for backward compatibility. |
| FR-7.2 | `/health/ready` must check: PostgreSQL (connectivity and a trivial query), Redis, the agent service `/health/ready`, and the Clerk JWKS document (cached with a TTL). Each check reports `name`, `status`, `durationMs`, and a non-sensitive message. |
| FR-7.3 | Deployment metadata must be exposed on `/health/ready` as a `version` block: `gitSha`, `buildTime`, `assemblyVersion`, `environment`. |
| FR-7.4 | The API must expose OpenTelemetry metrics via a Prometheus endpoint at `/metrics`, authenticated by `InternalServicePolicy` or a configured scrape token. |
| FR-7.5 | Process resource metrics must be collected: CPU seconds, working set, GC heap, thread count, thread-pool queue length, and connection-pool in-use/available. |
| FR-7.6 | Queue-depth metrics must be collected for every in-process queue and for every Redis-backed work queue: event-bus publish backlog, telemetry channel depth, notification delivery backlog, inbound WhatsApp message backlog, and agent runs currently in `Running`. |
| FR-7.7 | Database metrics must be collected: active connections, pool saturation, query duration histogram for EF-issued commands, and slow-query count above `Observability:SlowQueryMs` (default 500). |
| FR-7.8 | Cache metrics must be collected: hit/miss counters and operation latency for `IDistributedCache`, plus the existing Redis health. |
| FR-7.9 | Error rates must be exposed as a queryable series over the same data as §7 (status class 5xx) plus unhandled-exception counts. |
| FR-7.10 | Throughput must be exposed as requests per second and agent runs per minute. |
| FR-7.11 | Alert rules must be configurable as data, evaluated on a schedule, and delivered through the existing notification module. |
| FR-7.12 | Alerts must support severity (`Info|Warning|Critical`), cooldown, acknowledgement, and auto-resolution. |
| FR-7.13 | A `GET /api/v1/admin/statistics/system/overview` endpoint must return a single aggregate payload suitable for an operations dashboard. |
| FR-7.14 | A `GET /api/v1/admin/statistics/system/metrics` endpoint must return a time series for one named metric with dimensions. |
| FR-7.15 | A `GET /api/v1/admin/statistics/system/alerts` endpoint must list alerts with filters. |

### 8.3 Data model

**`SystemMetricSample`**, **`SystemAlertRule`**, **`SystemAlert`**. Full columns
in [domain-model.md §8](domain-model.md).

Design note: `SystemMetricSample` stores only metrics that cannot be scraped from
Prometheus (because no Prometheus server is deployed in this project). It is a
**durable fallback**, not a primary store. If the team deploys Prometheus, the
table becomes a 24-hour cache for the dashboard endpoints and can be reduced.

### 8.4 Business rules and validation

| ID | Rule |
| --- | --- |
| BR-7.1 | `/health/live` must never check a dependency; a healthy process with a dead database is still live. |
| BR-7.2 | `/health/ready` returns 503 when any check is `Unhealthy`. `Degraded` checks do not fail readiness but are reported. |
| BR-7.3 | Health check messages must not include connection strings, hostnames, credentials, or stack traces. |
| BR-7.4 | `/metrics` must be authenticated. Prometheus-format output on a public endpoint is an information-disclosure risk. |
| BR-7.5 | Alert rule evaluation is on a fixed interval (`Observability:AlertEvaluationSeconds`, default 60). |
| BR-7.6 | An alert in cooldown does not re-fire; it increments `OccurrenceCount` on the existing alert row instead. |
| BR-7.7 | Alert auto-resolution requires `Observability:AutoResolveConsecutiveOk` (default 3) consecutive evaluations below threshold. |
| BR-7.8 | Metric names follow `aveline.<subsystem>.<measure>` in `snake_case` for the Prometheus exporter and `camelCase` in JSON. Documented in the statistics catalog. |
| BR-7.9 | Metric retention: raw samples 30 days, hourly aggregates 400 days. |
| BR-7.10 | A metric whose value cannot be determined is omitted, never recorded as 0. Recording 0 for an unknown makes dashboards lie. |
| BR-7.11 | Critical alerts also create a `NotificationRecord` for every user with the `owner` or `admin` role, via the existing `IRecipientResolver`. |

### 8.5 Permissions

| Endpoint | Permission |
| --- | --- |
| `/health`, `/health/live`, `/health/ready` | anonymous |
| `/metrics` | `InternalServicePolicy` or scrape token |
| `/api/v1/admin/statistics/system/**` | `stats:system` |
| Alert acknowledgement | `stats:system` |

### 8.6 Events, jobs, and webhooks

| Kind | Name | Trigger |
| --- | --- | --- |
| Event | `system.alert.fired` / `system.alert.acknowledged` / `system.alert.resolved` | Alert lifecycle |
| Job | `SystemMetricCollector` (hosted service, every 30 s) | Samples process, DB, cache, queue, and throughput metrics into `SystemMetricSample` |
| Job | `AlertEvaluationJob` (hosted service, every 60 s) | Evaluates `SystemAlertRule` rows against recent samples |
| Job | `SystemMetricRetentionJob` (daily) | Deletes raw samples older than 30 days |
| Job | `StaleAgentRunJob` (hourly) | Marks `PausedForApproval` runs past timeout as `TimedOut` |
| Webhook | `POST /api/v1/webhooks/alerts` | Optional outbound alert forwarding to an external incident tool |

### 8.7 Edge cases

| Case | Behaviour |
| --- | --- |
| Database down | `/health/ready` returns 503 with the DB check first; the collector cannot write samples, so it buffers in memory up to 100 samples then drops with a counter |
| Redis down | Event bus and cache degrade; readiness returns `Degraded` (not `Unhealthy`) because the API remains partially functional. This is a deliberate, documented deviation from BR-7.2 and is configurable via `Observability:RedisIsCritical` (default `false`) |
| Agent service down | `/health/ready` returns 503 only if `Observability:AgentIsCritical` (default `true`), because no AI workflow can run |
| Clerk unreachable | Readiness `Degraded`: existing JWTs still validate from the cached JWKS until it expires |
| Metric collector throws | Logged and counted; the collector never crashes the host |
| Alert storm | Cooldown plus a per-rule `MaxAlertsPerHour` prevents a storm; excess occurrences aggregate into the open alert |
| Clock skew on containers | All timestamps are UTC from the host clock; Prometheus-visible `process_start_time_seconds` detects restarts |

### 8.8 Audit logging

Alert rule create/update/delete and alert acknowledgement write
`AuditLogEntry`. Metric samples do not.

### 8.9 Testing requirements

| Level | Cases |
| --- | --- |
| Unit | Each health check's healthy/unhealthy/degraded decision; each comparison operator; cooldown arithmetic; auto-resolve counter; metric-name formatting |
| Integration | `/health/ready` with a stopped dependency returns 503; `/metrics` without credentials returns 401; alert firing creates a `NotificationRecord`; auto-resolve after three good evaluations |
| Contract | `/health/ready` payload matches OpenAPI including the `version` block |
| Load | Metric collection adds no more than 0.5 % CPU at 1 000 req/s; alert evaluation over 200 rules completes in under 1 s |
| Failure injection | Kill Redis, kill Postgres, stop the agent container, and 403 the Clerk JWKS endpoint — readiness behaves as specified in §8.7 |

---

## 9. Definition of done — traceability

| Deliverable requirement | Where satisfied |
| --- | --- |
| Every required feature area covered | §3–§8 |
| Every statistic has formula, dimensions, source, exposing endpoint | [statistics-catalog.md](statistics-catalog.md) |
| Every endpoint documented with request/response schemas and auth | [api/openapi.yaml](../api/openapi.yaml), [api/README.md](../api/README.md) |
| Frontend developers can implement without backend questions | [api/README.md](../api/README.md) documents every endpoint including existing ones, with examples |
| Plan detailed enough to estimate and implement | [implementation-plan.md](implementation-plan.md) — module breakdown, migrations, jobs, milestones |
| Assumptions marked | [assumptions-and-open-questions.md](assumptions-and-open-questions.md) and inline `INFERRED`/`ASSUMPTION` markers |

### 9.1 Known limits of this specification

Stated plainly so they are not mistaken for gaps in the reading:

1. **`docs/api/openapi.yaml` is hand-authored, and this conflicts with an
   existing repo rule.** `docs/OpenApi/README.md:27` states *"Never hand-edit the
   exported spec — it is generated from the code."* The generated document at
   `/openapi/v1.json` covers only what is already implemented. This specification
   documents endpoints that do not exist yet, so it cannot be generated. The
   reconciliation procedure is in [api/README.md §1.3](../api/README.md).
2. **`Aveline.Api/Endpoints/VisualEndpoints.cs` registers 27 mappings against
   13 handlers**, including three overlapping internal prefixes. Not all of them
   are documented in [api/README.md](../api/README.md) — the canonical ones and
   the duplicates are listed explicitly there rather than omitted silently.
3. **Frontend behavior is out of scope.** Where an endpoint exists only to serve
   a screen, that is noted, not designed.
4. **`backend/Aveline.Domain`, `backend/Aveline.Application`, and
   `backend/Aveline.Infrastructure` are empty scaffolding** and are not part of
   the runtime. `Program.cs` references none of them. This plan targets
   `Aveline.Api/Modules/`, consistent with `ADR-001` and the actual code.
