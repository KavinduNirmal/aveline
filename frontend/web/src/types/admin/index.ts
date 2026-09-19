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

/** `BlossomStatementItem` (`IBlossomService.cs:58-69`). */
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
}

/** `BlossomStatementReconciliation` (`IBlossomService.cs:71-72`). */
export interface BlossomStatementReconciliation {
  projectedBalance: number
  ledgerDerivedBalance: number
  drift: number
  isConsistent: boolean
}

/** `BlossomStatement` (`IBlossomService.cs:74-85`). */
export interface BlossomStatement {
  organizationId: string
  periodStart: string
  periodEnd: string
  openingBalance: number
  items: BlossomStatementItem[]
  total: number
  page: number
  pageSize: number
  closingBalance: number
  reconciliation: BlossomStatementReconciliation
  generatedAt: string
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

/** The shared query contract of the six business reads. */
export interface BusinessWindowParams {
  from?: string
  to?: string
  granularity?: 'day' | 'week' | 'month'
  organizationId?: string
}

export type BusinessRankingMetric = 'messages' | 'agentRuns' | 'apiRequests' | 'blossomUnits'
