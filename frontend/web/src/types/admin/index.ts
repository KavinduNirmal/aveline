import type { AccountState, ContactPreferences } from "@/types/user"

export interface AuthClaims {
  userId: string
  email: string | null
  roles: string[]
  claims: Record<string, string[]>
  account: {
    hasCompletedOnboarding: boolean
    accountState: AccountState | null
    userRole: string | null
    organizationRole: string | null
  }
}

export interface AdminUserDto {
  id: string
  clerkId: string
  email: string
  firstName: string
  lastName: string
  displayName: string | null
  username: string
  phoneNumber: string
  address: string | null
  profileImageUrl: string | null
  userRole: string
  organizationRole: string
  organizationId: string
  hasCompletedOnboarding: boolean
  accountState: AccountState
  contactPreference: ContactPreferences
  pushNotificationsEnabled: boolean
  isActive: boolean
  createdAt: string
  updatedAt: string
}

export interface PagedUsers {
  items: AdminUserDto[]
  page: number
  pageSize: number
  total: number
}

export interface ChangeUserStateRequest {
  accountState: AccountState
  reason?: string | null
}

export type PlanTier = "Seed" | "Bloom" | "Orchid" | "Rose" | "Enterprise"

/**
 * The wire shape of `AdminOrganizationDtos.cs:6-15`. Note what is **not** here:
 * `ownerEmail` and `memberCount` are not on the wire, while `clerkOrgId`/`ownerUserId` are.
 * The delivered type had it exactly backwards.
 */
export interface AdminOrganizationDto {
  id: string
  name: string
  slug: string
  clerkOrgId: string | null
  ownerUserId: string
  planTier: PlanTier
  isActive: boolean
  createdAt: string
  updatedAt: string
}

export interface PagedAdminOrganizations {
  items: AdminOrganizationDto[]
  page: number
  pageSize: number
  total: number
}

/** The JSON value shapes `EntitlementOverrideService` accepts for a given `valueType`. */
export type EntitlementOverrideValue = number | boolean | string

/**
 * Mirrors `EntitlementOverrideDtos.cs:12-18`. `value` is a raw `JsonElement` server-side and
 * is validated against `valueType`, so a string-typed field rejects an `Integer` override
 * with a `400`. Both dates are optional and default server-side.
 */
export interface EntitlementOverrideInput {
  key: string
  valueType: string
  value: EntitlementOverrideValue
  reason: string
  effectiveFrom?: string | null
  effectiveTo?: string | null
}

export interface SetEntitlementOverridesRequest {
  overrides: EntitlementOverrideInput[]
}

/**
 * The three Blossom verbs bind three **different** request records
 * (`BlossomDtos.cs:69-78`), which is why they cannot share one body type:
 * `CreditBlossomsRequest`, `DebitBlossomsRequest(Amount, Reason, AllowNegative)` and
 * `RevokeBlossomsRequest(Guid LedgerEntryId, string Reason)`.
 */
export type BlossomSourceKind = string

export interface CreditBlossomsRequest {
  amount: number
  reason: string
  expiresAt?: string | null
  sourceKind?: BlossomSourceKind | null
  sourceRef?: string | null
}

export interface DebitBlossomsRequest {
  amount: number
  reason: string
  allowNegative: boolean
}

export interface RevokeBlossomsRequest {
  ledgerEntryId: string
  reason: string
}

export interface AuditLogEntry {
  id: string
  occurredAt: string
  organizationId: string | null
  actorKind: string
  actorUserId: string | null
  actorRef: string | null
  action: string
  entityType: string
  entityId: string
  reason: string | null
  requestId: string | null
  before: unknown | null
  after: unknown | null
}

export interface PagedAuditLogEntries {
  items: AuditLogEntry[]
  page: number
  pageSize: number
  total: number
}

export interface AdminApprovalRequestSummary {
  id: string
  clerkUserId: string
  email: string
  firstName: string
  lastName: string
  status: "Pending" | "Approved" | "Rejected"
  requestedAt: string
  reviewedAt?: string | null
  reviewedByClerkUserId?: string | null
}

export interface SystemVersionDto {
  gitSha: string
  buildTime: string
  assemblyVersion: string
  environment: string
}

