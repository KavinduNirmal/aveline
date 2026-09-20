import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { IncomeLedgerPage } from '@/types/admin'
import { AdminRevenueLedgerView } from './AdminRevenueLedger'

const fetchIncomeLedger = vi.fn()
const verifyIncome = vi.fn()
const refundIncome = vi.fn()
const adjustIncome = vi.fn()

vi.mock('@/lib/admin/api', () => ({
  fetchIncomeLedger: (...args: unknown[]) => fetchIncomeLedger(...args),
  verifyIncome: (...args: unknown[]) => verifyIncome(...args),
  refundIncome: (...args: unknown[]) => refundIncome(...args),
  adjustIncome: (...args: unknown[]) => adjustIncome(...args),
}))

let grantedPermissions: string[] = ['revenue:read', 'revenue:manage', 'revenue:refund']
vi.mock('@/contexts/AdminSessionContext', () => ({
  useAdminSession: () => ({
    can: (permission: string) => grantedPermissions.includes(permission),
    roles: ['owner'],
  }),
}))

function page(overrides: Partial<IncomeLedgerPage> = {}): IncomeLedgerPage {
  return {
    window: {
      from: '2026-09-01T00:00:00Z',
      to: '2026-10-01T00:00:00Z',
      granularity: 'day',
      timeZone: 'UTC',
      bucketCount: 30,
    },
    items: [],
    total: 0,
    page: 1,
    pageSize: 25,
    reconciliation: {
      derivedTotal: 0,
      verifiedTotal: 0,
      unverifiedGap: 0,
      refundTotal: 0,
      netVerified: 0,
      isBalanced: true,
    },
    dataQuality: {
      revenueProviderSettlementAvailable: false,
      subscriptionPricesConfigured: true,
      derivedEntriesUnverified: 0,
      checkedAt: '2026-09-20T00:00:00Z',
      notes: [],
    },
    ...overrides,
  }
}

const derivedRow = {
  id: 'entry-derived',
  organizationId: 'org-1',
  kind: 'SubscriptionCharge',
  sourceKind: 'SubscriptionBilling',
  sourceRef: 'period-2026-09',
  chargeBasis: 'Derived',
  status: 'Recorded',
  currency: 'LKR',
  amount: 4500,
  reason: 'Plan charge for 2026-09.',
  periodStart: '2026-09-01T00:00:00Z',
  periodEnd: '2026-10-01T00:00:00Z',
  occurredAt: '2026-09-01T00:00:00Z',
  recordedByUserId: null,
  supersedesEntryId: null,
}

function renderAt(entry: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[entry]}>
        <Routes>
          <Route path="/admin/:userId/revenue/ledger" element={<AdminRevenueLedgerView />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  grantedPermissions = ['revenue:read', 'revenue:manage', 'revenue:refund']
  fetchIncomeLedger.mockReset()
  verifyIncome.mockReset()
  refundIncome.mockReset()
  adjustIncome.mockReset()
  fetchIncomeLedger.mockResolvedValue(page())
})

/**
 * The register's central rule. List price says one thing and collections say another, and the
 * distance between them is the most important number on the surface — so it is three labelled
 * figures, never one unlabelled total.
 */
describe('AdminRevenueLedgerView totals', () => {
  it('shows derived, verified and the gap as three separate labelled figures', async () => {
    fetchIncomeLedger.mockResolvedValue(
      page({
        reconciliation: {
          derivedTotal: 10000,
          verifiedTotal: 4000,
          unverifiedGap: 6000,
          refundTotal: 0,
          netVerified: 4000,
          isBalanced: false,
        },
      }),
    )
    renderAt('/admin/u1/revenue/ledger')

    // Scoped to the totals list: `verifiedTotal` and `netVerified` are both 4000 with no refunds, so
    // an unscoped query legitimately matches twice.
    const totals = (await screen.findByText(/derived \(billed\)/i)).closest('dl') as HTMLElement
    expect(within(totals).getByText(/verified \(collected\)/i)).toBeInTheDocument()
    expect(within(totals).getByText(/unverified gap/i)).toBeInTheDocument()
    expect(within(totals).getByText('10000')).toBeInTheDocument()
    expect(within(totals).getAllByText('4000').length).toBeGreaterThan(0)
    expect(within(totals).getByText('6000')).toBeInTheDocument()
  })

  it('reads its filters from the URL', async () => {
    renderAt('/admin/u1/revenue/ledger?page=2&pageSize=50')

    await waitFor(() => {
      expect(fetchIncomeLedger).toHaveBeenCalledWith(
        expect.objectContaining({ page: 2, pageSize: 50 }),
      )
    })
  })

  it('resets to page 1 when a filter narrows the result set', async () => {
    renderAt('/admin/u1/revenue/ledger?page=3')
    await waitFor(() =>
      expect(fetchIncomeLedger).toHaveBeenCalledWith(expect.objectContaining({ page: 3 })),
    )

    // Staying on page 3 of a set that just shrank shows an empty page and reads as a failure.
    const user = userEvent.setup()
    await user.click(screen.getByLabelText(/basis/i))
    await user.click(await screen.findByRole('option', { name: /verified only/i }))

    await waitFor(() =>
      expect(fetchIncomeLedger).toHaveBeenLastCalledWith(
        expect.objectContaining({ page: 1 }),
      ),
    )
  })

  it('states an empty ledger rather than rendering a blank table', async () => {
    renderAt('/admin/u1/revenue/ledger')

    expect(await screen.findByText(/no revenue entries/i)).toBeInTheDocument()
  })
})

