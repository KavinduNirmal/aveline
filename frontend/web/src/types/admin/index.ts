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
