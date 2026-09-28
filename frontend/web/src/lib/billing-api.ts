import { apiClient } from '@/lib/api'

/**
 * The tenant billing and usage surface (docs/api/README.md C.2, C.3, C.4, B.23).
 *
 * Three rules are encoded in the types rather than left to the panels:
 *
 * 1. **`null` is "not measured", never `0`.** Every nullable measure is `number | null`; the panels
 *    render them through `formatMoney` / `formatCount` / `formatPercent`, which is the one place
 *    that distinction becomes text.
 * 2. **A zero plan price is not a price.** `OrganizationSubscription.PriceLkr` is never assigned in
 *    the product, so `BillingPeriod.subscriptionPricesConfigured` gates `planListPrice()`; a panel
 *    cannot render the raw column by accident (C-4).
 * 3. **No invoice exists.** The endpoints here return a statement of account and a price-book
 *    catalogue; there is no invoice, no provider client and no currency column to read (D8).
 */

export interface BlossomBalance {
  organizationId: string
  periodStart: string
  periodEnd: string
  periodIsClosed: boolean
  planTier: string | null
  monthlyBlossomLimit: number
  blossomGranted: number
  blossomAdjusted: number
  blossomUsed: number
  blossomRemaining: number
  percentUsed: number
  lowBalanceThresholdPercent: number
  asOf: string
}

export interface BlossomUsagePoint {
  key: string
  blossoms: number
  normalizedUnits: number
  workflowCount: number
}

export interface BlossomUsage {
  from: string
  to: string
  totalBlossoms: number
  totalNormalizedUnits: number
  series: BlossomUsagePoint[]
  generatedAt: string
}

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
  availableToRevoke: number | null
  provider: string | null
  model: string | null
  normalizedUnits: number | null
  actualCostUsd: number | null
}

export interface BlossomStatementReconciliation {
  projectedBalance: number
  ledgerDerivedBalance: number
  drift: number
  isConsistent: boolean
}

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
  total: number
  page: number
  pageSize: number
  closingBalance: number
  reconciliation: BlossomStatementReconciliation
  generatedAt: string
  maxWindowDays: number
  dataQuality: BlossomStatementDataQuality | null
}

/**
 * E-12. A billing period. `planListPriceLkr` is `null` — never `0` — when no price is configured,
 * and `subscriptionPricesConfigured` says whether one was.
 */
export interface BillingPeriod {
  periodStart: string
  periodEnd: string
  isClosed: boolean
  planTier: string | null
  hasSubscriptionRow: boolean
  monthlyBlossomLimit: number
  blossomGranted: number
  blossomAdjusted: number
  blossomUsed: number
  blossomRemaining: number
  planListPriceLkr: number | null
  subscriptionPricesConfigured: boolean
  topUpBlossoms: number
  topUpCount: number
}

/** E-11. A purchasable top-up pack, from the same price-book lookup the purchase uses. */
export interface TopUpPack {
  skuCode: string
  blossomQuantity: number
  priceLkr: number
  currency: string
}

export interface SubscriptionView {
  organizationId: string
  planTier: string
  billingCycle: string
  status: string
  currentPeriodStart: string
  currentPeriodEnd: string
  seatsIncluded: number
  priceLkr: number
  currency: string
  cancelAtPeriodEnd: boolean
  cancelledAt: string | null
  externalProvider: string | null
}

export interface EntitlementItem {
  key: string
  valueType: string
  /** A JSON scalar; the server stores strings, numbers and booleans in one column. */
  value: unknown
  source: string
  effectiveFrom: string
}

export interface EntitlementUsageItem {
  key: string
  observed: number
  allowed: number
  percentUsed: number
  hardLimit: boolean
}

export interface EntitlementUsage {
  items: EntitlementUsageItem[]
  materialisedAt: string
  dataQuality: { materialisedCounts: boolean }
}

export interface BurnRate {
  window: string
  burnRatePerDay: number
  projectedExhaustionAt: string | null
  currentBalance: number
  averageDailyUsage: number
  dataQuality: {
    latencyInstrumented: boolean
    nodeFailuresObserved: boolean
    perStepAttribution: boolean
    toolInstrumented: boolean
    costInstrumented: boolean
  }
}

const orgBase = (organizationId: string) => `/api/v1/orgs/${organizationId}`

/** The current period's Blossom position. Every boutique role may call this (`billing:view:self`). */
export async function fetchBlossomBalance(
  organizationId: string,
  signal?: AbortSignal,
): Promise<BlossomBalance> {
  const response = await apiClient.get<BlossomBalance>(
    `${orgBase(organizationId)}/blossoms/balance`,
    { signal },
  )
  return response.data
}

/** The daily Blossom consumption series. Requires `billing:view`. */
export async function fetchBlossomUsage(
  organizationId: string,
  options: { from?: string; to?: string; groupBy?: string } = {},
  signal?: AbortSignal,
): Promise<BlossomUsage> {
  const response = await apiClient.get<BlossomUsage>(
    `${orgBase(organizationId)}/blossoms/usage`,
    {
      params: {
        from: options.from || undefined,
        to: options.to || undefined,
        groupBy: options.groupBy ?? 'day',
      },
      signal,
    },
  )
  return response.data
}

