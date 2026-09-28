import { apiClient } from "@/lib/api"
import type {
  AdminApprovalRequestSummary,
  BusinessActiveUsers,
  BusinessGrowth,
  BusinessOrganizationUsage,
  BusinessPlanMix,
  BusinessRankingMetric,
  BusinessSubscriptionTrend,
  BusinessUsage,
  BusinessWindowParams,
  AdminUserDto,
  AgentOverviewDto,
  AuditLogEntry,
  IncomeLedgerEntry,
  IncomeLedgerPage,
  RefundIncomeRequest,
  RevenueAccountsPage,
  RevenueBlossomSales,
  RevenueCollections,
  RevenueOverview,
  RevenueTimeseries,
  RevenueWindowParams,
  VerifyIncomeRequest,
  AdjustIncomeRequest,
  BlossomReconciliation,
  BlossomReconciliationParams,
  BlossomStatement,
  BlossomStatementParams,
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

/**
 * `GET /api/v1/admin/orgs/{organizationId}/blossoms/statement` (`BlossomEndpoints.cs`).
 *
 * The balance must never be cached, so callers pass `staleTime: 0`. Paging and filtering are
 * **server-side** since R4: an unset filter is omitted rather than sent empty, because `?from=`
 * would bind as an unparseable date and `?q=` would filter on the empty string instead of not
 * filtering at all.
 */
export async function fetchBlossomStatement(
  organizationId: string,
  params: BlossomStatementParams = {},
): Promise<BlossomStatement> {
  const response = await apiClient.get<BlossomStatement>(
    `/api/v1/admin/orgs/${organizationId}/blossoms/statement`,
    {
      params: {
        from: params.from || undefined,
        to: params.to || undefined,
        kind: params.kind || undefined,
        entryType: params.entryType || undefined,
        sourceKind: params.sourceKind || undefined,
        q: params.query || undefined,
        minAmount: params.minAmount ?? undefined,
        maxAmount: params.maxAmount ?? undefined,
        page: params.page ?? 1,
        pageSize: params.pageSize ?? 50,
      },
    },
  )
  return response.data
}

// ── Revenue reads (S-50…S-55) ────────────────────────────────────────────────────────────────

/** `GET /api/v1/admin/revenue/ledger` (S-50). The register is deliberately uncached server-side. */
export async function fetchIncomeLedger(
  params: { from?: string; to?: string; page?: number; pageSize?: number } = {},
): Promise<IncomeLedgerPage> {
  const response = await apiClient.get<IncomeLedgerPage>(
    "/api/v1/admin/revenue/ledger",
    {
      params: {
        from: params.from || undefined,
        to: params.to || undefined,
        page: params.page ?? 1,
        pageSize: params.pageSize ?? 50,
      },
    },
  )
  return response.data
}

/** `GET /api/v1/admin/revenue/accounts` (S-51). */
export async function fetchIncomeAccounts(
  params: RevenueWindowParams = {},
): Promise<RevenueAccountsPage> {
  const response = await apiClient.get<RevenueAccountsPage>(
    "/api/v1/admin/revenue/accounts",
    { params: revenueQuery(params) },
  )
  return response.data
}

/** `GET /api/v1/admin/statistics/revenue/overview` (S-52). */
export async function fetchIncomeOverview(
  params: RevenueWindowParams = {},
): Promise<RevenueOverview> {
  const response = await apiClient.get<RevenueOverview>(
    "/api/v1/admin/statistics/revenue/overview",
    { params: revenueQuery(params) },
  )
  return response.data
}

/** `GET /api/v1/admin/statistics/revenue/timeseries` (S-53). */
export async function fetchRevenueTimeseries(
  params: RevenueWindowParams = {},
): Promise<RevenueTimeseries> {
  const response = await apiClient.get<RevenueTimeseries>(
    "/api/v1/admin/statistics/revenue/timeseries",
    { params: revenueQuery(params) },
  )
  return response.data
}

/** `GET /api/v1/admin/statistics/revenue/collections` (S-54). */
export async function fetchRevenueCollections(
  params: RevenueWindowParams = {},
): Promise<RevenueCollections> {
  const response = await apiClient.get<RevenueCollections>(
    "/api/v1/admin/statistics/revenue/collections",
    { params: revenueQuery(params) },
  )
  return response.data
}

/** `GET /api/v1/admin/statistics/revenue/blossoms` (S-55). */
export async function fetchRevenueBlossomSales(
  params: RevenueWindowParams = {},
): Promise<RevenueBlossomSales> {
  const response = await apiClient.get<RevenueBlossomSales>(
    "/api/v1/admin/statistics/revenue/blossoms",
    { params: revenueQuery(params) },
  )
  return response.data
}

/** `GET /api/v1/admin/statistics/billing/reconciliation` (S-56). */
export async function reviseReconciliation(
  params: BlossomReconciliationParams = {},
): Promise<BlossomReconciliation> {
  const response = await apiClient.get<BlossomReconciliation>(
    "/api/v1/admin/statistics/billing/reconciliation",
    { params: { organizationId: params.organizationId || undefined } },
  )
  return response.data
}

/** The shared query mapping for the revenue series reads; absent values are omitted. */
function revenueQuery(params: RevenueWindowParams): Record<string, unknown> {
  return {
    from: params.from || undefined,
    to: params.to || undefined,
    granularity: params.granularity || undefined,
  }
}

// ── Revenue writes (S-50) ────────────────────────────────────────────────────────────────────

/** What a revenue write returns, including the `Idempotency-Replayed` signal. */
export interface IncomeMutationResult {
  entry: IncomeLedgerEntry
  /** True when the server replayed a stored response rather than applying the operation again. */
  replayed: boolean
}

/**
 * The three revenue POSTs are idempotency-guarded, so the `Idempotency-Key` header is a
 * **required argument**. It belongs to the operation rather than the request, so it is derived from
 * the payload by the caller and reused across a retry of the same payload.
 */
export async function verifyIncome(
  request: VerifyIncomeRequest,
  idempotencyKey: string,
): Promise<IncomeMutationResult> {
  return postRevenue("/api/v1/admin/revenue/ledger/verify", request, idempotencyKey)
}

export async function refundIncome(
  request: RefundIncomeRequest,
  idempotencyKey: string,
): Promise<IncomeMutationResult> {
  return postRevenue("/api/v1/admin/revenue/ledger/refund", request, idempotencyKey)
}

export async function adjustIncome(
  request: AdjustIncomeRequest,
  idempotencyKey: string,
): Promise<IncomeMutationResult> {
  return postRevenue("/api/v1/admin/revenue/ledger/adjust", request, idempotencyKey)
}

async function postRevenue<T extends object>(
  path: string,
  body: T,
  idempotencyKey: string,
): Promise<IncomeMutationResult> {
  const response = await apiClient.post<IncomeLedgerEntry>(path, body, {
    headers: { "Idempotency-Key": idempotencyKey },
  })
  return {
    entry: response.data,
    replayed: readReplayHeader(response.headers),
  }
}

/**
 * Reads the `Idempotency-Replayed` signal case-insensitively.
 *
 * Axios lower-cases response header names today, but the header is the server's contract rather
 * than axios's, so a change in that behaviour would silently turn every replay into a fresh
 * application — the one confusion the signal exists to prevent.
 */
function readReplayHeader(headers: unknown): boolean {
  if (headers === null || typeof headers !== "object") return false
  const record = headers as Record<string, unknown>
  for (const [key, value] of Object.entries(record)) {
    if (key.toLowerCase() === "idempotency-replayed") return value === "true"
  }
  return false
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

// ── Business KPIs (S-44…S-49) ────────────────────────────────────────────────────────────────

/**
 * The shared query string for the six business reads. An absent parameter is omitted rather
 * than sent empty, so the server's own default applies.
 */
function businessQuery(params: BusinessWindowParams): Record<string, string | number | undefined> {
  return {
    from: params.from || undefined,
    to: params.to || undefined,
    granularity: params.granularity || undefined,
    organizationId: params.organizationId || undefined,
  }
}

export async function fetchBusinessGrowth(
  params: BusinessWindowParams,
): Promise<BusinessGrowth> {
  const response = await apiClient.get<BusinessGrowth>(
    "/api/v1/admin/statistics/business/growth",
    { params: businessQuery(params) },
  )
  return response.data
}

export async function fetchBusinessActiveUsers(
  params: BusinessWindowParams,
): Promise<BusinessActiveUsers> {
  const response = await apiClient.get<BusinessActiveUsers>(
    "/api/v1/admin/statistics/business/active-users",
    { params: businessQuery(params) },
  )
  return response.data
}

export async function fetchBusinessPlanMix(): Promise<BusinessPlanMix> {
  const response = await apiClient.get<BusinessPlanMix>(
    "/api/v1/admin/statistics/business/plan-mix",
  )
  return response.data
}

export async function fetchBusinessSubscriptionTrend(
  params: BusinessWindowParams,
): Promise<BusinessSubscriptionTrend> {
  const response = await apiClient.get<BusinessSubscriptionTrend>(
    "/api/v1/admin/statistics/business/subscriptions",
    { params: businessQuery(params) },
  )
  return response.data
}

/**
 * Product usage. Passing `organizationId` is the org drill-down; the server then additionally
 * requires `admin:orgs:read`.
 */
export async function fetchBusinessUsage(
  params: BusinessWindowParams,
): Promise<BusinessUsage> {
  const response = await apiClient.get<BusinessUsage>(
    "/api/v1/admin/statistics/business/usage",
    { params: businessQuery(params) },
  )
  return response.data
}

export async function fetchBusinessOrganizationUsage(
  params: BusinessWindowParams & { metric?: BusinessRankingMetric; limit?: number },
): Promise<BusinessOrganizationUsage> {
  const response = await apiClient.get<BusinessOrganizationUsage>(
    "/api/v1/admin/statistics/business/organizations",
    {
      params: {
        ...businessQuery(params),
        metric: params.metric || undefined,
        limit: params.limit ?? undefined,
      },
    },
  )
  return response.data
}
