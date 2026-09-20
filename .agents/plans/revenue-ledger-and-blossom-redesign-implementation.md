# Revenue ledger, money statistics, and Blossom ledger redesign — implementation plan

**Status:** approved (revision 1, FINAL).
**Owner workstream:** Slice 3 — Commerce Validation & Optimization.
**Companions:**
`docs/frontend/admin-console.md` (the console's design record),
`docs/backend/statistics-catalog.md` (S-catalog),
`docs/api/README.md` (endpoint catalog).

This plan is the design record. The executable work is the seven GitHub issues
**R0 … R6**, whose bodies are drafted under `.agents/plans/issues/`. Where an issue
body and this plan disagree, this plan wins.

---

## 1. Objective

Deliver, on the admin console at `/admin/:userId/*`:

1. a **Revenue** surface — an append-only income ledger plus a payments/financial
   statistics surface; and
2. a **redesigned Blossom ledger** whose page is genuinely wired to the backend
   rather than being a form plus a manual fetch.

Every phase is built test-first, and every phase is tracked by one GitHub issue.

### Success criteria

- Money is recorded once, from a stated source, and can be reconciled: the ledger's
  totals can be checked against the rows they claim to summarise.
- The console never invents a number. An unverifiable revenue figure renders as
  *derived* or *verified*, or as *not measured* — never as a confident total.
- The Blossom ledger page answers the real operator question — *"this boutique says
  a grant vanished"* — in one place: open the statement, filter, find the entry,
  act on it inline, with the reconciliation banner always visible.
- Every pre-existing blocking gate stays green **unchanged**:
  `admin-conformance.test.ts`, `admin-truthfulness.test.ts`,
  `admin-prometheus-boundary.test.ts`, `admin-install.test.ts`,
  `permissions.sync.test.ts`, `role-policies.sync.test.ts`, `metrics.test.ts`
  (`V1…V11` pin), `routes.test.ts`.
- `dotnet test` and `bun run test` green; `bunx tsc -b` exit 0; `bun run lint`
  clean; the admin coverage ratchet raised, never lowered.

---

## 2. Decision record

| # | Decision | Rationale |
|---|---|---|
| **D1** | "Income" means **Aveline's own revenue** — paid plan subscriptions and Blossom top-up packs. Not boutique customer sales. | The console is the Aveline team's internal tool (`admin-console.md` Q2). `Payment` is an `ITenantEntity` reaching `/api/v1/orgs/{organizationId}/payments` only, so it is a boutique's sales record. The two economies must not share one ledger. |
| **D2** | The income ledger is a **new append-only table**, not a projection. | It is the revenue journal. Refunds and corrections are new compensating rows, exactly as `BlossomLedgerEntry` already works. A projection can never reconcile and cannot hold an adjustment. |
| **D3** | A **new financial statistics sub-domain**, sourced from Aveline's own Postgres tables. | DR-1 in `admin-console.md` settles that the console may own a Postgres business time series. It never touches Prometheus, which is what `admin-prometheus-boundary.test.ts` enforces. |
| **D4** | **No fabricated revenue.** The ledger has two entry classes — **derived** (what a period's list price says should be billed) and **verified** (money an operator, or later a provider, confirmed) — and the UI must always say which. | There is no payment-provider client in this repo. `OrganizationSubscription`/`BlossomPriceEntry` carry provider columns that nothing writes, and `docs/api/README.md:1278` states it: *"Phase 3 attaches a payment provider; until then a top-up is a recorded grant, not a charge."* Booking income at the top-up site would invent revenue. |
| **D5** | Blossom statement **paging and filtering move server-side**. The reconciliation **formula** is not touched. | The statement merges `BlossomLedgerEntries` ∪ `AiUsageRecords`; it currently materialises the whole window and pages in memory. `LedgerDerivedBalance`/`ReconciliationDrift` are shared with `SystemMetricCollector` and are an alerting contract (BR-2.14, S-30), so changing them would move the alarm. |

---

## 3. Grounding facts

Every claim below was verified by inspection before this plan was written.

| Fact | Evidence |
|---|---|
| Payments are boutique-scoped, and admin payment endpoints do not exist. | `Modules/Commerce/Models/Payment.cs` (requires `OrderId`, `ITenantEntity`); `Controllers/PaymentsController.cs:11` routes under `/api/v1/orgs/{organizationId:guid}/payments`; no `admin` string appears anywhere in `Modules/Commerce`. |
| No income concept exists. | `grep -rniE "\bincome\b"` over `Aveline.Api` and `Aveline.Api.Tests` returns **zero**. `revenue` appears only as `BlossomRevenue`/`TotalBlossomRevenue`, which are Blossom **units**. |
| Subscription price is never assigned. | `SubscriptionService.UpsertSubscriptionAsync` (`:299-340`) sets tier, seats, status and period; `PriceLkr` is never written and stays `0m`. |
| The API is .NET 10. | `Aveline.Api.csproj` and `Aveline.Api.Tests.csproj` target `net10.0`; EF Core / AspNetCore `10.0.12`; CI pins `dotnet-version: "10.0.x"`. |
| 25 permissions, 9 roles, mirrored in the client and enforced by sync tests. | `Authorization/Permissions.cs:18-94`; `frontend/web/src/lib/admin/permissions.ts`; `permissions.sync.test.ts` parses the C# from source. |
| The registry does not mount routes. | `lib/admin/routes.ts` feeds only `AdminSidePanel` and `AdminHeader`; `App.tsx:79-100` is a separate hand-written route block. |
| The Blossom statement pages in memory. | `BlossomService.GetStatementAsync` reads with `page: 1, pageSize: int.MaxValue` (`:272`), then `Skip/Take`s the in-memory list (`:312-316`). Clamp is `[1, 200]`, window cap 92 days. |
| Consumption rows carry almost nothing. | `BlossomStatementItem` has no provider, model, normalized-unit or cost field; consumption rows are `Kind="Consumption"`, `Reason="Agent workflow"`, `SourceRef=workflowId`. |
| `AdminBlossoms.tsx` has no test. | No `AdminBlossoms.dom.test.tsx` exists; `components/admin/admin` coverage floor is lines 28. |
| The admin coverage ratchet is not gated in CI. | `ci.yml` runs `bun run test:coverage` only, whose glob-scoped thresholds exclude `src/routes/admin/**` and `src/components/admin/**`. |

---

## 4. New statistics catalog entries

Added to `docs/backend/statistics-catalog.md` in R0. Signatures as landed:

| Id | Name | Endpoint | Access |
|---|---|---|---|
| **S-50** | `revenueLedger` | `GET /api/v1/admin/revenue/ledger` | `revenue:read` |
| **S-51** | `revenueAccounts` | `GET /api/v1/admin/revenue/accounts` | `revenue:read` |
| **S-52** | `revenueOverview` (`mrr`, `arr`, `arpu`, `payingOrganizations`) | `GET /api/v1/admin/statistics/revenue/overview` | `revenue:read` |
| **S-53** | `revenueTimeseries` | `GET /api/v1/admin/statistics/revenue/timeseries` | `revenue:read` |
| **S-54** | `revenueCollections` | `GET /api/v1/admin/statistics/revenue/collections` | `revenue:read` |
| **S-55** | `revenueBlossomSales` | `GET /api/v1/admin/statistics/revenue/blossoms` | `revenue:read` |
| **S-56** | `billingReconciliation` | `GET /api/v1/admin/statistics/billing/reconciliation` | `stats:system` |

---

## 5. Subsystem changes

### 5.1 Backend — revenue module (`Aveline.Api/Modules/Revenue/`)

**Domain**

- `Enums.cs` — `IncomeEntryKind` (`SubscriptionCharge`, `TopUpPurchase`, `Refund`,
  `Adjustment`); `IncomeSourceKind` (`SubscriptionBilling`, `BlossomTopUp`, `Admin`,
  `System`); `IncomeChargeBasis` (`Derived`, `Verified`); `IncomeEntryStatus`
  (`Recorded`, `Voided`).
- `Models/IncomeLedgerEntry.cs` — `Id` (`Guid.CreateVersion7()`, matching
  `BlossomLedgerEntry`), `OrganizationId`, `Kind`, `SourceKind`, `SourceRef` (the
  dedup identity), `ChargeBasis`, `Status`, `Currency` (`"LKR"`), `Amount`
  (`decimal(18,2)`, **always positive — the sign is derived from `Kind`**), `Reason`
  (10–500 chars, the Blossom rule), `PeriodStart`/`PeriodEnd` (`null` for
  `TopUpPurchase`/`Refund`/`Adjustment`), `OccurredAt`, `RecordedByUserId`,
  `SupersedesEntryId`, `IdempotencyKey`/`IdempotencyScope`.
- `Models/RevenueDomainExceptions.cs` — `RevenueDomainException` base +
  `RevenueValidationException` (400), `IncomeLedgerEntryNotFoundException` (404),
  `RevenueOrganizationNotFoundException` (404), `DuplicateRevenueEntryException`
  (409 `code=duplicate-revenue-entry`), `IncomeEntryNotVoidableException` (409).
- `Domain/RevenueReconciliation.cs` — pure: `DerivedTotal`, `VerifiedTotal`,
  `UnverifiedGap = DerivedTotal − VerifiedTotal`, `RefundTotal`,
  `NetVerified = VerifiedTotal − RefundTotal`, `IsBalanced`.

**Persistence**

- `Infrastructure/Data/Configurations/RevenueConfigurations.cs` — table
  `IncomeLedgerEntries`; `decimal(18,2)` on `Amount`; enum-as-string max 32;
  a **filtered unique** index on `(SourceKind, SourceRef)` as the dedup identity,
  mirroring the Blossom ledger's filtered unique idempotency index; indexes
  `(OccurredAt desc)`, `(OrganizationId, OccurredAt desc)`, `(Kind, OccurredAt desc)`;
  a check constraint `Amount > 0` declared in the model so its name is assertable.
- Migration `AddIncomeLedger` (`dotnet ef migrations add`).
- `IncomeLedgerService` owns every rule, so no write site re-implements one:
  `Amount > 0`; `Reason` 10–500; a `Verified` entry may not share
  `(SourceKind, SourceRef)` with a live `Derived` entry unless it nulls it; every
  write records `RecordedByUserId` (the console refuses to write an unauditable
  entry with a null actor, as `AdminOrganizationEndpoints` already does for
  entitlement overrides).

**Generic ledger paging** — `Aveline.Api/Modules/Shared/LedgerPageRequest.cs`, one
small value object with a single `Normalize()` clamping `Page ≥ 1` and `PageSize` to
`[1, 200]`, shared by the Blossom and revenue readers so a second ad-hoc clamp cannot
appear.

### 5.2 Backend — revenue write paths

| Source | Where | Basis |
|---|---|---|
| Blossom top-up purchase | `BlossomEndpoints` `POST /orgs/{organizationId}/blossoms/top-ups` — **only when `PaymentReference` is present**, which the endpoint already checks | `Derived` |
| Subscription period charge | `BillingPeriodRolloverJob`, beside the `PeriodAllocation` write, for an `Active` subscription with `PriceLkr > 0` | `Derived` |
| Verified receipt | new `POST /admin/revenue/ledger/verify` | `Verified` |
| Refund | new `POST /admin/revenue/ledger/refund` | `Verified` |
| Adjustment / correction | new `POST /admin/revenue/ledger/adjust`, optionally nulling an entry via `SupersedesEntryId` | `Verified` |

Every write is idempotency-guarded through the existing `IdempotencyEndpointFilter`
(`Idempotency-Key` required; fails closed with 503 when the lease store is down).

### 5.3 Backend — revenue read API

`Aveline.Api/Modules/Revenue/Endpoints/RevenueEndpoints.cs`:

| Method | Route | Returns |
|---|---|---|
| `GET` | `/api/v1/admin/revenue/ledger` | `IncomeLedgerPageDto` — filters `from`, `to`, `kind`, `sourceKind`, `chargeBasis`, `q`, `page`, `pageSize`; the page carries the window totals and the reconciliation block |
| `GET` | `/api/v1/admin/revenue/accounts` | `RevenueAccountsDto` — per-organization derived/verified/net over the window, with `PriceLkr = 0` surfaced as a data-quality note rather than a zero that reads as "free" |
| `POST` | `/api/v1/admin/revenue/ledger/verify` \| `/refund` \| `/adjust` | `IncomeLedgerEntryDto` |
| `GET` | `/api/v1/admin/statistics/revenue/overview` | `RevenueOverviewDto` |
| `GET` | `/api/v1/admin/statistics/revenue/timeseries` | `RevenueTimeseriesDto` |
| `GET` | `/api/v1/admin/statistics/revenue/collections` | `RevenueCollectionsDto` |
| `GET` | `/api/v1/admin/statistics/revenue/blossoms` | `RevenueBlossomSalesDto` |

- `IncomeDataQualityDto` is **a fifth deliberate vocabulary**, not a reuse of the
  business one. A field is named only where it means something specific here:
  `RevenueProviderSettlementAvailable`, `SubscriptionPricesConfigured`,
  `DerivedEntriesUnverified`, `CheckedAt`, `Notes`.
- House patterns exactly: `.Produces<T>(200).WithSummary(...)`, `Results.Ok/BadRequest/
  Conflict/NotFound`, **no `[AsParameters]`**, **no `TypedResults`**,
  `Cache-Control: private, max-age=…` through an `IDistributedCache` cache modelled on
  `BusinessKpiCache` (per-key single-flight, both cache reads and writes swallowed so
  an outage degrades to an uncached query and never to a failed request, and the
  degradation surfaced as a data-quality note).
- New error-envelope codes: `duplicate-revenue-entry`, `income-entry-not-voidable`,
  and `revenue-provider-unavailable` reserved for a future provider path.

### 5.4 Backend — authorization

Three new constants added to `Aveline.Api/Authorization/Permissions.cs` **and**
`Permissions.All`:

| Constant | Value | Granted to |
|---|---|---|
| `RevenueRead` | `revenue:read` | `admin`, `owner`, `moderator` |
| `RevenueManage` | `revenue:manage` | `admin`, `owner` |
| `RevenueRefund` | `revenue:refund` | `owner` |

`moderator` already holds `analytics:business:read`; reading revenue is the same class
of read and is inside their remit. **Posting or refunding money is not**, and stays
team-only, consistent with the standing rule that boutique roles never hold
money-shaped permissions.

The client mirror in `frontend/web/src/lib/admin/permissions.ts` (the `Permission`
union, `ALL_PERMISSIONS`, and `ROLE_PERMISSIONS.moderator`) is updated in the same
commit so `permissions.sync.test.ts` stays green without being edited beyond its
count assertion. Adding a permission automatically registers a matching policy
(`AuthorizationConfiguration.cs:280-283`), and `PermissionsCatalogTests` enforces that
each new permission has at least one role grant.

### 5.5 Backend — Blossom ledger (`Aveline.Api/Modules/Billing/`)

| Change | Detail |
|---|---|
| Server-side statement paging | Replace the full-window materialise-then-`Skip/Take` with an ordered, paged read across the merged ledger ∪ consumption source, returning the true `Total`. Asserted by draining > 200 rows across pages and seeing each row exactly once. |
| Filters | Add `sourceKind`, `q` (reason / `sourceRef` contains) and amount `min`/`max` to `GET .../blossoms/statement`, beside the existing `entryType` and `page`/`pageSize`. The existing `kind` parameter keeps its meaning. |
| Richer consumption rows | Add nullable `Provider`, `Model`, `NormalizedUnits`, `ActualCostUsd` to `BlossomStatementItem`, so a consumption line can be explained instead of being `"Agent workflow"`. |
| Window presets | `MaxWindowDays` 92 → configurable `Billing:StatementMaxWindowDays` (default **400**), matching the retention window the S-catalog already claims. The response states the **effective** limit so a wide request is reported, never silently clamped. |
| Statement `dataQuality` | `BlossomStatementDataQualityDto`: `ReconciliationChecked`, `OpeningBalanceFromProjection` (the opening balance is derived backwards from the cached `BlossomRemaining`, which must be *stated*, not implied), `WindowCapped`, `Notes`. |
| Revocability | The statement marks a grant row with its remaining revocable quantity, so the redesign can offer an action instead of a 409. The `grant-not-revocable` 409 and its `availableToRevoke` payload remain the server's authoritative answer. |
| Cross-org drift | New `GET /api/v1/admin/statistics/billing/reconciliation` (S-56) over the per-account drift `SystemMetricCollector` already computes, so a Critical drift is findable from the console rather than only from Grafana. |

### 5.6 Backend — audit

`blossom.ledger.*` audit rows already exist. Add
`revenue.ledger.{verified|refunded|adjusted}` through the same `IAuditService`, so the
revenue journal is visible in the Audit Explorer, and assert the action names in an
integration test.

### 5.7 Frontend — the `money` domain

`lib/admin/routes.ts` gains `money` in `AdminDomain` + `ADMIN_DOMAINS` and **four**
entries (the registry requires ≥ 2 per domain):

| id | subPath | label | domain | gate |
|---|---|---|---|---|
| `revenue` | `revenue` | Revenue | money | `role: MoneyRead` |
| `revenue-ledger` | `revenue/ledger` | Income Ledger | money | `role: MoneyRead` |
| `revenue-stats` | `revenue/statistics` | Payments Statistics | money | `role: MoneyRead` |
| `blossoms` (moved) | `blossoms` | Blossom Ledger | money | `role: MoneyOperations` |

`lib/admin/role-policies.ts` gains `MoneyRead` (`owner`, `admin`, `moderator`) and
`MoneyOperations` (`owner`, `admin`), each mirroring a new `RequireRole(...)`
registration in `AuthorizationConfiguration.cs`; `role-policies.sync.test.ts` is
updated in the same commit. `routes.test.ts` gains the money-domain block (ids, gates,
`declaredPermissions()`), following the existing business-domain precedent.

`App.tsx` gains the four `<Route>` children. Because **nothing** tests
registry↔router correspondence today, R0 adds that test: every `enabled` registry entry
must have a matching `<Route path=...>` in `App.tsx`.

### 5.8 Frontend — Blossom ledger redesign

Replace `AdminBlossoms.tsx` (336 lines, ad-hoc `useState` loaders, unbounded `Input`
for a GUID entry id, statement loaded only by pressing a button):

- **The statement is the spine, not a side panel.** `OrgPicker` chooses the
  organization; the statement then loads and pages through TanStack Query with
  `staleTime: 0` (the balance must never be cached, as the current code already
  documents) and `keepPreviousData`, with `Pagination` bound to the server's `Total`.
- **Filters live in the URL** through the existing `lib/admin/query-params.ts`:
  `entryType`, `sourceKind`, window preset, `q`, `page`, `pageSize`. A filtered view
  survives the back button and is shareable.
- **Header block**: period, opening, closing, requested versus effective window, and
  the summary counts.
- **`BalanceTimeline`** — ledger entries and consumption events on one axis, so a
  discontinuity is visible at a glance instead of inferred from a table.
- **Grant operations move onto the row.** `Revoke` stops being a typed GUID and
  becomes an inline action on an eligible grant row, pre-gated on the revocability the
  statement now reports; `Credit`/`Debit` keep their own form. All three keep
  `IdempotentActionButton` and its single-flight guarantee.
- **Reconciliation banner** stays prominent and now also renders `dataQuality`. An
  unavailable reconciliation reads *"reconciliation status unknown"*, never
  "consistent".
- **Cross-org drift strip** from the new S-56 endpoint.
- **A11y**: the statement table goes through `DataTable`, which owns loading, empty,
  error-with-retry and `aria-sort`, so the page cannot invent its own states.

### 5.9 Frontend — revenue pages

- `routes/admin/AdminRevenue.tsx` — the domain landing page: three headline `KpiTile`s
  and links to the two children.
- `routes/admin/AdminRevenueLedger.tsx` — the register: `DataTable` + `Pagination`,
  URL-backed filters, reconciliation block, `dataQuality` notice, and `Verify` /
  `Refund` / `Adjust` actions gated per permission and each carrying an
  `Idempotency-Key`. A `Derived` row is visually **and** textually distinct from a
  `Verified` row, and the register never sums the two into one unlabelled total.
- `routes/admin/AdminRevenueStats.tsx` — four independent `useEndpoint` reads with
  per-endpoint failure isolation (the established reason `AdminBusinessGrowth` uses
  `LoadState<T>` rather than one combined query): MRR/ARR, revenue timeseries
  (`TimeSeriesChart`), collection rate, Blossom pack sales (`HorizontalBarChart`).
- `lib/admin/revenue-series.ts` — pure bucket/gap/`isPartial` shaping, modelled on
  `business-series.ts`; and `lib/admin/revenue-quality.ts` for the fifth `dataQuality`
  vocabulary. Both unit-tested, with `revenue-series.ts` at 100 % lines.

### 5.10 Conformance obligations for every new admin file

No raw palette classes or hex; no lowercase `<select>`/`<input>`/`<button>`/`<table>`/
`<hr>`; `gap-*` not `space-*`; every Recharts `<Line>`/`<Area>` carries
`connectNulls={CONNECT_NULLS}`; no inline `<svg>`; no `aveline_`/`pg_` string;
`recharts` stays `^3.10.1`. The `admin-conformance.test.ts` allow-list stays exactly
one entry.

---

## 6. TDD obligation

Red-green-refactor for every task. The issue bodies name, per phase, the test file to
write **before** each production file.

- **Backend red**: write the failing test, run
  `dotnet test Aveline.Api.Tests/Aveline.Api.Tests.csproj --filter "FullyQualifiedName~<Suite>"`,
  confirm it fails **for the expected reason**, then write the minimal implementation.
  Integration suites use `IAsyncLifetime` + `StubAuthServer` +
  `WebApplicationFactory<Program>` + the shared `"AvelineInMemoryDb"` seed. Migration,
  check-constraint and filtered-index assertions use Testcontainers
  `pgvector/pgvector:pg16`. Service suites use a per-test unique InMemory context.
- **Frontend red**: write the failing test, run `bun run test -- <path>`
  (`*.dom.test.tsx` for anything that renders React), confirm it fails, then implement.
- **No production file in this plan may be written before its test exists and has been
  observed to fail.** The single `dotnet ef migrations add` is the only generated
  artifact; its model-level assertions are written first.
- Test-only code lives in test utilities (`src/test/**`, `Aveline.Api.Tests/*Helpers*`),
  never in production classes.
- Assertions are made on real behaviour, never on mock behaviour.

---

## 7. GitHub issues

All seven issues are open. Briefs are kept in
`.agents/plans/issues/revenue-p0.md` … `revenue-p6.md`; the GitHub body is the brief
with its leading heading stripped.

| Issue | Phase | Scope | Depends on |
|---|---|---|---|
| **R0** [#341](https://github.com/KavinduNirmal/aveline/issues/341) | Foundations and the honesty contract | D1–D5 recorded; `IncomeDataQualityDto` vocabulary; S-50…S-56 in the catalog; the three permissions in C# **and** the client mirror (both sync tests updated together); the `money` domain and its two role policies with `role-policies.sync.test.ts`; the registry↔router correspondence test. **No UI, no endpoints.** | — |
| **R1** [#342](https://github.com/KavinduNirmal/aveline/issues/342) | The income ledger schema | Entity, enums, EF configuration, migration, filtered unique dedup index, `Amount > 0` check constraint, `RevenueReconciliation`, `IncomeLedgerService` validation rules. Testcontainers model assertions. | R0 |
| **R2** [#343](https://github.com/KavinduNirmal/aveline/issues/343) | Write paths | Top-up writer (guarded on `PaymentReference`), the `BillingPeriodRolloverJob` derived charge, the three admin write endpoints with `Idempotency-Key`, `revenue.ledger.*` audit actions, and the rule that a `Verified` entry nulls its `Derived` counterpart. | R1 |
| **R3** [#344](https://github.com/KavinduNirmal/aveline/issues/344) | Read API and revenue statistics | Paged ledger read, accounts read, four statistics reads, `IDistributedCache` cache, `dataQuality`, API docs + OpenAPI. | R1 |
| **R4** [#345](https://github.com/KavinduNirmal/aveline/issues/345) | Blossom ledger backend | Server-side statement paging, new filters, consumption-row detail, configurable window with the effective limit returned, statement `dataQuality`, revocability in the statement, cross-org reconciliation endpoint. | R0, R1 |
| **R5** [#346](https://github.com/KavinduNirmal/aveline/issues/346) | Frontend foundation and the Blossom redesign | api.ts + types; the four money-domain pages; redesigned `AdminBlossoms` with `BalanceTimeline`, URL-backed filters and inline revoke; the drift strip. | R3, R4 |
| **R6** [#347](https://github.com/KavinduNirmal/aveline/issues/347) | Revenue pages, documentation and the ratchet | Ledger register, statistics page, `revenue-series.ts`/`revenue-quality.ts`; `docs/frontend/admin-console.md`; Playwright signed-out coverage for the four new routes; `docs/deployment.md`; coverage ratchet raised; **a new `bun run test:coverage:admin` CI step** in `ci.yml`. | R5 |

R2 and R4 may proceed in parallel once R1 lands.

---

## 8. Edge cases and failure modes

| Case | Required behaviour |
|---|---|
| `PriceLkr = 0` | A derived charge of 0 renders as *"no list price configured"*. Never as free revenue, and never as MRR. |
| Duplicate revenue | A retried top-up must not double-book. The filtered unique `(SourceKind, SourceRef)` index is the last line of defence; `IdempotencyEndpointFilter` is the first. |
| Refund of an unverified charge | A refund against a `Derived` entry with no `Verified` counterpart is a **409 with a code**, not a negative total. |
| Cross-period top-up | `Billing:AllowCrossPeriodTopUps` changes expiry, not revenue recognition. A top-up with no `PaymentReference` writes **no** income row at all. |
| Reconciliation drift | Drift is Critical and already alerts. The console renders it, and never presents a balance as authoritative while `isConsistent` is false or unknown. |
| Cache unreachable | Degrades to an uncached query with a data-quality note. Never a failed request, never a stale "verified" total. |
| Empty ledger on day one | Every KPI has a stated empty state, not a division by zero. |
| Window larger than the effective maximum | The response reports the effective limit; the request is not silently clamped. |
| Idempotency lease store down | `IdempotencyEndpointFilter` **fails closed** (503 `idempotency-unavailable`); the console offers an explicit retry and never auto-retries. |
| Statement row appears twice across pages | Failure mode of a naive merge. Pinned by the > 200-row drain test in R4. |

---

## 9. Assumptions

1. "Payments" in the request means Aveline's revenue events, not a cross-org browser of
   boutique `Payments` rows (D1, confirmed with the requester).
2. No payment-provider integration is in scope. The provider columns stay unwritten and
   the console says so. The ledger is designed so a provider writer is an **added**
   `ChargeBasis.Verified` writer, not a redesign.
3. The revenue ledger is a new table. The Commerce `Payments`/`Orders` tables are left
   alone, including their existing defect that `RefundPaymentAsync` discards `reason`
   and that `OrdersController` names its route parameter `orgId` rather than
   `organizationId`. Both are recorded here as out-of-scope observations.
4. `moderator` may read revenue but may not post or refund it.
5. Branching follows `docs/git-flow.md`: `feature/*` from `development`. Work starts on
   a fresh `feature/slice3-revenue-ledger-and-blossom-redesign` rather than continuing
   `feature/admin-frontend-ui-v3`.
6. The Blossom reconciliation **formula** is out of scope; only its presentation,
   paging, filtering and discoverability change.
7. A Playwright authenticated walk needs a Clerk test session this environment does not
   have. R6 delivers signed-out coverage for the new routes, matching the existing
   `console-access.spec.ts` precedent, and states the gap rather than implying it.

---

## 10. Local verification

```bash
cd frontend/web
bun run test && bunx tsc -b && bun run lint
bun run test:coverage && bun run test:coverage:admin

dotnet test Aveline.Api.Tests/Aveline.Api.Tests.csproj
# targeted while in the red-green loop:
dotnet test Aveline.Api.Tests/Aveline.Api.Tests.csproj --filter "FullyQualifiedName~<Suite>"
```

Testcontainers suites need a running Docker daemon, and fail rather than skip without
one, so R1's migration assertions must run before R1 is closed.