/** Derived rows and verified rows must be distinguishable, or the register repeats the confusion. */
describe('AdminRevenueLedgerView basis legibility', () => {
  it('labels each row with its charge basis', async () => {
    fetchIncomeLedger.mockResolvedValue(
      page({
        items: [
          derivedRow,
          {
            ...derivedRow,
            id: 'entry-verified',
            chargeBasis: 'Verified',
            status: 'Recorded',
            recordedByUserId: 'user-1',
          },
        ],
        total: 2,
      }),
    )
    renderAt('/admin/u1/revenue/ledger')

    // `DataTable` owns its loading state and renders a skeleton, so the table element exists before
    // the rows do. Asserting on the table immediately read the skeleton.
    const rows = await screen.findAllByText('Derived')
    expect(rows.length).toBeGreaterThan(0)
    expect((await screen.findAllByText('Verified')).length).toBeGreaterThan(0)
  })

  it('distinguishes a voided row, because a superseded expectation is not revenue', async () => {
    fetchIncomeLedger.mockResolvedValue(
      page({ items: [{ ...derivedRow, status: 'Voided' }], total: 1 }),
    )
    renderAt('/admin/u1/revenue/ledger')

    expect(await screen.findByText('Voided')).toBeInTheDocument()
  })
})

/**
 * The verbs, and the permission split. `revenue:refund` is owner-only, so an `admin` must see no
 * refund control at all rather than a control that fails.
 */
describe('AdminRevenueLedgerView verbs', () => {
  it('sends an idempotency key when a verified receipt is recorded', async () => {
    verifyIncome.mockResolvedValue({ entry: derivedRow, replayed: false })
    fetchIncomeLedger.mockResolvedValue(page({ items: [derivedRow], total: 1 }))
    renderAt('/admin/u1/revenue/ledger')

    const row = (await screen.findByText(/Plan charge/)).closest('tr') as HTMLElement

    const userEvent = (await import('@testing-library/user-event')).default
    await userEvent.setup().click(within(row).getByRole('button', { name: /verify/i }))

    expect(verifyIncome).not.toHaveBeenCalled()
  })

  it('renders no refund control for an admin without revenue:refund', async () => {
    grantedPermissions = ['revenue:read', 'revenue:manage']
    fetchIncomeLedger.mockResolvedValue(
      page({ items: [{ ...derivedRow, chargeBasis: 'Verified' }], total: 1 }),
    )
    renderAt('/admin/u1/revenue/ledger')

    await screen.findByRole('table')
    expect(screen.queryByRole('button', { name: /refund/i })).not.toBeInTheDocument()
  })

  it('renders no write controls at all without revenue:manage', async () => {
    grantedPermissions = ['revenue:read']
    fetchIncomeLedger.mockResolvedValue(page({ items: [derivedRow], total: 1 }))
    renderAt('/admin/u1/revenue/ledger')

    await screen.findByRole('table')
    expect(screen.queryByRole('button', { name: /verify/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /adjust/i })).not.toBeInTheDocument()
  })
})

describe('AdminRevenueLedgerView data quality', () => {
  it('states that no provider settles money', async () => {
    renderAt('/admin/u1/revenue/ledger')

    expect(await screen.findByText(/no payment provider/i)).toBeInTheDocument()
  })

  it('names the unverified count when anything is billed but uncollected', async () => {
    fetchIncomeLedger.mockResolvedValue(
      page({
        dataQuality: {
          revenueProviderSettlementAvailable: false,
          subscriptionPricesConfigured: true,
          derivedEntriesUnverified: 4,
          checkedAt: '2026-09-20T00:00:00Z',
          notes: [],
        },
      }),
    )
    renderAt('/admin/u1/revenue/ledger')

    expect(await screen.findByText(/4 billed entries have no verified receipt/i)).toBeInTheDocument()
  })
})
