import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { RevenueOverview } from '@/types/admin'
import { AdminRevenueView } from './AdminRevenue'

const fetchIncomeOverview = vi.fn()
const fetchIncomeAccounts = vi.fn()

vi.mock('@/lib/admin/api', () => ({
  fetchIncomeOverview: (...args: unknown[]) => fetchIncomeOverview(...args),
  fetchIncomeAccounts: (...args: unknown[]) => fetchIncomeAccounts(...args),
}))

let grantedPermissions: string[] = ['revenue:read']
vi.mock('@/contexts/AdminSessionContext', () => ({
  useAdminSession: () => ({
    can: (permission: string) => grantedPermissions.includes(permission),
    roles: ['owner'],
  }),
}))

function overview(overrides: Partial<RevenueOverview> = {}): RevenueOverview {
  return {
    asOf: '2026-09-20T00:00:00Z',
    mrr: 14500,
    arr: 174000,
    arpu: 7250,
    payingOrganizations: 2,
    activeSubscriptions: 3,
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

function renderAt(entry: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[entry]}>
        <Routes>
          <Route path="/admin/:userId/revenue" element={<AdminRevenueView />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  grantedPermissions = ['revenue:read']
  fetchIncomeOverview.mockReset()
  fetchIncomeAccounts.mockReset()
  fetchIncomeOverview.mockResolvedValue(overview())
  fetchIncomeAccounts.mockResolvedValue({
    window: {
      from: '2026-09-01T00:00:00Z',
      to: '2026-10-01T00:00:00Z',
      granularity: 'day',
      timeZone: 'UTC',
      bucketCount: 30,
    },
    items: [],
    total: 0,
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
  })
})

/**
 * The domain landing page: three headline figures and the way in to the two children.
 *
 * The load-bearing rule is the null-versus-zero one. `MRR` is `null` while every `PriceLkr` is `0`,
 * and a `0` would read as "we earn nothing" when the truth is "no price is configured" — so the
 * page must render *"not measured"* and never a zero.
 */
describe('AdminRevenueView', () => {
  it('renders MRR, ARR and the paying-organization count', async () => {
    renderAt('/admin/u1/revenue')

    expect(await screen.findByText('14500')).toBeInTheDocument()
    expect(screen.getByText('174000')).toBeInTheDocument()
    // `count` formats with the locale, which is the point of the unit.
    expect(screen.getByText('2')).toBeInTheDocument()
    // And the provider fact is stated rather than left to inference.
    expect(screen.getByText(/no payment provider/i)).toBeInTheDocument()
  })

  it('renders a null MRR as "not measured" rather than zero', async () => {
    fetchIncomeOverview.mockResolvedValue(
      overview({
        mrr: null,
        arr: null,
        arpu: null,
        payingOrganizations: 0,
        dataQuality: {
          revenueProviderSettlementAvailable: false,
          subscriptionPricesConfigured: false,
          derivedEntriesUnverified: 0,
          checkedAt: '2026-09-20T00:00:00Z',
          notes: [],
        },
      }),
    )
    renderAt('/admin/u1/revenue')

    // Wait for the notes block, which only renders once the query has settled.
    expect(await screen.findByText(/no payment provider/i)).toBeInTheDocument()

    // MRR and ARR are both `null` and both render "not measured" — never a formatted zero.
    // `Card` carries `data-slot`, so the tiles are addressable without a bespoke test id.
    // MRR and ARR are both `null` and both render "not measured" — never a formatted zero.
    const tiles = Array.from(document.querySelectorAll('[data-slot="card"]')).map(
      (card) => card.textContent ?? '',
    )
    const moneyTiles = tiles.filter((text) => /^MRR|^ARR/.test(text))
    expect(moneyTiles.length).toBe(2)
    expect(moneyTiles.every((text) => /not measured/.test(text))).toBe(true)

    // `payingOrganizations: 0` is a **measured** zero and stays `0`: the rule is about a measure
    // that could not be computed, not about a count that is genuinely nothing.
    const paying = tiles.find((text) => /^Paying organizations/.test(text))
    expect(paying).toContain('0')
  })

  it('says the list price is missing rather than blaming demand', async () => {
    fetchIncomeOverview.mockResolvedValue(
      overview({
        mrr: null,
        arr: null,
        arpu: null,
        dataQuality: {
          revenueProviderSettlementAvailable: false,
          subscriptionPricesConfigured: false,
          derivedEntriesUnverified: 0,
          checkedAt: '2026-09-20T00:00:00Z',
          notes: [],
        },
      }),
    )
    renderAt('/admin/u1/revenue')

    // The provider note proves the query settled; only then is the absence of "not measured"
    // meaningful, because an unresolved tile also renders it.
    expect(await screen.findByText(/no payment provider/i)).toBeInTheDocument()
    expect(screen.getByText(/no list price is configured/i)).toBeInTheDocument()
  })

  it('states that no provider settles money, so nothing here is collected', async () => {
    renderAt('/admin/u1/revenue')

    expect(await screen.findByText(/no payment provider/i)).toBeInTheDocument()
  })

  it('links to the register and the statistics surface', async () => {
    renderAt('/admin/u1/revenue')

    expect(await screen.findByRole('link', { name: /income ledger/i })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /payments statistics/i })).toBeInTheDocument()
  })

  /**
   * The gate, asserted from both halves: a caller without `revenue:read` renders the section and
   * issues **no** request. A `403` in the network log is a worse experience than an absent page.
   */
  it('renders nothing and issues no request without revenue:read', async () => {
    grantedPermissions = []
    renderAt('/admin/u1/revenue')

    expect(await screen.findByText(/not available/i)).toBeInTheDocument()
    expect(fetchIncomeOverview).not.toHaveBeenCalled()
    expect(fetchIncomeAccounts).not.toHaveBeenCalled()
  })
})
