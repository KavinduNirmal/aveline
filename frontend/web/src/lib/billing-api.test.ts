import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const getMock = vi.fn()
const postMock = vi.fn()

vi.mock('@/lib/api', () => ({
  apiClient: {
    get: (...args: unknown[]) => getMock(...args),
    post: (...args: unknown[]) => postMock(...args),
  },
}))

import {
  fetchBillingPeriods,
  fetchBlossomBalance,
  fetchBlossomStatement,
  fetchBlossomUsage,
  fetchBurnRate,
  fetchEntitlements,
  fetchEntitlementUsage,
  fetchSubscription,
  fetchTopUpPacks,
  planListPrice,
  purchaseTopUp,
  type BillingPeriod,
} from './billing-api'

const ORG = '11111111-1111-1111-1111-111111111111'

describe('billing-api', () => {
  beforeEach(() => {
    getMock.mockReset().mockResolvedValue({ data: {} })
    postMock.mockReset().mockResolvedValue({ data: {} })
  })

  afterEach(() => vi.clearAllMocks())

  it('reads the billing-period history from the org path with the take limit', async () => {
    await fetchBillingPeriods(ORG, 6)

    const [path, config] = getMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/billing/periods`)
    expect(config.params).toEqual({ take: 6 })
  })

  it('reads the top-up catalogue from the blossoms path, which is the purchase\'s own permission', async () => {
    await fetchTopUpPacks(ORG)

    expect(getMock.mock.calls[0][0]).toBe(`/api/v1/orgs/${ORG}/blossoms/top-up-packs`)
  })

  it('omits blank statement filters rather than sending empty strings', async () => {
    await fetchBlossomStatement(ORG, { q: '', entryType: undefined, page: 2, pageSize: 25 })

    const [path, config] = getMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/blossoms/statement`)
    expect(config.params.q).toBeUndefined()
    expect(config.params.entryType).toBeUndefined()
    expect(config.params.page).toBe(2)
    expect(config.params.pageSize).toBe(25)
  })

  it('requests the Blossom consumption series with the window it was given', async () => {
    await fetchBlossomUsage(ORG, { from: '2026-08-01T00:00:00Z', to: '2026-08-31T00:00:00Z' })

    const [path, config] = getMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/blossoms/usage`)
    expect(config.params).toEqual({
      from: '2026-08-01T00:00:00Z',
      to: '2026-08-31T00:00:00Z',
      groupBy: 'day',
    })
  })

  it('reads the balance, subscription, entitlements and burn rate from their own routes', async () => {
    await fetchBlossomBalance(ORG)
    await fetchSubscription(ORG)
    await fetchEntitlements(ORG)
    await fetchEntitlementUsage(ORG)
    await fetchBurnRate(ORG)

    const paths = getMock.mock.calls.map((call) => call[0])
    expect(paths).toEqual([
      `/api/v1/orgs/${ORG}/blossoms/balance`,
      `/api/v1/orgs/${ORG}/subscription`,
      `/api/v1/orgs/${ORG}/entitlements`,
      `/api/v1/orgs/${ORG}/entitlements/usage`,
      `/api/v1/orgs/${ORG}/billing/burn-rate`,
    ])
  })

  it('sends the Idempotency-Key header on a top-up, because a retried purchase must not double-grant', async () => {
    await purchaseTopUp(ORG, 'pack-500', 'key-123')

    const [path, body, config] = postMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/blossoms/top-ups`)
    expect(body).toEqual({ skuCode: 'pack-500' })
    expect(config.headers['Idempotency-Key']).toBe('key-123')
  })

  it('does not require a provider reference when none is given, but forwards one when it is', async () => {
    await purchaseTopUp(ORG, 'pack-500', 'key-1')
    await purchaseTopUp(ORG, 'pack-500', 'key-2', 'provider-session-9')

    expect(postMock.mock.calls[0][1]).toEqual({ skuCode: 'pack-500' })
    expect(postMock.mock.calls[1][1]).toEqual({
      skuCode: 'pack-500',
      paymentReference: 'provider-session-9',
    })
  })
})

/**
 * C-4. `OrganizationSubscription.PriceLkr` is never assigned, so it is permanently `0`. A panel
 * that rendered the column directly would print `LKR 0` as a plan price for every subscription.
 * The helper is the one place the "is it actually configured?" question is answered.
 */
describe('planListPrice', () => {
  const base: BillingPeriod = {
    periodStart: '2026-08-01T00:00:00Z',
    periodEnd: '2026-09-01T00:00:00Z',
    isClosed: true,
    planTier: 'Bloom',
    hasSubscriptionRow: true,
    monthlyBlossomLimit: 150,
    blossomGranted: 0,
    blossomAdjusted: 0,
    blossomUsed: 0,
    blossomRemaining: 150,
    planListPriceLkr: null,
    subscriptionPricesConfigured: false,
    topUpBlossoms: 0,
    topUpCount: 0,
  }

  it('is null when the server did not configure a price', () => {
    expect(planListPrice(base)).toBeNull()
  })

  it('is null even if a zero somehow reaches the client, because a zero price is never a plan price', () => {
    expect(
      planListPrice({ ...base, planListPriceLkr: 0, subscriptionPricesConfigured: false }),
    ).toBeNull()
  })

  it('reports a real configured price', () => {
    expect(
      planListPrice({ ...base, planListPriceLkr: 9000, subscriptionPricesConfigured: true }),
    ).toBe(9000)
  })

  it('reports no price for a period with no subscription row', () => {
    expect(planListPrice({ ...base, hasSubscriptionRow: false, planTier: null })).toBeNull()
  })
})
