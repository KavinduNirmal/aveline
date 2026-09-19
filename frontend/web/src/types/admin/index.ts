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

export interface AdminOrganizationDto {
  id: string
  name: string
  slug: string
  planTier: PlanTier
  isActive: boolean
  ownerEmail?: string | null
  memberCount?: number
  createdAt: string
  updatedAt: string
}

export interface PagedAdminOrganizations {
  items: AdminOrganizationDto[]
  page: number
  pageSize: number
  total: number
}

export interface EntitlementOverrideInput {
  key: string
  valueType: string
  value: string
  effectiveFrom: string
  effectiveTo: string
  reason: string
}

export interface SetEntitlementOverridesRequest {
  overrides: EntitlementOverrideInput[]
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

export interface SystemAlert {
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

export interface SystemOverview {
  version: SystemVersionDto
  readiness: ReadinessDto
  uptimeSeconds: number
  alerts: {
    critical: number
    warning: number
    top: SystemAlert[]
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
