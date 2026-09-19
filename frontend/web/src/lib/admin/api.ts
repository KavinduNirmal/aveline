import { apiClient } from "@/lib/api"
import type {
  AdminApprovalRequestSummary,
  AdminUserDto,
  AuditLogEntry,
  AuthClaims,
  ChangeUserStateRequest,
  PagedAdminOrganizations,
  PagedAuditLogEntries,
  PagedUsers,
  SetEntitlementOverridesRequest,
  SystemAlert,
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
}): Promise<{
  items: SystemAlert[]
  page: number
  pageSize: number
  total: number
}> {
  const response = await apiClient.get(
    "/api/v1/admin/statistics/system/alerts",
    { params },
  )
  return response.data
}

export async function acknowledgeAlert(
  alertId: string,
  note?: string,
): Promise<unknown> {
  const response = await apiClient.post(
    `/api/v1/admin/statistics/system/alerts/${alertId}/acknowledge`,
    { note: note || null },
  )
  return response.data
}

export async function executeBlossomOperation(
  organizationId: string,
  operation: "credit" | "debit" | "revoke",
  data: {
    amount: number
    reason: string
    allowNegative?: boolean
    idempotencyKey?: string
  },
): Promise<unknown> {
  const headers: Record<string, string> = {}
  if (data.idempotencyKey) {
    headers["Idempotency-Key"] = data.idempotencyKey
  }

  const response = await apiClient.post(
    `/api/v1/admin/orgs/${organizationId}/blossoms/${operation}`,
    {
      amount: data.amount,
      reason: data.reason,
      allowNegative: data.allowNegative ?? false,
    },
    { headers },
  )
  return response.data
}
