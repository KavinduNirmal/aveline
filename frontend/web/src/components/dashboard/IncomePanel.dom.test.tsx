import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const fetchIncomeLedger = vi.fn()
const fetchIncomeAccounts = vi.fn()

vi.mock('@/lib/income-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/income-api')>('@/lib/income-api')
  return {
    ...actual,
    fetchIncomeLedger: (...args: unknown[]) => fetchIncomeLedger(...args),
    fetchIncomeAccounts: (...args: unknown[]) => fetchIncomeAccounts(...args),
  }
})

import { IncomePanel } from './IncomePanel'

const ORG = '11111111-1111-1111-1111-111111111111'

const LEDGER = {
  window: {
    from: '2026-09-01T00:00:00Z',
    to: '2026-09-30T00:00:00Z',
    generatedAt: '2026-09-30T00:00:00Z',
  },
  items: [
    {
      id: 'e1',
      kind: 'Sale',
      sourceKind: 'CounterWalkIn',
      sourceRef: 'interaction:1',
      chargeBasis: 'Verified',
      status: 'Recorded',
      currency: 'LKR',
      amount: 18500,
      reason: 'Counter sale of a silk saree today.',
      occurredAt: '2026-09-20T10:00:00Z',
      recordedAt: '2026-09-20T10:00:00Z',
      orderId: null,
      customerId: null,
      paymentId: null,
      recordedByUserId: 'u1',
    },
    {
      id: 'e2',
      kind: 'Sale',
      sourceKind: 'OrderSettlement',
      sourceRef: 'order:9',
      chargeBasis: 'Derived',
      status: 'Recorded',
      currency: 'LKR',
      amount: 30000,
      reason: 'Order 9 reached a paid status; billed value.',
      occurredAt: '2026-09-19T10:00:00Z',
      recordedAt: '2026-09-19T10:00:00Z',
      orderId: 'o9',
      customerId: null,
      paymentId: null,
      recordedByUserId: null,
    },
  ],
  total: 2,
  page: 1,
  pageSize: 50,
  windowCapped: false,
  totals: { saleTotal: 48500, paymentTotal: 0, refundTotal: 0, adjustmentTotal: 0 },
  reconciliation: {
    verifiedTotal: 18500,
    derivedTotal: 30000,
    refundTotal: 0,
    netVerified: 18500,
    unverifiedGap: 30000,
    isReconciled: false,
  },
  currency: 'LKR',
  dataQuality: {
    paymentRowsPresent: true,
    orderCostsComplete: true,
    incomeLedgerBackfilled: false,
    refundAndOutstandingExcludedFromCollected: true,
    checkedAt: '2026-09-30T00:00:00Z',
    notes: ['No payment provider is connected, so `Derived` entries are billed value.'],
  },
}

const ACCOUNTS = {
  window: LEDGER.window,
  items: [{ kind: 'Sale', total: 48500, count: 2 }],
  byPaymentMethod: [],
  reconciliation: LEDGER.reconciliation,
  currency: 'LKR',
  dataQuality: LEDGER.dataQuality,
}

