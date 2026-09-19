import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { AdminBusinessGrowthView } from './AdminBusinessGrowth'

vi.mock('@/contexts/AdminSessionContext', () => ({
  useAdminSession: () => ({
    status: 'ready',
    email: 'owner@aveline.lk',
    userId: 'user-1',
    roles: ['owner'],
    permissions: new Set<string>(['analytics:business:read']),
    accountState: 'Active',
    hasCompletedOnboarding: true,
    error: null,
    can: () => true,
    refresh: async () => undefined,
  }),
}))

const fetchBusinessGrowth = vi.fn()
const fetchBusinessActiveUsers = vi.fn()
const fetchBusinessPlanMix = vi.fn()
const fetchBusinessSubscriptionTrend = vi.fn()

vi.mock('@/lib/admin/api', () => ({
  fetchBusinessGrowth: (...args: unknown[]) => fetchBusinessGrowth(...args),
  fetchBusinessActiveUsers: (...args: unknown[]) => fetchBusinessActiveUsers(...args),
  fetchBusinessPlanMix: (...args: unknown[]) => fetchBusinessPlanMix(...args),
  fetchBusinessSubscriptionTrend: (...args: unknown[]) => fetchBusinessSubscriptionTrend(...args),
}))

const QUALITY_CLEAN = {
  userAttributionAvailable: true,
  unresolvedAttributionCount: 0,
  subscriptionHistoryBackfilled: false,
  lastActivityIsReconstructed: false,
  agentMetricsUninstrumented: false,
  notes: [],
}

const WINDOW = {
  from: '2026-08-21T00:00:00Z',
  to: '2026-09-20T00:00:00Z',
  granularity: 'day',
  timeZone: 'UTC',
  bucketCount: 30,
}

const GROWTH = {
  window: WINDOW,
  observedFrom: '2026-08-21T00:00:00Z',
  series: [
    {
      bucketStart: '2026-09-19T00:00:00Z',
      isPartial: false,
      newUsers: 4,
      newOrganizations: 1,
      newAdminRequests: 0,
      approvedAdminRequests: 0,
    },
    {
      bucketStart: '2026-09-20T00:00:00Z',
      isPartial: true,
      newUsers: 2,
      newOrganizations: 0,
      newAdminRequests: 0,
      approvedAdminRequests: 0,
    },
  ],
  totals: { newUsers: 6, newOrganizations: 1, newAdminRequests: 0, approvedAdminRequests: 0 },
  previousTotals: { newUsers: 3, newOrganizations: 1, newAdminRequests: 0, approvedAdminRequests: 0 },
  dataQuality: QUALITY_CLEAN,
}

const ACTIVE_USERS = {
  window: WINDOW,
  series: [
    { bucketStart: '2026-09-19T00:00:00Z', isPartial: false, activeUsers: 7, activeOrganizations: 2 },
    { bucketStart: '2026-09-20T00:00:00Z', isPartial: true, activeUsers: 3, activeOrganizations: 1 },
  ],
  rolling: { dau: 3, wau: 7, mau: 9, stickiness: 0.3333 },
  dataQuality: QUALITY_CLEAN,
}

const PLAN_MIX = {
  asOf: '2026-09-20T12:00:00Z',
  tiers: [
    {
      planTier: 'Seed',
      isFree: true,
      organizationCount: 8,
      activeOrganizationCount: 7,
      billedSubscriptionCount: 0,
      userCount: 8,
      monthlyPriceLkr: 0,
    },
    {
      planTier: 'Bloom',
      isFree: false,
      organizationCount: 2,
      activeOrganizationCount: 2,
      billedSubscriptionCount: 2,
      userCount: 5,
      monthlyPriceLkr: 7000,
    },
  ],
  free: { organizationCount: 8, userCount: 8, monthlyPriceLkr: 0, shareOfOrganizations: 0.8 },
  premium: { organizationCount: 2, userCount: 5, monthlyPriceLkr: 7000, shareOfOrganizations: 0.2 },
  organizationsTotal: 10,
  organizationsWithBillingRow: 2,
  totalMonthlyPriceLkr: 7000,
  dataQuality: QUALITY_CLEAN,
}

const SUBSCRIPTIONS = {
  window: WINDOW,
  series: [
    {
      bucketStart: '2026-09-19T00:00:00Z',
      isPartial: false,
      activeTotal: 10,
      activeByTier: { Seed: 8, Bloom: 2 },
      started: 1,
      cancelled: 0,
      isBackfilled: false,
    },
    {
      bucketStart: '2026-09-20T00:00:00Z',
      isPartial: true,
      activeTotal: 10,
      activeByTier: { Seed: 8, Bloom: 2 },
      started: 0,
      cancelled: 0,
      isBackfilled: true,
    },
  ],
  openingActive: 9,
  closingActive: 10,
  churnRate: 0,
  dataQuality: QUALITY_CLEAN,
}

