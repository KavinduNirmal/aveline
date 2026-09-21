import { apiClient } from '@/lib/api'

/**
 * The tenant statistics surface: API consumption (S-24…S-32, `stats:view`).
 *
 * **The agentic family is deliberately absent.** Runs, tokens and provider cost are agent
 * internals; the org-scoped agent statistics routes are not mounted for a tenant, and only the
 * team-only `/admin/statistics/agents/**` subset survives (the console reads it through
 * `lib/admin/api.ts`). A boutique reads its usage in Blossoms, so there is nothing here to ask
 * `canReadAgentStatistics` about.
 *
 * One rule is encoded here: **a percentile below the server's sample floor is `null` with a
 * reason, never `0`.** Every latency and rate field is `number | null` and the panels pass them
 * through the shared formatters, so an absent measurement cannot be rendered as a fast one.
 */

export interface ApiStatisticsDataQuality {
  rollupComplete: boolean
  rawLogSampled: boolean
  latencyBuckets: boolean
}

export interface ApiRequestCount {
  from: string
  to: string
  requestCount: number
  successCount: number
  errorCount: number
  clientErrorCount: number
  serverErrorCount: number
  throttledCount: number
  dataQuality: ApiStatisticsDataQuality
}

export interface ApiErrorBreakdownItem {
  routeTemplate: string | null
  statusCode: number | null
  requestCount: number
}

export interface ApiErrorRate {
  from: string
  to: string
  requestCount: number
  clientErrorRate: number | null
  serverErrorRate: number | null
  throttleRate: number | null
  byStatusCode: ApiErrorBreakdownItem[]
  byRoute: ApiErrorBreakdownItem[]
  dataQuality: ApiStatisticsDataQuality
}

export interface ApiLatencyPoint {
  windowStart: string
  requestCount: number
  avgMs: number | null
  p50Ms: number | null
  p95Ms: number | null
  p99Ms: number | null
}

export interface ApiLatency {
  from: string
  to: string
  requestCount: number
  avgMs: number | null
  p50Ms: number | null
  p95Ms: number | null
  p99Ms: number | null
  maxDurationMs: number
  precision: string
  reason: string | null
  series: ApiLatencyPoint[]
  dataQuality: ApiStatisticsDataQuality
}

export interface ApiEndpointUsageItem {
  routeTemplate: string
  httpMethod: string
  requestCount: number
  errorCount: number
  throttledCount: number
  avgMs: number
}

export interface ApiEndpointUsage {
  items: ApiEndpointUsageItem[]
  dataQuality: ApiStatisticsDataQuality
}

export interface ApiUserUsageItem {
  userId: string | null
  requestCount: number
  errorCount: number
  errorRate: number | null
  lastSeenAt: string | null
}

export interface ApiUserUsage {
  items: ApiUserUsageItem[]
  page: number
  pageSize: number
  total: number
  dataQuality: ApiStatisticsDataQuality
}

export interface ApiKeyUsageItem {
  apiKeyId: string | null
  requestCount: number
  errorCount: number
  errorRate: number | null
  avgMs: number
  topEndpoint: string | null
  lastUsedAt: string | null
}

export interface ApiKeyUsage {
  items: ApiKeyUsageItem[]
  page: number
  pageSize: number
  total: number
  dataQuality: ApiStatisticsDataQuality
}

export interface ApiQuotaStatusItem {
  metricKey: string
  apiKeyId: string | null
  limit: number | null
  used: number
  remaining: number | null
  percentUsed: number | null
  periodStart: string
  periodEnd: string
  warnedAt: string | null
  exhaustedAt: string | null
}

export interface ApiQuotaStatus {
  items: ApiQuotaStatusItem[]
  dataQuality: ApiStatisticsDataQuality
}

export interface ApiSlowRequestItem {
  apiKeyId: string | null
  userId: string | null
  routeTemplate: string
  httpMethod: string
  statusCode: number
  durationMs: number
  occurredAt: string
  requestId: string | null
  traceId: string | null
  resourceType: string | null
  resourceId: string | null
  errorCode: string | null
}

export interface ApiSlowRequests {
  items: ApiSlowRequestItem[]
  page: number
  pageSize: number
  total: number
  dataQuality: ApiStatisticsDataQuality
}

export interface ApiBillableRequests {
  from: string
  to: string
  billableRequestCount: number
  excludedRequestCount: number
  dataQuality: ApiStatisticsDataQuality
}

export interface ApiStatisticsFilterParams {
  from?: string
  to?: string
  routeTemplate?: string
  httpMethod?: string
  status?: string
  statusClass?: string
  apiKeyId?: string
  userId?: string
  page?: number
  pageSize?: number
  groupBy?: string
}

const apiBase = (organizationId: string) => `/api/v1/orgs/${organizationId}/statistics/api`
function apiParams(filters: ApiStatisticsFilterParams) {
  return {
    from: filters.from || undefined,
    to: filters.to || undefined,
    routeTemplate: filters.routeTemplate || undefined,
    httpMethod: filters.httpMethod || undefined,
    status: filters.status || undefined,
    statusClass: filters.statusClass || undefined,
    apiKeyId: filters.apiKeyId || undefined,
    userId: filters.userId || undefined,
    page: filters.page ?? 1,
    pageSize: filters.pageSize ?? 25,
    groupBy: filters.groupBy ?? 'day',
  }
}

async function get<T>(path: string, params: unknown, signal?: AbortSignal): Promise<T> {
  const response = await apiClient.get<T>(path, { params, signal })
  return response.data
}

// ── API consumption (stats:view) ────────────────────────────────────────────────────────────────

export const fetchApiRequests = (org: string, f: ApiStatisticsFilterParams = {}, s?: AbortSignal) =>
  get<ApiRequestCount>(`${apiBase(org)}/requests`, apiParams(f), s)

export const fetchApiErrors = (org: string, f: ApiStatisticsFilterParams = {}, s?: AbortSignal) =>
  get<ApiErrorRate>(`${apiBase(org)}/errors`, apiParams(f), s)

export const fetchApiLatency = (org: string, f: ApiStatisticsFilterParams = {}, s?: AbortSignal) =>
  get<ApiLatency>(`${apiBase(org)}/latency`, apiParams(f), s)

export const fetchApiEndpoints = (org: string, f: ApiStatisticsFilterParams = {}, s?: AbortSignal) =>
  get<ApiEndpointUsage>(`${apiBase(org)}/endpoints`, apiParams(f), s)

export const fetchApiUsers = (org: string, f: ApiStatisticsFilterParams = {}, s?: AbortSignal) =>
  get<ApiUserUsage>(`${apiBase(org)}/users`, apiParams(f), s)

/** A quota is a period fact, so this route takes no window. */
export const fetchApiQuota = (org: string, s?: AbortSignal) =>
  get<ApiQuotaStatus>(`${apiBase(org)}/quota`, undefined, s)

export const fetchApiSlowRequests = (
  org: string,
  f: ApiStatisticsFilterParams = {},
  s?: AbortSignal,
) => get<ApiSlowRequests>(`${apiBase(org)}/slow-requests`, apiParams(f), s)

export const fetchApiBillable = (org: string, f: ApiStatisticsFilterParams = {}, s?: AbortSignal) =>
  get<ApiBillableRequests>(`${apiBase(org)}/billable`, apiParams(f), s)

export const fetchApiKeyUsage = (org: string, f: ApiStatisticsFilterParams = {}, s?: AbortSignal) =>
  get<ApiKeyUsage>(`/api/v1/orgs/${org}/statistics/api-keys`, apiParams(f), s)
