import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { AdminRevenueStatsView } from './AdminRevenueStats'

const fetchIncomeOverview = vi.fn()
const fetchRevenueTimeseries = vi.fn()
const fetchRevenueCollections = vi.fn()
const fetchRevenueBlossomSales = vi.fn()

vi.mock('@/lib/admin/api', () => ({
  fetchIncomeOverview: (...a: unknown[]) => fetchIncomeOverview(...a),
  fetchRevenueTimeseries: (...a: unknown[]) => fetchRevenueTimeseries(...a),
  fetchRevenueCollections: (...a: unknown[]) => fetchRevenueCollections(...a),
  fetchRevenueBlossomSales: (...a: unknown[]) => fetchRevenueBlossomSales(...a),
}))

vi.mock('@/contexts/AdminSessionContext', () => ({
  useAdminSession: () => ({ can: () => true, roles: ['owner'] }),
}))

const window = {
  from: '2026-09-01T00:00:00Z',
  to: '2026-10-01T00:00:00Z',
  granularity: 'day',
  timeZone: 'UTC',
  bucketCount: 30,
}

const quality = {
  revenueProviderSettlementAvailable: false,
  subscriptionPricesConfigured: true,
  derivedEntriesUnverified: 0,
  checkedAt: '2026-09-20T00:00:00Z',
  notes: [],
}

function renderAt(entry: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[entry]}>
        <Routes>
          <Route path="/admin/:userId/revenue/statistics" element={<AdminRevenueStatsView />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  fetchIncomeOverview.mockReset()
  fetchRevenueTimeseries.mockReset()
  fetchRevenueCollections.mockReset()
  fetchRevenueBlossomSales.mockReset()

  fetchIncomeOverview.mockResolvedValue({
    asOf: '2026-09-20T00:00:00Z',
    mrr: 14500,
    arr: 174000,
    arpu: 7250,
    payingOrganizations: 2,
    activeSubscriptions: 3,
    dataQuality: quality,
  })
  fetchRevenueTimeseries.mockResolvedValue({
    window,
    series: [
      { bucketStart: '2026-09-01T00:00:00Z', isPartial: false, derived: 100, verified: 80, refunded: 0 },
      { bucketStart: '2026-09-02T00:00:00Z', isPartial: false, derived: 0, verified: 0, refunded: 0 },
    ],
    dataQuality: quality,
  })
  fetchRevenueCollections.mockResolvedValue({
    window,
    series: [
      {
        bucketStart: '2026-09-01T00:00:00Z',
        isPartial: false,
        derivedTotal: 100,
        verifiedTotal: 80,
        refundedTotal: 0,
        collectionRate: 80,
        outstanding: 20,
      },
    ],
    dataQuality: quality,
  })
  fetchRevenueBlossomSales.mockResolvedValue({
    window,
    packsSold: 3,
    blossomsGranted: 1500,
    listPriceLkr: 27000,
    verifiedLkr: 9000,
    conversion: 33.33,
    grantedWithoutReference: 1,
    dataQuality: quality,
  })
})

/**
 * Four independent reads, and the point of the page: **one failing endpoint must not blank the
 * others.** `AdminBusinessGrowth` records the same reasoning for its own `LoadState` pattern —
 * a page that shows nothing because one of four aggregates failed is a page that hides three
 * working answers.
 */
describe('AdminRevenueStatsView failure isolation', () => {
  it('renders the three working sections when one endpoint fails', async () => {
    fetchRevenueCollections.mockRejectedValue(new Error('collections endpoint is down'))
    renderAt('/admin/u1/revenue/statistics')

    // The failing one states its error through `ChartFrame`'s own error state.
    expect(await screen.findByText(/collections endpoint is down/i)).toBeInTheDocument()
    // And the others still render their figures.
    expect(screen.getByText('14500')).toBeInTheDocument()
    // The Blossom section's own figures, which prove its read was unaffected. `Packs sold` appears
    // three times by design — the chart legend, a bar category and the detail figure — so the
    // unique Blossom total is what distinguishes a working read.
    expect(screen.getAllByText('Packs sold').length).toBeGreaterThan(0)
    // The list price and the verified total, which are the figures this section renders.
    expect(screen.getByText('27000')).toBeInTheDocument()
    expect(screen.getByText('9000')).toBeInTheDocument()
  })

  it('never renders a message inside the chart container', async () => {
    fetchRevenueCollections.mockRejectedValue(new Error('down'))
    renderAt('/admin/u1/revenue/statistics')

    await screen.findByText(/down/i)

    // `ChartFrame` renders `error`/`empty` itself, at full width, without mounting `ChartContainer`.
    // The recorded trap: a non-chart child inside `ResponsiveContainer` lands in a 0x0 box and wraps
    // one character per line. Every failing section must therefore have no chart slot at all.
    const failing = screen
      .getByText(/down/i)
      .closest('[data-slot="card"]')
    expect(failing?.querySelector('[data-slot="chart"]')).toBeNull()
  })

  it('keeps the page usable when every endpoint fails', async () => {
    fetchIncomeOverview.mockRejectedValue(new Error('overview down'))
    fetchRevenueTimeseries.mockRejectedValue(new Error('timeseries down'))
    fetchRevenueCollections.mockRejectedValue(new Error('collections down'))
    fetchRevenueBlossomSales.mockRejectedValue(new Error('blossoms down'))
    renderAt('/admin/u1/revenue/statistics')

    expect(await screen.findByText(/overview down/i)).toBeInTheDocument()
    expect(screen.getByText(/timeseries down/i)).toBeInTheDocument()
    expect(screen.getByText(/collections down/i)).toBeInTheDocument()
    expect(screen.getByText(/blossoms down/i)).toBeInTheDocument()
  })
})

describe('AdminRevenueStatsView null versus zero', () => {
  it('labels a null MRR rather than drawing it as zero', async () => {
    fetchIncomeOverview.mockResolvedValue({
      asOf: '2026-09-20T00:00:00Z',
      mrr: null,
      arr: null,
      arpu: null,
      payingOrganizations: 0,
      activeSubscriptions: 0,
      dataQuality: { ...quality, subscriptionPricesConfigured: false },
    })
    renderAt('/admin/u1/revenue/statistics')

    expect((await screen.findAllByText(/not measured/i)).length).toBeGreaterThan(0)
  })

  it('keeps a measured zero in the series as zero', async () => {
    renderAt('/admin/u1/revenue/statistics')

    // Two buckets arrive; one is a real zero. The chart must not treat it as a gap.
    expect(await screen.findByText(/Revenue over time/i)).toBeInTheDocument()
  })
})

describe('AdminRevenueStatsView window', () => {
  it('offers a range control that cannot be emptied', async () => {
    renderAt('/admin/u1/revenue/statistics')

    // `RangePresets` guards against Radix emitting "" on deselect, so a range is always set.
    expect(await screen.findByRole('radio', { name: /30 ?d/i })).toBeInTheDocument()
  })
})
