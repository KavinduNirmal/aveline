import { apiClient } from "@/lib/api"
import type {
  AdminApprovalRequestSummary,
  AdminUserDto,
  AgentOverviewDto,
  AuditLogEntry,
  BlossomStatement,
  AuthClaims,
  ChangeUserStateRequest,
  CreditBlossomsRequest,
  DebitBlossomsRequest,
  PagedAdminOrganizations,
  PagedAuditLogEntries,
  PagedPricingRules,
  PagedUsers,
  PricingPriceEntry,
  PricingRecomputeResult,
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

/** What a Blossom mutation returns, including the `Idempotency-Replayed` signal. */
export interface BlossomMutationResult {
  data: unknown
  replayed: boolean
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
): Promise<BlossomMutationResult> {
  const response = await apiClient.post(
    `/api/v1/admin/orgs/${organizationId}/blossoms/credit`,
    request,
    { headers: { "Idempotency-Key": idempotencyKey } },
  )
  return {
    data: response.data,
    replayed: response.headers["idempotency-replayed"] === "true",
  }
}

export async function debitBlossoms(
  organizationId: string,
  request: DebitBlossomsRequest,
  idempotencyKey: string,
): Promise<BlossomMutationResult> {
  const response = await apiClient.post(
    `/api/v1/admin/orgs/${organizationId}/blossoms/debit`,
    request,
    { headers: { "Idempotency-Key": idempotencyKey } },
  )
  return {
    data: response.data,
    replayed: response.headers["idempotency-replayed"] === "true",
  }
}

export async function revokeBlossoms(
  organizationId: string,
  request: RevokeBlossomsRequest,
  idempotencyKey: string,
): Promise<BlossomMutationResult> {
  const response = await apiClient.post(
    `/api/v1/admin/orgs/${organizationId}/blossoms/revoke`,
    request,
    { headers: { "Idempotency-Key": idempotencyKey } },
  )
  return {
    data: response.data,
    replayed: response.headers["idempotency-replayed"] === "true",
  }
}

/**
 * `POST /admin/pricing/rules/{ruleId}/recompute`. Guarded by `pricing:backdate`, which `admin`
 * does not hold; returns `200` with a `PricingRecomputeResult`, so a zero-effect run is visible
 * rather than silent.
 */
export async function recomputePricingRule(ruleId: string): Promise<PricingRecomputeResult> {
  const response = await apiClient.post<PricingRecomputeResult>(
    `/api/v1/admin/pricing/rules/${ruleId}/recompute`,
  )
  return response.data
}

/** `GET /admin/orgs/{organizationId}/blossoms/statement`. The balance must never be cached. */
export async function fetchBlossomStatement(
  organizationId: string,
  params: { from?: string; to?: string; page?: number; pageSize?: number } = {},
): Promise<BlossomStatement> {
  const response = await apiClient.get<BlossomStatement>(
    `/api/v1/admin/orgs/${organizationId}/blossoms/statement`,
    { params },
  )
  return response.data
}

/**
 * `GET /admin/pricing/price-book` — a **bare, unpaginated array** (`PricingEndpoints.cs:230`), so
 * there is no `page`/`pageSize` to send. Guarded by the `PricingAdminRead` role policy.
 */
export async function fetchPriceBook(params: {
  skuKind?: string
  planTier?: string
  organizationId?: string
} = {}): Promise<PricingPriceEntry[]> {
  const response = await apiClient.get<PricingPriceEntry[]>(
    '/api/v1/admin/pricing/price-book',
    { params },
  )
  return response.data
}

/**
 * `GET /admin/pricing/rules` — a **paged** envelope (`PricingRulePageDto`), unlike the bare
 * price-book array. Guarded by the `PricingAdminRead` role policy.
 */
export async function fetchPricingRules(params: {
  scopeKind?: string
  provider?: string
  model?: string
  status?: string
  activeAt?: string
  page?: number
  pageSize?: number
} = {}): Promise<PagedPricingRules> {
  const response = await apiClient.get<PagedPricingRules>('/api/v1/admin/pricing/rules', {
    params,
  })
  return response.data
}