export interface ReadinessCheckDto {
  name: string
  status: string
  durationMs: number
  message: string | null
}

export interface ReadinessDto {
  status: string
  checks: ReadinessCheckDto[]
}

/**
 * The alerts-list row (`SystemStatisticsDtos.cs:16`). It **does** carry `ruleName`, which is
 * exactly why it must not be reused for the acknowledge response.
 */
export interface SystemAlertDto {
  id: string
  ruleId: string | null
  ruleName: string | null
  organizationId: string | null
  metricName: string
  severity: string
  status: string
  title: string
  detail: string | null
  observedValue: number | null
  threshold: number | null
  occurrenceCount: number
  firedAt: string
  lastObservedAt: string
  acknowledgedAt: string | null
  resolvedAt: string | null
}

/**
 * What `POST …/alerts/{id}/acknowledge` actually returns: the **EF entity**
 * (`SystemStatisticsEndpoints.cs:140`), which has no `ruleName` and carries six extra
 * mutable fields. Patching by `id` must read only `status`/`acknowledgedAt`.
 */
export interface SystemAlertAckResponse {
  id: string
  ruleId: string | null
  organizationId: string | null
  metricName: string
  severity: string
  status: string
  title: string
  detail: string | null
  observedValue: number | null
  threshold: number | null
  occurrenceCount: number
  consecutiveOkCount: number
  firedAt: string
  lastObservedAt: string
  acknowledgedAt: string | null
  acknowledgedByUserId: string | null
  resolvedAt: string | null
  resolvedByUserId: string | null
  resolutionNote: string | null
  notificationRecordId: string | null
}

export interface SystemOverview {
  version: SystemVersionDto
  readiness: ReadinessDto
  uptimeSeconds: number
  alerts: {
    critical: number
    warning: number
    top: SystemAlertDto[]
  }
  throughput: {
    requestsPerSecond: number | null
    agentRunsPerMinute: number | null
    blossomsPerHour: number | null
    omitted: string[]
  }
  errors: {
    errorRate: number | null
    requestCount: number
    errorCount: number
    windowSize: string
    unhandledExceptionsMeasured: boolean
    omitted: string[]
  }
  queues: {
    telemetryChannelDepth: number | null
    eventBusBacklog: number | null
    notificationBacklog: number | null
    inboundMessageBacklog: number | null
    agentRunsRunning: number | null
    omitted: string[]
  }
  omitted: string[]
  generatedAt: string
}

export interface SystemMetricPoint {
  windowStart: string
  value: number | null
}

export interface SystemMetricSeries {
  metric: string
  unit: string | null
  windowSize: string
  points: SystemMetricPoint[]
  dataQuality: {
    points: number
    measured: boolean
    omitted: string[]
  }
}

export interface SystemAlertPage {
  items: SystemAlertDto[]
  page: number
  pageSize: number
  total: number
}

/** The agent family's five instrumentation flags (`AgentStatisticsDtos.cs:11-16`). */
export interface AgentDataQualityDto {
  latencyInstrumented: boolean
  nodeFailuresObserved: boolean
  perStepAttribution: boolean
  toolInstrumented: boolean
  costInstrumented: boolean
}

/** `AgentOverviewDto` (`AgentStatisticsDtos.cs:256-263`). `successRate` is null when unmeasured. */
export interface AgentOverviewDto {
  totalRuns: number
  running: number
  pausedForApproval: number
  succeeded: number
  failed: number
  successRate: number | null
  dataQuality: AgentDataQualityDto
}

/** `PricingRecomputeResult` (`IPricingService.cs:56-60`). A zero-effect run is visible here. */
export interface PricingRecomputeResult {
  ruleId: string
  processedRecords: number
  affectedOrganizations: number
  totalDelta: number
  recomputedAt: string
}

/**
 * `BlossomStatementItem` (`IBlossomService.cs`).
 *
 * The five trailing fields were added in Revenue Ledger R4 (issue #345). The reason is that a
 * consumption row previously carried a workflow id and the words "Agent workflow", so an operator
 * asking where 400 Blossoms went had nothing to read. The consumption-only fields are `null` on an
 * entitlement row, and `availableToRevoke` is `null` on anything that is not a revocable grant —
 * absent rather than `0`, because "how much can be revoked" has no answer for a non-grant.
 */