describe('IncomePanel', () => {
  beforeEach(() => {
    fetchIncomeLedger.mockReset().mockResolvedValue(LEDGER)
    fetchIncomeAccounts.mockReset().mockResolvedValue(ACCOUNTS)
  })

  it('renders the two bases as two labelled figures, never as one income number', async () => {
    render(<IncomePanel organizationId={ORG} organizationName="House of Fashions" />)

    expect(await screen.findByText('Reconciliation')).toBeInTheDocument()
    expect(screen.getByText('Collected')).toBeInTheDocument()
    // The label appears both as the reconciliation term and as a register row's basis, which is the
    // point: the same words are used everywhere the distinction is made.
    expect(screen.getAllByText('Billed, unconfirmed').length).toBeGreaterThan(0)
    expect(screen.getByText('Refunded')).toBeInTheDocument()
    // There is no single unlabelled "Income" or "Total" figure on the screen.
    expect(screen.queryByText(/^total income$/i)).not.toBeInTheDocument()
  })

  it('distinguishes a derived row from a verified row in text', async () => {
    render(<IncomePanel organizationId={ORG} organizationName="House of Fashions" />)

    await screen.findByText('Reconciliation')
    expect(screen.getAllByText('Money taken').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Billed, unconfirmed').length).toBeGreaterThan(0)
  })

  it('reports the unverified gap as information, not as an error', async () => {
    render(<IncomePanel organizationId={ORG} organizationName="House of Fashions" />)

    expect(await screen.findByText(/has no confirmation yet/i)).toBeInTheDocument()
  })

  it('says reconciliation is unknown rather than claiming it is consistent', async () => {
    fetchIncomeLedger.mockResolvedValue({
      ...LEDGER,
      items: [],
      total: 0,
      reconciliation: {
        verifiedTotal: null,
        derivedTotal: null,
        refundTotal: null,
        netVerified: null,
        unverifiedGap: null,
        isReconciled: false,
      },
    })
    render(<IncomePanel organizationId={ORG} organizationName="House of Fashions" />)

    expect(await screen.findByText(/reconciliation status unknown/i)).toBeInTheDocument()
    expect(screen.queryByText(/every sale is confirmed/i)).not.toBeInTheDocument()
  })

  it('renders an error state with a retry rather than stale figures', async () => {
    fetchIncomeLedger.mockRejectedValueOnce(new Error('network'))
    render(<IncomePanel organizationId={ORG} organizationName="House of Fashions" />)

    expect(await screen.findByText(/could not load the income register/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument()
  })

  it('renders an empty register that says what will fill it', async () => {
    fetchIncomeLedger.mockResolvedValue({ ...LEDGER, items: [], total: 0 })
    render(<IncomePanel organizationId={ORG} organizationName="House of Fashions" />)

    expect(await screen.findByText(/register is empty for this view/i)).toBeInTheDocument()
  })

  it('re-queries when the basis filter changes', async () => {
    render(<IncomePanel organizationId={ORG} organizationName="House of Fashions" />)
    await screen.findByText('Reconciliation')
    fetchIncomeLedger.mockClear()

    await userEvent.click(screen.getByLabelText(/filter by basis/i))
    await userEvent.click(await screen.findByRole('option', { name: /billed, unconfirmed/i }))

    await waitFor(() =>
      expect(fetchIncomeLedger).toHaveBeenCalledWith(
        ORG,
        expect.objectContaining({ basis: 'Derived', page: 1 }),
        expect.anything(),
      ),
    )
  })

  /**
   * The `Money` span itself, not the container around it: the treatment lives on the figure. Located
   * by the container the figure belongs to, so an assertion cannot drift onto a different amount
   * that happens to share the same text.
   */
  function moneyIn(container: 'td' | 'p' | 'dd', text: string) {
    return screen
      .getAllByText(text)
      .find((el) => el.closest(container) !== null)
      ?.closest('span')
  }

  it('states every money figure in a monospaced, tabular figure set', async () => {
    render(<IncomePanel organizationId={ORG} organizationName="House of Fashions" />)
    await screen.findByText('Reconciliation')

    // The register's amounts, the per-kind totals and the reconciliation figures all share one
    // treatment, so a reader can line up a column of money without the digits shifting width.
    for (const figure of [moneyIn('td', 'LKR 18,500.00'), moneyIn('p', 'LKR 48,500.00')]) {
      expect(figure).toBeDefined()
      expect(figure?.className).toContain('font-mono')
      expect(figure?.className).toContain('tabular-nums')
    }
  })

  it('tints every money figure with the theme primary rather than near-black', async () => {
    render(<IncomePanel organizationId={ORG} organizationName="House of Fashions" />)
    await screen.findByText('Reconciliation')

    // One accent for the section, from the theme, so the figures read as this product's figures.
    // Every place a money figure appears is asserted, because a single un-tinted one is what makes a
    // screen look half-finished: a register row, a per-kind total, and each reconciliation figure.
    const figures = [
      moneyIn('td', 'LKR 18,500.00'),
      moneyIn('p', 'LKR 48,500.00'),
      moneyIn('dd', 'LKR 18,500.00'),
      moneyIn('dd', 'LKR 30,000.00'),
      moneyIn('dd', 'LKR 0.00'),
    ]

    for (const figure of figures) {
      expect(figure).toBeDefined()
      expect(figure?.className).toContain('text-primary')
      expect(figure?.className).not.toContain('font-serif')
    }
  })
})
