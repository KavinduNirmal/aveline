import { apiClient } from '@/lib/api'

/**
 * The boutique income surface (docs/api/README.md B.21).
 *
 * Two rules are encoded in the types, not just in the copy:
 *
 * 1. **Every measure is `number | null`.** `null` is "not measured" and is never rendered as `0`;
 *    `formatMoney` is the one place that distinction is turned into text.
 * 2. **`derivedTotal` and `verifiedTotal` are separate fields with no combined total.** There is
 *    deliberately no `total` a caller could render on its own, because the whole point of the
 *    surface is that billed value and money taken are different facts.
 */

export type BoutiqueSaleKind = 'Sale' | 'PaymentReceived' | 'Refund' | 'Adjustment'
export type BoutiqueSaleBasis = 'Derived' | 'Verified'
export type BoutiqueSaleSourceKind =
  | 'CounterWalkIn'
  | 'OrderPayment'
  | 'OrderSettlement'
  | 'Refund'
  | 'System'
export type BoutiqueSaleStatus = 'Recorded' | 'Voided'

export interface BoutiqueIncomeWindow {
  from: string
  to: string
  generatedAt: string
}

export interface BoutiqueIncomeDataQuality {
  paymentRowsPresent: boolean
  orderCostsComplete: boolean
  incomeLedgerBackfilled: boolean
  refundAndOutstandingExcludedFromCollected: boolean
  checkedAt: string
  notes: string[]
}

export interface BoutiqueIncomeTotals {
  saleTotal: number | null
  paymentTotal: number | null
  refundTotal: number | null
  adjustmentTotal: number | null
}

export interface BoutiqueIncomeReconciliation {
  verifiedTotal: number | null
  derivedTotal: number | null
  refundTotal: number | null
  netVerified: number | null
  /** Equal to `derivedTotal`; the headline honesty number, not an error. */
  unverifiedGap: number | null
  isReconciled: boolean
}

export interface BoutiqueIncomeEntry {
  id: string
  kind: BoutiqueSaleKind
  sourceKind: BoutiqueSaleSourceKind
  sourceRef: string | null
  chargeBasis: BoutiqueSaleBasis
  status: BoutiqueSaleStatus
  currency: string
  amount: number
  reason: string
  occurredAt: string
  recordedAt: string
  orderId: string | null
  customerId: string | null
  paymentId: string | null
  recordedByUserId: string | null
}

export interface BoutiqueIncomeLedgerPage {
  window: BoutiqueIncomeWindow
  items: BoutiqueIncomeEntry[]
  total: number
  page: number
  pageSize: number
  windowCapped: boolean
  totals: BoutiqueIncomeTotals
  reconciliation: BoutiqueIncomeReconciliation
  currency: string
  dataQuality: BoutiqueIncomeDataQuality
}

export interface BoutiqueIncomeKindTotal {
  kind: BoutiqueSaleKind
  total: number
  count: number
}

export interface BoutiqueIncomePaymentMethodTotal {
  paymentMethod: string
  total: number
  count: number
}

export interface BoutiqueIncomeAccounts {
  window: BoutiqueIncomeWindow
  items: BoutiqueIncomeKindTotal[]
  byPaymentMethod: BoutiqueIncomePaymentMethodTotal[]
  reconciliation: BoutiqueIncomeReconciliation
  currency: string
  dataQuality: BoutiqueIncomeDataQuality
}

export interface IncomeLedgerFilters {
  from?: string
  to?: string
  kind?: BoutiqueSaleKind
  basis?: BoutiqueSaleBasis
  q?: string
  page?: number
  pageSize?: number
}

const incomeBase = (organizationId: string) => `/api/v1/orgs/${organizationId}/income`

/** The paged register, with the window's totals and the two-basis reconciliation attached. */
export async function fetchIncomeLedger(
  organizationId: string,
  filters: IncomeLedgerFilters = {},
  signal?: AbortSignal,
): Promise<BoutiqueIncomeLedgerPage> {
  const response = await apiClient.get<BoutiqueIncomeLedgerPage>(`${incomeBase(organizationId)}/ledger`, {
    params: {
      from: filters.from || undefined,
      to: filters.to || undefined,
      kind: filters.kind || undefined,
      basis: filters.basis || undefined,
      q: filters.q || undefined,
      page: filters.page ?? 1,
      pageSize: filters.pageSize ?? 50,
    },
    signal,
  })
  return response.data
}

/** The window's takings broken down by kind, plus the payment-method split for cash entries. */
export async function fetchIncomeAccounts(
  organizationId: string,
  filters: { from?: string; to?: string } = {},
  signal?: AbortSignal,
): Promise<BoutiqueIncomeAccounts> {
  const response = await apiClient.get<BoutiqueIncomeAccounts>(
    `${incomeBase(organizationId)}/accounts`,
    {
      params: { from: filters.from || undefined, to: filters.to || undefined },
      signal,
    },
  )
  return response.data
}

/**
 * Whether the ledger's two bases can honestly be described as reconciled. A gap is **not** an error:
 * it is billed value the shop has no evidence of collecting yet.
 */
export function hasUnverifiedGap(reconciliation: BoutiqueIncomeReconciliation): boolean {
  return !reconciliation.isReconciled && (reconciliation.unverifiedGap ?? 0) > 0
}

/**
 * The collected figure as a labelled pair, never a single number: money taken, and the refunds that
 * came out of it. A caller that wants one number must take `net` knowingly.
 */
export function collectedBreakdown(reconciliation: BoutiqueIncomeReconciliation): {
  collected: number | null
  refunds: number | null
  net: number | null
} {
  return {
    collected: reconciliation.verifiedTotal,
    refunds: reconciliation.refundTotal,
    net: reconciliation.netVerified,
  }
}
