import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const fetchBillingPeriods = vi.fn()
const fetchBlossomStatement = vi.fn()
const fetchEntitlements = vi.fn()
const fetchSubscription = vi.fn()
const fetchTopUpPacks = vi.fn()

vi.mock('@/lib/billing-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/billing-api')>('@/lib/billing-api')
  return {
    ...actual,
    fetchBillingPeriods: (...a: unknown[]) => fetchBillingPeriods(...a),
    fetchBlossomStatement: (...a: unknown[]) => fetchBlossomStatement(...a),
    fetchEntitlements: (...a: unknown[]) => fetchEntitlements(...a),
    fetchSubscription: (...a: unknown[]) => fetchSubscription(...a),
    fetchTopUpPacks: (...a: unknown[]) => fetchTopUpPacks(...a),
  }
})

import { BillingPanel } from './BillingPanel'

const ORG = '11111111-1111-1111-1111-111111111111'

const ORGANIZATION = {
  id: ORG,
  name: 'Test Boutique',
  slug: 'test-boutique',
  planTier: 'Bloom',
} as never

const SUBSCRIPTION = {
  organizationId: ORG,
  planTier: 'Bloom',
  billingCycle: 'Monthly',
  status: 'Active',
  currentPeriodStart: '2026-09-01T00:00:00Z',
  currentPeriodEnd: '2026-10-01T00:00:00Z',
  seatsIncluded: 3,
  priceLkr: 0,
  currency: 'LKR',
  cancelAtPeriodEnd: false,
  cancelledAt: null,
  externalProvider: null,
}

const PERIOD = {
  periodStart: '2026-08-01T00:00:00Z',
  periodEnd: '2026-09-01T00:00:00Z',
  isClosed: true,
  planTier: 'Bloom',
  hasSubscriptionRow: true,
  monthlyBlossomLimit: 150,
  blossomGranted: 100,
  blossomAdjusted: -20,
  blossomUsed: 30,
  blossomRemaining: 200,
  planListPriceLkr: null,
  subscriptionPricesConfigured: false,
  topUpBlossoms: 1500,
  topUpCount: 2,
}

const STATEMENT = {
  organizationId: ORG,
  periodStart: '2026-08-01T00:00:00Z',
  periodEnd: '2026-09-01T00:00:00Z',
  openingBalance: 150,
  items: [
    {
      id: 'l1',
      occurredAt: '2026-08-03T00:00:00Z',
      kind: 'entitlement',
      entryType: 'TopUpGrant',
      blossomDelta: 500,
      balanceAfter: 650,
      reason: 'Top-up purchase pack-500.',
      sourceKind: 'PaymentProvider',
      sourceRef: 'sess_1',
      expiresAt: null,
      createdByUserId: null,
      availableToRevoke: 500,
      provider: null,
      model: null,
      normalizedUnits: null,
      actualCostUsd: null,
    },
  ],
  total: 1,
  page: 1,
  pageSize: 25,
  closingBalance: 650,
  reconciliation: {
    projectedBalance: 650,
    ledgerDerivedBalance: 650,
    drift: 0,
    isConsistent: true,
  },
  generatedAt: '2026-09-01T00:00:00Z',
  maxWindowDays: 400,
  dataQuality: {
    reconciliationChecked: false,
    openingBalanceFromProjection: true,
    windowCapped: false,
    maxWindowDays: 400,
    notes: [],
  },
}

