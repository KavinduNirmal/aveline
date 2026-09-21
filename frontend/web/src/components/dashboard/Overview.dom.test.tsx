import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const fetchTenantTakings = vi.fn()
const fetchDashboardSummary = vi.fn()

vi.mock('@/lib/dashboard-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/dashboard-api')>('@/lib/dashboard-api')
  return {
    ...actual,
    fetchTenantTakings: (...args: unknown[]) => fetchTenantTakings(...args),
    fetchDashboardSummary: (...args: unknown[]) => fetchDashboardSummary(...args),
  }
})

vi.mock('@clerk/react', () => ({
  useUser: () => ({ user: { firstName: 'Kavindu' } }),
}))

import { Overview } from './Overview'

const ORGANIZATION = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'House of Fashions',
  slug: 'house-of-fashions',
  clerkOrgId: null,
  ownerUserId: 'u-1',
  address: null,
  phoneNumber: null,
  description: 'Bridal and occasion wear.',
  logoUrl: null,
  planTier: 'Bloom',
  hasCompletedOnboarding: true,
  createdAt: '2026-01-01T00:00:00Z',
} as never

const QUALITY = {
  paymentRowsPresent: true,
  orderCostsComplete: true,
  incomeLedgerBackfilled: false,
  usageMetricsAvailable: true,
  refundAndOutstandingExcludedFromCollected: true,
  checkedAt: '2026-09-30T00:00:00Z',
  notes: ['No payment provider is connected, so Derived entries are billed value.'],
}

const TAKINGS = {
  window: '30d',
  windowFrom: '2026-09-01T00:00:00Z',
  windowTo: '2026-09-30T00:00:00Z',
  generatedAt: '2026-09-30T00:00:00Z',
  currency: 'LKR',
  collected: 18500,
  billedUnconfirmed: 30000,
  paymentRowsPresent: true,
  ledgerBackfilled: false,
  dataQuality: QUALITY,
}

const SUMMARY = {
  window: '30d',
  windowFrom: '2026-09-01T00:00:00Z',
  windowTo: '2026-09-30T00:00:00Z',
  generatedAt: '2026-09-30T00:00:00Z',
  currency: 'LKR',
  sales: {
    grossOrderValue: 48500,
    orderCount: 3,
    averageOrderValue: 16166.67,
    discountTotal: 0,
    marginAmount: 6000,
    marginPercent: 0.4,
    marginCostsComplete: true,
  },
  cash: { collected: 18500, outstanding: 0, refunded: 0, refundCount: 0 },
  customers: {
    activeCount: 2,
    totalCount: 4,
    newInWindow: 1,
    repeatCount: 2,
    inactivityThresholdDays: 90,
  },
  catalog: { itemCount: 7, lowStockCount: 1, outOfStockCount: 0, stockValueAtCost: 14000 },
  team: { activeSeats: 2, allowedSeats: 0, pendingInvitations: 0, roleBreakdown: {} },
  usage: {
    blossomUsed: 40,
    monthlyBlossomLimit: 150,
    blossomRemaining: 110,
    percentUsed: 26.7,
  },
  operations: { pendingApprovals: 1, openConversations: 2, scheduledDeliveries: 0 },
  dataQuality: QUALITY,
}