export interface BlossomStatementFilters {
  from?: string
  to?: string
  entryType?: string
  kind?: string
  q?: string
  page?: number
  pageSize?: number
}

/**
 * The statement of account. It **pages in the database** (Revenue Ledger R4), so the tenant panel
 * requests a page exactly as the admin console does and does not cap its own window.
 */
export async function fetchBlossomStatement(
  organizationId: string,
  filters: BlossomStatementFilters = {},
  signal?: AbortSignal,
): Promise<BlossomStatement> {
  const response = await apiClient.get<BlossomStatement>(
    `${orgBase(organizationId)}/blossoms/statement`,
    {
      params: {
        from: filters.from || undefined,
        to: filters.to || undefined,
        entryType: filters.entryType || undefined,
        kind: filters.kind || undefined,
        q: filters.q || undefined,
        page: filters.page ?? 1,
        pageSize: filters.pageSize ?? 25,
      },
      signal,
    },
  )
  return response.data
}

/** E-12. Requires `billing:view`. */
export async function fetchBillingPeriods(
  organizationId: string,
  take = 12,
  signal?: AbortSignal,
): Promise<BillingPeriod[]> {
  const response = await apiClient.get<BillingPeriod[]>(
    `${orgBase(organizationId)}/billing/periods`,
    { params: { take }, signal },
  )
  return response.data
}

/** E-11. Requires `billing:manage` — the same permission the purchase needs. */
export async function fetchTopUpPacks(
  organizationId: string,
  signal?: AbortSignal,
): Promise<TopUpPack[]> {
  const response = await apiClient.get<TopUpPack[]>(
    `${orgBase(organizationId)}/blossoms/top-up-packs`,
    { signal },
  )
  return response.data
}

export async function fetchSubscription(
  organizationId: string,
  signal?: AbortSignal,
): Promise<SubscriptionView> {
  const response = await apiClient.get<SubscriptionView>(
    `${orgBase(organizationId)}/subscription`,
    { signal },
  )
  return response.data
}

export async function fetchEntitlements(
  organizationId: string,
  signal?: AbortSignal,
): Promise<EntitlementItem[]> {
  const response = await apiClient.get<EntitlementItem[]>(
    `${orgBase(organizationId)}/entitlements`,
    { signal },
  )
  return response.data
}

export async function fetchEntitlementUsage(
  organizationId: string,
  signal?: AbortSignal,
): Promise<EntitlementUsage> {
  const response = await apiClient.get<EntitlementUsage>(
    `${orgBase(organizationId)}/entitlements/usage`,
    { signal },
  )
  return response.data
}

export async function fetchBurnRate(
  organizationId: string,
  signal?: AbortSignal,
): Promise<BurnRate> {
  const response = await apiClient.get<BurnRate>(
    `${orgBase(organizationId)}/billing/burn-rate`,
    { signal },
  )
  return response.data
}

/**
 * Purchases a top-up pack by SKU. A top-up is a **grant, not a charge**: no payment provider is
 * connected, so the only evidence of payment is the `paymentReference` the operator supplies.
 *
 * The request carries the same `Idempotency-Key` the server requires, so a retried click cannot
 * grant the pack twice.
 */
export async function purchaseTopUp(
  organizationId: string,
  skuCode: string,
  idempotencyKey: string,
  paymentReference?: string,
  signal?: AbortSignal,
): Promise<BlossomStatementItem> {
  const response = await apiClient.post<BlossomStatementItem>(
    `${orgBase(organizationId)}/blossoms/top-ups`,
    paymentReference ? { skuCode, paymentReference } : { skuCode },
    { headers: { 'Idempotency-Key': idempotencyKey }, signal },
  )
  return response.data
}

/**
 * The plan list price for a period, or `null` when the server did not configure one.
 *
 * This is the C-4 guard: the stored `PriceLkr` column is never assigned anywhere in the product, so
 * reading it directly would print `LKR 0` as a plan price for every subscription. A zero is treated
 * as "not configured", exactly as the server does.
 */
export function planListPrice(period: BillingPeriod): number | null {
  if (!period.hasSubscriptionRow || !period.subscriptionPricesConfigured) {
    return null
  }

  const price = period.planListPriceLkr
  return price === null || price <= 0 ? null : price
}

/**
 * Reads an entitlement value as a number, or `null` when it is not numeric. A string entitlement
 * (a tier name, a boolean flag) is not a limit and must not be coerced into one.
 */
export function entitlementNumber(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null
}

/**
 * A limit-vs-observed percentage, or `null` when there is no limit to measure against. A zero or
 * absent allowance is "no limit stated", never "0% used".
 */
export function limitPercent(observed: number | null, allowed: number | null): number | null {
  if (observed === null || allowed === null || allowed <= 0) {
    return null
  }

  return (observed / allowed) * 100
}
