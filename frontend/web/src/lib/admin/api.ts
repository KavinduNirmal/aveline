import { apiClient } from "@/lib/api"
import type {
  AdminApprovalRequestSummary,
  AdminUserDto,
  AgentOverviewDto,
  AuditLogEntry,
  AuthClaims,
  ChangeUserStateRequest,
  CreditBlossomsRequest,
  DebitBlossomsRequest,
  PagedAdminOrganizations,
  PagedAuditLogEntries,
  PagedUsers,
  RevokeBlossomsRequest,
  SetEntitlementOverridesRequest,
  SystemAlertAckResponse,
  SystemAlertPage,
  SystemOverview,
  SystemMetricSeries,
} from "@/types/admin"

export async function fetchAuthClaims(): Promise<AuthClaims> {
  const response = await apiClient.get<AuthClaims>("/api/v1/auth/claims")
  return response.data
}

export async function searchAdminUsers(params: {
  q?: string
  accountState?: string
  organizationId?: string
  page?: number
  pageSize?: number
}): Promise<PagedUsers> {
  const response = await apiClient.get<PagedUsers>("/api/v1/admin/users", {
    params: {
      q: params.q || undefined,
      accountState: params.accountState || undefined,
      organizationId: params.organizationId || undefined,
      page: params.page ?? 1,
      pageSize: params.pageSize ?? 50,
    },
  })
  return response.data
}

export async function updateUserAccountState(
  userId: string,
  data: ChangeUserStateRequest,
): Promise<AdminUserDto> {
  const response = await apiClient.patch<AdminUserDto>(
    `/api/v1/admin/users/${userId}/state`,
    data,
  )
  return response.data
}

export async function searchAdminOrganizations(params: {
  q?: string
  isActive?: boolean
  planTier?: string
  page?: number
  pageSize?: number
}): Promise<PagedAdminOrganizations> {
  const response = await apiClient.get<PagedAdminOrganizations>(
    "/api/v1/admin/orgs",
    {
      params: {
        q: params.q || undefined,
        isActive: params.isActive !== undefined ? params.isActive : undefined,
        planTier: params.planTier || undefined,
        page: params.page ?? 1,
        pageSize: params.pageSize ?? 50,
      },
    },
  )
  return response.data
}

export async function setEntitlementOverrides(
  organizationId: string,
  request: SetEntitlementOverridesRequest,
): Promise<{ organizationId: string; entitlements: unknown }> {
  const response = await apiClient.patch(
    `/api/v1/admin/orgs/${organizationId}/entitlement-overrides`,
    request,
  )
  return response.data
}

export async function queryAuditEntries(params: {
  action?: string
  entityType?: string
  entityId?: string
  organizationId?: string
  actorUserId?: string
  from?: string
  to?: string
  page?: number
  pageSize?: number
}): Promise<PagedAuditLogEntries> {
  const response = await apiClient.get<PagedAuditLogEntries>(
    "/api/v1/admin/audit",
    {
      params: {
        action: params.action || undefined,
        entityType: params.entityType || undefined,
        entityId: params.entityId || undefined,
        organizationId: params.organizationId || undefined,
        actorUserId: params.actorUserId || undefined,
        from: params.from || undefined,
        to: params.to || undefined,
        page: params.page ?? 1,
        pageSize: params.pageSize ?? 50,
      },
    },
  )
  return response.data
}

export async function getAuditEntry(id: string): Promise<AuditLogEntry> {
  const response = await apiClient.get<AuditLogEntry>(
    `/api/v1/admin/audit/${id}`,
  )
  return response.data
}

export async function listAdminRequests(): Promise<
  AdminApprovalRequestSummary[]
> {
  const response = await apiClient.get<AdminApprovalRequestSummary[]>(
    "/api/v1/admin/requests",
  )
  return response.data
}

export async function approveAdminRequest(
  id: string,
): Promise<AdminApprovalRequestSummary> {
  const response = await apiClient.post<AdminApprovalRequestSummary>(
    `/api/v1/admin/requests/${id}/approve`,
  )
  return response.data
}

export async function rejectAdminRequest(
  id: string,
): Promise<AdminApprovalRequestSummary> {
  const response = await apiClient.post<AdminApprovalRequestSummary>(
    `/api/v1/admin/requests/${id}/reject`,
  )
  return response.data
}

export async function fetchSystemOverview(): Promise<SystemOverview> {
  const response = await apiClient.get<SystemOverview>(
    "/api/v1/admin/statistics/system/overview",
  )
  return response.data
}

export async function fetchSystemMetrics(params: {
  metric: string
  from?: string
  to?: string
  windowSize?: string
}): Promise<SystemMetricSeries> {
  const response = await apiClient.get<SystemMetricSeries>(
    "/api/v1/admin/statistics/system/metrics",
    { params },
  )
  return response.data
}

export async function fetchSystemAlerts(params: {
  status?: string
  severity?: string
  ruleId?: string
  page?: number
  pageSize?: number
}): Promise<SystemAlertPage> {
  const response = await apiClient.get<SystemAlertPage>(
    "/api/v1/admin/statistics/system/alerts",
    { params },
  )
  return response.data
}

/**
 * `GET /admin/statistics/agents/overview` — the agent family's own `dataQuality` vocabulary
 * (five booleans), which must not be rendered with the system family's wording.
 */
export async function fetchAgentOverview(): Promise<AgentOverviewDto> {
  const response = await apiClient.get<AgentOverviewDto>(
    "/api/v1/admin/statistics/agents/overview",
  )
  return response.data
}

export async function acknowledgeAlert(
  alertId: string,
  note?: string,
): Promise<SystemAlertAckResponse> {
  const response = await apiClient.post<SystemAlertAckResponse>(
    `/api/v1/admin/statistics/system/alerts/${alertId}/acknowledge`,
    { note: note || null },
  )
  return response.data
}

/**
 * The three Blossom POSTs are idempotency-guarded and the `Idempotency-Key` header is
 * **mandatory** for all of them (`IdempotencyEndpointFilter.cs:44-52`), so the key is a
 * required argument rather than an optional one: a caller cannot forget it.
 *
 * The three verbs also bind different request records, so each has its own function
 * (`BlossomDtos.cs:69-78`). A shared body would make `revoke` unsendable — the delivered
 * client sent `{ amount, reason, allowNegative }` against
 * `RevokeBlossomsRequest(Guid LedgerEntryId, string Reason)`.
 */
export async function creditBlossoms(
  organizationId: string,
  request: CreditBlossomsRequest,
  idempotencyKey: string,
): Promise<unknown> {
  const response = await apiClient.post(
    `/api/v1/admin/orgs/${organizationId}/blossoms/credit`,
    request,
    { headers: { "Idempotency-Key": idempotencyKey } },
  )
  return response.data
}

export async function debitBlossoms(
  organizationId: string,
  request: DebitBlossomsRequest,
  idempotencyKey: string,
): Promise<unknown> {
  const response = await apiClient.post(
    `/api/v1/admin/orgs/${organizationId}/blossoms/debit`,
    request,
    { headers: { "Idempotency-Key": idempotencyKey } },
  )
  return response.data
}

export async function revokeBlossoms(
  organizationId: string,
  request: RevokeBlossomsRequest,
  idempotencyKey: string,
): Promise<unknown> {
  const response = await apiClient.post(
    `/api/v1/admin/orgs/${organizationId}/blossoms/revoke`,
    request,
    { headers: { "Idempotency-Key": idempotencyKey } },
  )
  return response.data
}