describe('Overview', () => {
  beforeEach(() => {
    fetchTenantTakings.mockReset().mockResolvedValue(TAKINGS)
    fetchDashboardSummary.mockReset().mockResolvedValue(SUMMARY)
  })

  it('renders the reduced takings card for every role, with both figures labelled', async () => {
    render(
      <Overview
        organization={ORGANIZATION}
        usage={null}
        role="org:boutique_staff"
        window="30d"
      />,
    )

    expect(await screen.findByText('Takings')).toBeInTheDocument()
    expect(screen.getByText('Collected')).toBeInTheDocument()
    expect(screen.getByText('Billed, unconfirmed')).toBeInTheDocument()
    // A staff member's read is fetched and no strip is requested.
    expect(fetchTenantTakings).toHaveBeenCalled()
    expect(fetchDashboardSummary).not.toHaveBeenCalled()
  })

  it('renders the full strip for a role with reports:view', async () => {
    render(
      <Overview
        organization={ORGANIZATION}
        usage={null}
        role="org:boutique_manager"
        window="30d"
      />,
    )

    expect(await screen.findByText('Gross order value')).toBeInTheDocument()
    expect(fetchDashboardSummary).toHaveBeenCalled()
  })

  it('hides the strip in the owner preview without calling the API again', async () => {
    render(
      <Overview
        organization={ORGANIZATION}
        usage={null}
        role="org:boutique_owner"
        window="30d"
      />,
    )

    await screen.findByText('Gross order value')
    fetchDashboardSummary.mockClear()

    await userEvent.click(screen.getByRole('button', { name: /preview the staff view/i }))

    // The preview is client-side: it issues **no** request, because no permission changed.
    expect(fetchDashboardSummary).not.toHaveBeenCalled()
    await waitFor(() =>
      expect(screen.queryByText('Gross order value')).not.toBeInTheDocument(),
    )
    // The reduced card is still there, which is exactly what staff see.
    expect(screen.getByText('Takings')).toBeInTheDocument()
    expect(screen.getByText(/previewing the staff view/i)).toBeInTheDocument()
  })

  it('says a hidden strip is hidden rather than showing an empty region', async () => {
    render(
      <Overview
        organization={ORGANIZATION}
        usage={null}
        role="org:boutique_staff"
        window="30d"
      />,
    )

    expect(
      await screen.findByText(/detailed figures are available to an owner, manager or supervisor/i),
    ).toBeInTheDocument()
  })

  it('renders "not measured" rather than a zero when a figure is absent', async () => {
    fetchDashboardSummary.mockResolvedValue({
      ...SUMMARY,
      sales: { ...SUMMARY.sales, averageOrderValue: null, grossOrderValue: null, orderCount: 0 },
    })

    render(
      <Overview
        organization={ORGANIZATION}
        usage={null}
        role="org:boutique_manager"
        window="30d"
      />,
    )

    // The average over no orders does not exist, so it reads "not measured".
    expect(await screen.findAllByText(/not measured/i)).not.toHaveLength(0)
  })

  it('flags an incomplete margin instead of presenting it as trustworthy', async () => {
    fetchDashboardSummary.mockResolvedValue({
      ...SUMMARY,
      sales: { ...SUMMARY.sales, marginCostsComplete: false },
      dataQuality: { ...QUALITY, orderCostsComplete: false },
    })

    render(
      <Overview
        organization={ORGANIZATION}
        usage={null}
        role="org:boutique_manager"
        window="30d"
      />,
    )

    expect(await screen.findByText('cost data incomplete')).toBeInTheDocument()
  })

  it('states the balance is unavailable rather than inventing a zero', async () => {
    render(
      <Overview
        organization={ORGANIZATION}
        usage={null}
        role="org:boutique_staff"
        window="30d"
      />,
    )

    expect(await screen.findByText(/balance unavailable/i)).toBeInTheDocument()
    // The old literal is gone, and the truthfulness gate enforces that too.
    expect(screen.queryByText(/demo mode/i)).not.toBeInTheDocument()
  })

  it('keeps the reduced card when the strip call fails', async () => {
    fetchDashboardSummary.mockRejectedValueOnce(new Error('403'))

    render(
      <Overview
        organization={ORGANIZATION}
        usage={null}
        role="org:boutique_manager"
        window="30d"
      />,
    )

    expect(await screen.findByText('Takings')).toBeInTheDocument()
    expect(
      await screen.findByText(/detailed figures could not be loaded/i),
    ).toBeInTheDocument()
  })

  it('renders an error state with a retry when the reduced read fails', async () => {
    fetchTenantTakings.mockRejectedValueOnce(new Error('network'))

    render(
      <Overview
        organization={ORGANIZATION}
        usage={null}
        role="org:boutique_staff"
        window="30d"
      />,
    )

    expect(await screen.findByText(/could not load the boutique's takings/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument()
  })
})