export interface BlossomStatementItem {
  id: string
  occurredAt: string
  kind: string
  entryType: string | null
  blossomDelta: number
  balanceAfter: number
  reason: string
  sourceKind: string | null
  sourceRef: string | null
  expiresAt: string | null
  createdByUserId: string | null
  /** Present only on a revocable grant. The `409 grant-not-revocable` remains authoritative. */
  availableToRevoke?: number | null
  provider?: string | null
  model?: string | null
  /** Input + output + cached tokens, so a Blossom charge can be checked rather than trusted. */
  normalizedUnits?: number | null
  actualCostUsd?: number | null
}

/** `BlossomStatementReconciliation` (`IBlossomService.cs:71-72`). */
export interface BlossomStatementReconciliation {
  projectedBalance: number
  ledgerDerivedBalance: number
  drift: number
  isConsistent: boolean
}

/** `BlossomStatement` (`IBlossomService.cs:74-85`). */
/**
 * `BlossomStatementDataQuality` — what the statement's numbers rest on (R4, S-3).
 *
 * Two flags exist because two things a reader would otherwise assume are not true.
 * `openingBalanceFromProjection` is always `true` today: the opening balance is derived
 * **backwards** from the cached `BlossomRemaining` projection, not accumulated forward from the
 * ledger. And a `reconciliationChecked` of `false` must render as *"reconciliation status
 * unknown"* — **never** as consistent.
 */
export interface BlossomStatementDataQuality {
  reconciliationChecked: boolean
  openingBalanceFromProjection: boolean
  windowCapped: boolean
  maxWindowDays: number
  notes: string[]
}

export interface BlossomStatement {
  organizationId: string
  periodStart: string
  periodEnd: string
  openingBalance: number
  items: BlossomStatementItem[]
  /** The **window** total, not the page length, and the same on every page. */
  total: number
  page: number
  pageSize: number
  closingBalance: number
  reconciliation: BlossomStatementReconciliation
  generatedAt: string
  /** The effective window cap, so the surface can state it rather than hardcode it. */
  maxWindowDays?: number
  dataQuality?: BlossomStatementDataQuality | null
}

/**
 * `PricingPriceEntryDto` (`PricingDtos.cs:53-66`). `GET /admin/pricing/price-book` returns a **bare
 * unpaginated array** of these, and `GET /admin/pricing/price-book/{entryId}` returns one or an
 * empty-bodied `404`.
 */
export interface PricingPriceEntry {
  id: string
  planTier: string | null
  organizationId: string | null
  skuKind: string
  skuCode: string | null
  blossomQuantity: number
  priceLkr: number
  effectiveFrom: string
  effectiveTo: string | null
  status: string
  changeReason: string
  createdAt: string
  updatedAt: string
}

/** `PricingRuleDto` (`PricingDtos.cs:7-24`). */
export interface PricingRule {
  id: string
  scopeKind: string
  provider: string | null
  model: string | null
  unitsPerBlossom: number
  minimumChargeBlossoms: number
  roundingMode: string
  roundingDecimals: number
  effectiveFrom: string
  effectiveTo: string | null
  status: string
  version: number
  changeReason: string
  createdByUserId: string
  approvedByUserId: string | null
  createdAt: string
  updatedAt: string
}

/** `PricingRulePageDto` (`PricingDtos.cs:41-43`) — this one **is** paged, unlike the price book. */
export interface PagedPricingRules {
  items: PricingRule[]
  total: number
  page: number
  pageSize: number
}

// ── Business KPIs (S-44…S-49) ────────────────────────────────────────────────────────────────
// The wire shapes of `/api/v1/admin/statistics/business/*`. Authority: the C# DTOs in
// `Aveline.Api/Modules/Analytics/DTOs/BusinessKpiDtos.cs` and the catalog entries S-44…S-49.

/** `BusinessWindowDto` — echoed so the client never re-derives the window it asked for. */
export interface BusinessWindow {
  from: string
  to: string
  granularity: string
  timeZone: string
  bucketCount: number
}