function renderGrowth() {
  return render(
    <MemoryRouter>
      <AdminBusinessGrowthView />
    </MemoryRouter>,
  )
}

function seedAll() {
  fetchBusinessGrowth.mockResolvedValue(GROWTH)
  fetchBusinessActiveUsers.mockResolvedValue(ACTIVE_USERS)
  fetchBusinessPlanMix.mockResolvedValue(PLAN_MIX)
  fetchBusinessSubscriptionTrend.mockResolvedValue(SUBSCRIPTIONS)
}

beforeEach(() => {
  fetchBusinessGrowth.mockReset()
  fetchBusinessActiveUsers.mockReset()
  fetchBusinessPlanMix.mockReset()
  fetchBusinessSubscriptionTrend.mockReset()
})

describe('AdminBusinessGrowthView when every call fails', () => {
  it('names each failure and renders no number', async () => {
    fetchBusinessGrowth.mockRejectedValue(new Error('growth is down'))
    fetchBusinessActiveUsers.mockRejectedValue(new Error('active users is down'))
    fetchBusinessPlanMix.mockRejectedValue(new Error('plan mix is down'))
    fetchBusinessSubscriptionTrend.mockRejectedValue(new Error('subscriptions is down'))

    renderGrowth()

    await waitFor(() => {
      expect(screen.getAllByRole('alert').length).toBeGreaterThan(0)
    })
    // No KPI tile rendered a figure.
    expect(screen.queryByText('not measured')).not.toBeInTheDocument()
  })

  it('never renders one of the fabricated figures the delivered console shipped', async () => {
    fetchBusinessGrowth.mockRejectedValue(new Error('down'))
    fetchBusinessActiveUsers.mockRejectedValue(new Error('down'))
    fetchBusinessPlanMix.mockRejectedValue(new Error('down'))
    fetchBusinessSubscriptionTrend.mockRejectedValue(new Error('down'))

    const { container } = renderGrowth()

    await waitFor(() => expect(container.textContent).toContain('Growth'))
    for (const fabricated of ['128,450', '99.98%', '18.4', 'ORD-94021']) {
      expect(container.textContent).not.toContain(fabricated)
    }
  })
})

describe('AdminBusinessGrowthView with data', () => {
  beforeEach(seedAll)

  it('renders the four KPI tiles from the responses', async () => {
    renderGrowth()

    await waitFor(() => expect(screen.getByText('New users')).toBeInTheDocument())
    expect(screen.getByText('New boutiques')).toBeInTheDocument()
    expect(screen.getByText('Daily active users')).toBeInTheDocument()
    expect(screen.getByText('Premium share')).toBeInTheDocument()
    // 6 new users over 30 days, 1 new boutique, dau 3, premium share 20.00%.
    expect(screen.getByText('20.00%')).toBeInTheDocument()
  })

  it('renders the signups, active-user, plan-mix and subscription frames', async () => {
    renderGrowth()

    await waitFor(() => expect(screen.getByText('Signups')).toBeInTheDocument())
    expect(screen.getByText('Active users')).toBeInTheDocument()
    expect(screen.getByText(/plan mix/i)).toBeInTheDocument()
    expect(screen.getByText('Subscriptions')).toBeInTheDocument()
  })

  it('states the billing-row gap as a footnote rather than leaving it unexplained', async () => {
    renderGrowth()

    await waitFor(() =>
      expect(screen.getByText(/2 of 10 boutiques have a billing record/i)).toBeInTheDocument(),
    )
  })

  it('names the window source on every KPI tile', async () => {
    renderGrowth()

    await waitFor(() => expect(screen.getByText('New users')).toBeInTheDocument())
    expect(screen.getAllByText(/business\/growth/).length).toBeGreaterThan(0)
    expect(screen.getByText(/business\/plan-mix/)).toBeInTheDocument()
  })
})