describe('BillingPanel', () => {
  beforeEach(() => {
    fetchSubscription.mockReset().mockResolvedValue(SUBSCRIPTION)
    fetchEntitlements.mockReset().mockResolvedValue([
      {
        key: 'blossoms.monthly',
        valueType: 'number',
        value: 150,
        source: 'plan-default',
        effectiveFrom: '2026-09-01T00:00:00Z',
      },
    ])
    fetchBillingPeriods.mockReset().mockResolvedValue([PERIOD])
    fetchBlossomStatement.mockReset().mockResolvedValue(STATEMENT)
    fetchTopUpPacks.mockReset().mockResolvedValue([
      { skuCode: 'pack-500', blossomQuantity: 500, priceLkr: 2000, currency: 'LKR' },
    ])
  })

  it('renders the plan, the period history and the statement without ever claiming an invoice', async () => {
    render(<BillingPanel organization={ORGANIZATION} role="org:boutique_owner" onUpgrade={() => {}} />)

    expect(await screen.findByRole('heading', { name: 'Billing' })).toBeTruthy()
    expect(screen.getByText('Plan entitlements')).toBeTruthy()
    expect(screen.getByText('Billing periods')).toBeTruthy()
    expect(screen.getByText('Blossom statement')).toBeTruthy()
    // D8: no invoice exists anywhere in the product, so the word must not be rendered.
    expect(screen.queryByText(/invoice/i)).toBeNull()
  })

  it('shows when a grant expires and never the operator reason', async () => {
    // The statement used to carry a Reason column whose consumption text named the provider and
    // model ("AI workflow on gpt-4o"). A boutique reads Blossoms, so the column is the grant's
    // expiry instead.
    fetchBlossomStatement.mockResolvedValue({
      ...STATEMENT,
      items: [
        {
          ...STATEMENT.items[0],
          reason: 'AI workflow on gpt-4o.',
          expiresAt: '2026-09-30T00:00:00Z',
        },
      ],
    })

    render(<BillingPanel organization={ORGANIZATION} role="org:boutique_owner" onUpgrade={() => {}} />)

    expect(await screen.findByText('Blossom statement')).toBeTruthy()
    expect(screen.getByText('Expires')).toBeTruthy()
    expect(screen.queryByText('Reason')).toBeNull()
    expect(screen.queryByText(/AI workflow/i)).toBeNull()
  })

  it('explains a missing billing record instead of showing a bare "None" pill', async () => {
    // The server sends the subscription status enum verbatim; "None" is the absence of a billing
    // row, not a plan state, and a reader could not tell that from the pill alone.
    fetchSubscription.mockResolvedValue({ ...SUBSCRIPTION, status: 'None' })

    render(<BillingPanel organization={ORGANIZATION} role="org:boutique_owner" onUpgrade={() => {}} />)

    expect(await screen.findByText('No billing record')).toBeTruthy()
    expect(screen.queryByText('None')).toBeNull()
    expect(screen.getByText(/no subscription row exists/i)).toBeTruthy()
  })

  it('explains why a plan shows no list price', async () => {
    render(<BillingPanel organization={ORGANIZATION} role="org:boutique_owner" onUpgrade={() => {}} />)

    expect(await screen.findByText('no list price configured')).toBeTruthy()
    expect(screen.getByText(/no LKR list price on file/i)).toBeTruthy()
  })

  it('reports a zero plan price as not configured rather than as LKR 0', async () => {
    render(<BillingPanel organization={ORGANIZATION} role="org:boutique_owner" onUpgrade={() => {}} />)

    expect((await screen.findAllByText('not configured')).length).toBeGreaterThan(0)
    expect(screen.getByText('no list price configured')).toBeTruthy()
  })

  it('says reconciliation is unknown when the server did not check it', async () => {
    render(<BillingPanel organization={ORGANIZATION} role="org:boutique_owner" onUpgrade={() => {}} />)

    expect(await screen.findByText('reconciliation status unknown')).toBeTruthy()
    // A failed check must never be presented as consistent.
    expect(screen.queryByText('reconciled')).toBeNull()
  })

  it('offers the top-up dialog to an owner and fetches the catalogue', async () => {
    render(<BillingPanel organization={ORGANIZATION} role="org:boutique_owner" onUpgrade={() => {}} />)

    expect(await screen.findByText('Top up Blossoms')).toBeTruthy()
    expect(fetchTopUpPacks).toHaveBeenCalled()
  })

  it('does not fetch the catalogue for a manager who may not purchase', async () => {
    render(<BillingPanel organization={ORGANIZATION} role="org:boutique_manager" onUpgrade={() => {}} />)

    expect(await screen.findByText('Billing periods')).toBeTruthy()
    expect(fetchTopUpPacks).not.toHaveBeenCalled()
    expect(screen.queryByText('Top up Blossoms')).toBeNull()
  })
  it('offers the upgrade path on the plan page and reports the click to the shell', async () => {
    // The plan is this page's subject, so the route to a larger one belongs here. Routing is the
    // shell's job; the panel only reports the intent.
    const onUpgrade = vi.fn()
    render(
      <BillingPanel
        organization={ORGANIZATION}
        role="org:boutique_owner"
        onUpgrade={onUpgrade}
      />,
    )

    const cta = await screen.findByRole('button', { name: /upgrade plan/i })
    await userEvent.click(cta)

    expect(onUpgrade).toHaveBeenCalledTimes(1)
  })
})