/**
 * `BusinessDataQualityDto`. A false flag is named, never silently absorbed: `null` on a measure
 * plus `userAttributionAvailable: false` is "not measured", which is not the same as `0`.
 */
export interface BusinessDataQuality {
  userAttributionAvailable: boolean
  unresolvedAttributionCount: number
  subscriptionHistoryBackfilled: boolean
  lastActivityIsReconstructed: boolean
  agentMetricsUninstrumented: boolean
  notes: string[]
}

export interface GrowthPoint {
  bucketStart: string
  isPartial: boolean
  newUsers: number
  newOrganizations: number
  newAdminRequests: number
  approvedAdminRequests: number
}

export interface GrowthTotals {
  newUsers: number
  newOrganizations: number
  newAdminRequests: number
  approvedAdminRequests: number
}

export interface BusinessGrowth {
  window: BusinessWindow
  observedFrom: string | null
  series: GrowthPoint[]
  totals: GrowthTotals
  previousTotals: GrowthTotals
  dataQuality: BusinessDataQuality
}

export interface ActiveUsersPoint {
  bucketStart: string
  isPartial: boolean
  /** `null` when no request in the window is attributed. Never `0` in that case. */
  activeUsers: number | null
  activeOrganizations: number | null
}

export interface RollingActiveUsers {
  dau: number | null
  wau: number | null
  mau: number | null
  stickiness: number | null
}

export interface BusinessActiveUsers {
  window: BusinessWindow
  series: ActiveUsersPoint[]
  rolling: RollingActiveUsers
  dataQuality: BusinessDataQuality
}

export interface PlanMixItem {
  planTier: string
  isFree: boolean
  /** From `Organizations.PlanTier`; authoritative for every organization. */
  organizationCount: number
  activeOrganizationCount: number
  /** From `OrganizationSubscriptions`; smaller when a billing row is absent. */
  billedSubscriptionCount: number
  userCount: number
  monthlyPriceLkr: number
}

export interface PlanMixSide {
  organizationCount: number
  userCount: number
  monthlyPriceLkr: number
  shareOfOrganizations: number
}

export interface BusinessPlanMix {
  asOf: string
  tiers: PlanMixItem[]
  free: PlanMixSide
  premium: PlanMixSide
  organizationsTotal: number
  organizationsWithBillingRow: number
  totalMonthlyPriceLkr: number
  dataQuality: BusinessDataQuality
}

export interface SubscriptionTrendPoint {
  bucketStart: string
  isPartial: boolean
  activeTotal: number
  activeByTier: Record<string, number>
  started: number
  cancelled: number
  /** True when this bucket was reconstructed from the audit ledger rather than snapshotted. */
  isBackfilled: boolean
}

export interface BusinessSubscriptionTrend {
  window: BusinessWindow
  series: SubscriptionTrendPoint[]
  openingActive: number
  closingActive: number
  churnRate: number
  dataQuality: BusinessDataQuality
}

export interface UsageTrendPoint {
  bucketStart: string
  isPartial: boolean
  messagesSent: number
  agentRuns: number
  apiRequests: number
  blossomUnits: number
  actualCostUsd: number
}

export interface UsageTotals {
  messagesSent: number
  agentRuns: number
  apiRequests: number
  blossomUnits: number
  actualCostUsd: number
}

export interface BusinessUsage {
  window: BusinessWindow
  organizationId: string | null
  series: UsageTrendPoint[]
  totals: UsageTotals
  dataQuality: BusinessDataQuality
}

export interface OrganizationUsageItem {
  rank: number
  organizationId: string
  name: string
  planTier: string
  messagesSent: number
  agentRuns: number
  apiRequests: number
  blossomUnits: number
  lastActivityAt: string | null
  daysSinceLastActivity: number | null
}

export interface BusinessOrganizationUsage {
  metric: string
  from: string
  to: string
  items: OrganizationUsageItem[]
  totalCount: number
  dataQuality: BusinessDataQuality
}

