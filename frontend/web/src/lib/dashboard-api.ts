import { apiClient } from '@/lib/api'

/**
 * The tenant dashboard's KPI surface (docs/api/README.md B.22).
 *
 * The types encode the contract rather than describing it:
 *
 * 1. **Every nullable measure is `number | null`.** `null` is "not measured" and is never rendered
 *    as `0`; `formatMoney` is the one place that distinction becomes text.
 * 2. **The reduced takings type carries exactly two money figures and no more.** There is no field
 *    a caller could render as "income" on its own, because there is no such figure.
 */

export type DashboardWindow = '7d' | '30d' | '90d' | 'mtd' | 'ytd'

export interface TenantDashboardDataQuality {
  paymentRowsPresent: boolean
  orderCostsComplete: boolean
  incomeLedgerBackfilled: boolean
  usageMetricsAvailable: boolean
  refundAndOutstandingExcludedFromCollected: boolean
  checkedAt: string
  notes: string[]
}

export interface TenantSalesKpis {
  grossOrderValue: number | null
  orderCount: number
  averageOrderValue: number | null
  discountTotal: number | null
  marginAmount: number | null
  marginPercent: number | null
  /** False when any order carries a zero wholesale cost, which is caller-supplied. */
  marginCostsComplete: boolean
}

export interface TenantCashKpis {
  collected: number | null
  outstanding: number | null
  refunded: number | null
  refundCount: number
}

export interface TenantCustomerKpis {
  activeCount: number
  totalCount: number
  newInWindow: number
  repeatCount: number
  inactivityThresholdDays: number
}

export interface TenantCatalogKpis {
  itemCount: number
  lowStockCount: number
  outOfStockCount: number
  stockValueAtCost: number | null
}

export interface TenantTeamKpis {
  activeSeats: number
  /**
   * Deliberately `0` in this aggregate: the allowance lives on the entitlements surface, so a panel
   * must not read `0` as "no seats allowed".
   */
  allowedSeats: number
  pendingInvitations: number
  roleBreakdown: Record<string, number>
}

export interface TenantUsageKpis {
  blossomUsed: number | null
  monthlyBlossomLimit: number | null
  blossomRemaining: number | null
  percentUsed: number | null
}

export interface TenantOperationsKpis {
  pendingApprovals: number
  openConversations: number
  scheduledDeliveries: number
}

export interface TenantDashboardSummary {
  window: string
  windowFrom: string
  windowTo: string
  generatedAt: string
  currency: string
  sales: TenantSalesKpis
  cash: TenantCashKpis
  customers: TenantCustomerKpis
  catalog: TenantCatalogKpis
  team: TenantTeamKpis
  usage: TenantUsageKpis
  operations: TenantOperationsKpis
  dataQuality: TenantDashboardDataQuality
}

/**
 * The reduced takings read. **Two money figures, and the server enforces the field set**: a panel
 * that wanted margin or a series would have to call a `reports:view` route.
 */
export interface TenantTakings {
  window: string
  windowFrom: string
  windowTo: string
  generatedAt: string
  currency: string
  collected: number | null
  billedUnconfirmed: number | null
  paymentRowsPresent: boolean
  ledgerBackfilled: boolean
  dataQuality: TenantDashboardDataQuality
}

export interface TenantRevenueBucket {
  bucketStart: string
  grossOrderValue: number | null
  collected: number | null
  refunded: number | null
  isPartial: boolean
}

export interface TenantRevenueSeries {
  bucket: 'day' | 'week' | 'month'
  windowFrom: string
  windowTo: string
  windowCapped: boolean
  points: TenantRevenueBucket[]
}

export interface TenantTopItem {
  itemName: string
  quantity: number
  revenue: number | null
}

export interface TenantTopItems {
  windowFrom: string
  windowTo: string
  limit: number
  items: TenantTopItem[]
}

const dashboardBase = (organizationId: string) => `/api/v1/orgs/${organizationId}/dashboard`

/** The full KPI strip. Requires `reports:view`. */
export async function fetchDashboardSummary(
  organizationId: string,
  window: DashboardWindow,
  signal?: AbortSignal,
): Promise<TenantDashboardSummary> {
  const response = await apiClient.get<TenantDashboardSummary>(`${dashboardBase(organizationId)}/summary`, {
    params: { window },
    signal,
  })
  return response.data
}

/** The reduced takings read. Every boutique role may call it. */
export async function fetchTenantTakings(
  organizationId: string,
  window: DashboardWindow,
  signal?: AbortSignal,
): Promise<TenantTakings> {
  const response = await apiClient.get<TenantTakings>(`${dashboardBase(organizationId)}/takings`, {
    params: { window },
    signal,
  })
  return response.data
}

export async function fetchRevenueSeries(
  organizationId: string,
  options: { from?: string; to?: string; bucket?: 'day' | 'week' | 'month' } = {},
  signal?: AbortSignal,
): Promise<TenantRevenueSeries> {
  const response = await apiClient.get<TenantRevenueSeries>(
    `${dashboardBase(organizationId)}/revenue-series`,
    {
      params: {
        from: options.from || undefined,
        to: options.to || undefined,
        bucket: options.bucket ?? 'day',
      },
      signal,
    },
  )
  return response.data
}

export async function fetchTopItems(
  organizationId: string,
  options: { window?: DashboardWindow; limit?: number } = {},
  signal?: AbortSignal,
): Promise<TenantTopItems> {
  const response = await apiClient.get<TenantTopItems>(`${dashboardBase(organizationId)}/top-items`, {
    params: { window: options.window ?? '30d', limit: options.limit ?? 5 },
    signal,
  })
  return response.data
}

/**
 * Whether the KPI strip's richer panels are worth fetching at all. A caller without `reports:view`
 * gets a 403 from the strip routes, so the client asks before it calls and the panel is **hidden**
 * rather than rendered broken.
 */
export function canReadDashboardStrip(hasReportsView: boolean): boolean {
  return hasReportsView
}