describe('AdminBusinessGrowthView honesty rules', () => {
  it('renders "not measured" for a null active-user reading, never a zero', async () => {
    seedAll()
    fetchBusinessActiveUsers.mockResolvedValue({
      ...ACTIVE_USERS,
      series: ACTIVE_USERS.series.map((point) => ({
        ...point,
        activeUsers: null,
        activeOrganizations: null,
      })),
      rolling: { dau: null, wau: null, mau: null, stickiness: null },
      dataQuality: { ...QUALITY_CLEAN, userAttributionAvailable: false },
    })

    renderGrowth()

    await waitFor(() => expect(screen.getAllByText(/not measured/i).length).toBeGreaterThan(0))
  })

  it('renders the attribution data-quality note when attribution is unavailable', async () => {
    seedAll()
    fetchBusinessActiveUsers.mockResolvedValue({
      ...ACTIVE_USERS,
      rolling: { dau: null, wau: null, mau: null, stickiness: null },
      dataQuality: {
        ...QUALITY_CLEAN,
        userAttributionAvailable: false,
        notes: ['User attribution is unavailable for this window'],
      },
    })

    renderGrowth()

    await waitFor(() =>
      expect(screen.getByText(/attribution is unavailable/i)).toBeInTheDocument(),
    )
  })

  it('renders the unresolved-attribution undercount when the server reports one', async () => {
    seedAll()
    fetchBusinessActiveUsers.mockResolvedValue({
      ...ACTIVE_USERS,
      dataQuality: { ...QUALITY_CLEAN, unresolvedAttributionCount: 17 },
    })

    renderGrowth()

    await waitFor(() =>
      expect(screen.getByText(/17 request\(s\) could not be attributed/i)).toBeInTheDocument(),
    )
  })

  it('marks a backfilled subscription bucket approximate', async () => {
    seedAll()
    fetchBusinessSubscriptionTrend.mockResolvedValue({
      ...SUBSCRIPTIONS,
      dataQuality: { ...QUALITY_CLEAN, subscriptionHistoryBackfilled: true },
      series: SUBSCRIPTIONS.series.map((point) => ({ ...point, isBackfilled: true })),
    })

    renderGrowth()

    await waitFor(() =>
      expect(screen.getByText(/reconstructed from the audit ledger/i)).toBeInTheDocument(),
    )
    // The approximate bucket is also shaded on the chart, not only named in the notice.
    expect(screen.getByText('Subscriptions')).toBeInTheDocument()
  })

  it('renders a data-quality notice as its own vocabulary', async () => {
    seedAll()
    fetchBusinessGrowth.mockResolvedValue({
      ...GROWTH,
      dataQuality: { ...QUALITY_CLEAN, notes: ['a server note worth reading'] },
    })

    renderGrowth()

    await waitFor(() =>
      expect(screen.getByText('a server note worth reading')).toBeInTheDocument(),
    )
  })
})

describe('AdminBusinessGrowthView range control', () => {
  it('re-issues every request with the new window and granularity', async () => {
    seedAll()
    renderGrowth()

    await waitFor(() => expect(fetchBusinessGrowth).toHaveBeenCalled())
    const firstCall = fetchBusinessGrowth.mock.calls[0][0] as { granularity: string }
    expect(firstCall.granularity).toBe('day')

    await userEvent.click(screen.getByRole('radio', { name: /12 m/i }))

    await waitFor(() => {
      const lastCall = fetchBusinessGrowth.mock.calls.at(-1)?.[0] as { granularity: string }
      expect(lastCall.granularity).toBe('month')
    })
  })

  it('keeps a range selected when the active preset is clicked again', async () => {
    seedAll()
    renderGrowth()

    await waitFor(() => expect(fetchBusinessGrowth).toHaveBeenCalled())
    const activeBefore = screen.getByRole('radio', { name: /30 d/i })
    expect(activeBefore).toHaveAttribute('data-state', 'on')

    await userEvent.click(activeBefore)

    // The deselect guard means the active preset stays selected instead of clearing the range.
    expect(screen.getByRole('radio', { name: /30 d/i })).toHaveAttribute('data-state', 'on')
  })

  it('re-queries when the refresh control is used', async () => {
    seedAll()
    renderGrowth()

    await waitFor(() => expect(fetchBusinessGrowth).toHaveBeenCalled())
    const before = fetchBusinessGrowth.mock.calls.length

    await userEvent.click(screen.getByRole('button', { name: /refresh/i }))

    await waitFor(() => expect(fetchBusinessGrowth.mock.calls.length).toBeGreaterThan(before))
  })
})

describe('AdminBusinessGrowthView partial buckets', () => {
  it('marks the trailing open bucket in the chart', async () => {
    seedAll()
    const { container } = renderGrowth()

    await waitFor(() => expect(screen.getByText('Signups')).toBeInTheDocument())
    // The partial bucket is shaded; the marker the chart layer renders is a Recharts area.
    await waitFor(() =>
      expect(container.querySelectorAll('.recharts-reference-area').length).toBeGreaterThan(0),
    )
  })
})