// ── Revenue reads (S-50…S-56) ────────────────────────────────────────────────────────────────
// Authority: `Aveline.Api/Modules/Revenue/DTOs/RevenueReadDtos.cs` and the catalog entries
// S-50…S-56. Every measure is nullable, and **`null` is not `0`**: it means the measure could not
// be computed. A `0` MRR would read as "we earn nothing" when the truth is "no price is
// configured", which is the case in production today.

/** `IncomeLedgerEntryDto` (`RevenueDtos.cs`). The wire shape of one journal row. */
export interface IncomeLedgerEntry {
  id: string
  organizationId: string
  kind: string
  sourceKind: string
  sourceRef: string | null
  chargeBasis: string
  status: string
  currency: string
  /** Always positive. The sign is derived from `kind`. */
  amount: number
  reason: string
  periodStart: string | null
  periodEnd: string | null
  occurredAt: string
  recordedByUserId: string | null
  supersedesEntryId: string | null
}

/** `RevenueWindowDto` — echoed so the client never re-derives the window it asked for. */
export interface RevenueWindow {
  from: string
  to: string
  granularity: string
  timeZone: string
  bucketCount: number
}

/** `RevenueReconciliationDto`. `unverifiedGap` is a **magnitude**, not a signed subtraction. */
export interface RevenueReconciliation {
  derivedTotal: number
  verifiedTotal: number
  unverifiedGap: number
  refundTotal: number
  netVerified: number
  isBalanced: boolean
}

/** `IncomeLedgerPageDto` (S-50). The totals describe the window, not the page. */
export interface IncomeLedgerPage {
  window: RevenueWindow
  items: IncomeLedgerEntry[]
  total: number
  page: number
  pageSize: number
  reconciliation: RevenueReconciliation
  dataQuality: IncomeDataQuality
}

/** `RevenueAccountItemDto` (S-51). */
export interface RevenueAccountItem {
  organizationId: string
  name: string
  planTier: string
  derivedTotal: number
  verifiedTotal: number
  refundTotal: number
  netVerified: number
  unverifiedGap: number
}

export interface RevenueAccountsPage {
  window: RevenueWindow
  items: RevenueAccountItem[]
  total: number
  reconciliation: RevenueReconciliation
  dataQuality: IncomeDataQuality
}

/** `RevenueOverviewDto` (S-52). List-price **scheduled** revenue, not collected revenue. */
export interface RevenueOverview {
  asOf: string
  mrr: number | null
  arr: number | null
  arpu: number | null
  payingOrganizations: number
  activeSubscriptions: number
  dataQuality: IncomeDataQuality
}

/** `RevenueTimeseriesPointDto` (S-53). Three separate series, never merged. */
export interface RevenueTimeseriesPoint {
  bucketStart: string
  isPartial: boolean
  derived: number
  verified: number
  refunded: number
}

export interface RevenueTimeseries {
  window: RevenueWindow
  series: RevenueTimeseriesPoint[]
  dataQuality: IncomeDataQuality
}

/** `RevenueCollectionPointDto` (S-54). */
export interface RevenueCollectionPoint {
  bucketStart: string
  isPartial: boolean
  derivedTotal: number
  verifiedTotal: number
  refundedTotal: number
  /** Percentage, or `null` when nothing was billed. Never `0` for "no rate". */
  collectionRate: number | null
  /** Signed. A negative value means more came in than was billed. */
  outstanding: number
}

export interface RevenueCollections {
  window: RevenueWindow
  series: RevenueCollectionPoint[]
  dataQuality: IncomeDataQuality
}

/** `RevenueBlossomSalesDto` (S-55). Referenced purchases only. */
export interface RevenueBlossomSales {
  window: RevenueWindow
  packsSold: number
  blossomsGranted: number
  listPriceLkr: number
  verifiedLkr: number
  conversion: number | null
  grantedWithoutReference: number
  dataQuality: IncomeDataQuality
}

/** `BlossomReconciliationRowDto` (S-56). Only drifted accounts appear. */
export interface BlossomReconciliationRow {
  organizationId: string
  organizationName: string
  periodStart: string
  projectedBalance: number
  ledgerDerivedBalance: number
  drift: number
  isConsistent: boolean
}

/**
 * `BlossomReconciliationDto` (S-56).
 *
 * `reconciliationChecked` is `null`, not `false`: the read does not itself run a reconciliation
 * pass, so it cannot claim one happened. `false` would claim it checked and found nothing.
 */
export interface BlossomReconciliation {
  organizationId: string | null
  accounts: BlossomReconciliationRow[]
  driftedCount: number
  accountsChecked: number
  reconciliationChecked: boolean | null
  checkedAt: string
  notes: string[]
}

/** The S-56 reconciliation report's scope. */
export interface BlossomReconciliationParams {
  organizationId?: string
}

/** The shared query contract of the revenue reads. */
export interface RevenueWindowParams {
  from?: string
  to?: string
  granularity?: 'day' | 'week' | 'month'
}

/**
 * The statement's filter set (R4). An unset filter is omitted, never sent empty.
 *
 * `kind` is **lower-case** because the server lower-cases it before matching
 * (`BlossomEndpoints.TryParseKind`), so the wire value is lower-case and a client that sends
 * `Entitlement` is relying on someone else's leniency.
 */
export interface BlossomStatementParams {
  from?: string
  to?: string
  kind?: 'all' | 'entitlement' | 'consumption'
  entryType?: string
  sourceKind?: string
  query?: string
  minAmount?: number
  maxAmount?: number
  page?: number
  pageSize?: number
}

/** The three administrative revenue verbs' request bodies (S-50). */
export interface VerifyIncomeRequest {
  organizationId: string
  sourceKind: string
  sourceRef: string
  amount: number
  reason: string
}

export interface RefundIncomeRequest {
  organizationId: string
  sourceKind: string
  sourceRef: string
  amount: number
  reason: string
}

export interface AdjustIncomeRequest {
  organizationId: string
  amount: number
  reason: string
  sourceRef?: string | null
  supersedesEntryId?: string | null
}

/** The shared query contract of the six business reads. */
export interface BusinessWindowParams {
  from?: string
  to?: string
  granularity?: 'day' | 'week' | 'month'
  organizationId?: string
}

export type BusinessRankingMetric = 'messages' | 'agentRuns' | 'apiRequests' | 'blossomUnits'

// ── Revenue (S-50…S-56) ──────────────────────────────────────────────────────────────────────
// The wire shapes of `/api/v1/admin/revenue/*` and `/api/v1/admin/statistics/revenue/*`.
// Authority: the C# DTOs in `Aveline.Api/Modules/Revenue/DTOs/RevenueDtos.cs` (R3) and the
// catalog entries S-50…S-56 (R0).

/**
 * `IncomeDataQualityDto`. The **fifth** data-quality vocabulary, alongside system, agent, api
 * and business. Deliberately not a reuse of `BusinessDataQuality`: attribution and backfill say
 * nothing about whether a price was configured or whether a receipt was verified.
 *
 * The three facts this exists to state, so no caller has to infer them:
 *
 * - `revenueProviderSettlementAvailable: false` — no payment provider is wired in this
 *   repository, so **no figure here is settled money**. Every amount is an expectation or an
 *   operator's confirmation, and the surface must say which.
 * - `subscriptionPricesConfigured: false` — every subscription currently has `PriceLkr = 0`
 *   because `SubscriptionService` never assigns it. A derived charge of `0` therefore means
 *   *no list price is configured*; it does not mean free, and MRR is `null` rather than `0`.
 * - `derivedEntriesUnverified` — the count of `Derived` rows with no `Verified` counterpart.
 *   That gap is the most important number on the surface, and it is not an error.
 *
 * Field order mirrors the C# record. `INCOME_QUALITY_FIELDS` in `lib/admin/revenue-quality.ts`
 * pins the set.
 */
export interface IncomeDataQuality {
  /** `false` until a provider client settles money. Never rendered as "collected". */
  revenueProviderSettlementAvailable: boolean
  /** `false` when every subscription's `PriceLkr` is `0`, so MRR is not measurable. */
  subscriptionPricesConfigured: boolean
  /** `Derived` rows with no `Verified` counterpart — the unverified gap. */
  derivedEntriesUnverified: number
  /** When these flags were evaluated, distinct from the window's `to`. */
  checkedAt: string
  /** Free-text notes, including the cache-degradation note when a shared cache is absent. */
  notes: string[]
}
