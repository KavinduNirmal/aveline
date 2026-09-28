import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const getMock = vi.fn()

vi.mock('@/lib/api', () => ({
  apiClient: { get: (...args: unknown[]) => getMock(...args) },
}))

import {
  collectedBreakdown,
  fetchIncomeAccounts,
  fetchIncomeLedger,
  hasUnverifiedGap,
  type BoutiqueIncomeReconciliation,
} from './income-api'

const ORG = '11111111-1111-1111-1111-111111111111'

describe('income-api', () => {
  beforeEach(() => getMock.mockReset().mockResolvedValue({ data: {} }))
  afterEach(() => vi.clearAllMocks())

  it('builds the ledger path from the organization id', async () => {
    await fetchIncomeLedger(ORG, { kind: 'Sale', basis: 'Verified', q: 'saree', page: 2 })

    const [path, config] = getMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/income/ledger`)
    expect(config.params).toMatchObject({
      kind: 'Sale',
      basis: 'Verified',
      q: 'saree',
      page: 2,
      pageSize: 50,
    })
  })

  it('omits empty filters rather than sending blank values', async () => {
    await fetchIncomeLedger(ORG, { kind: undefined, q: '', from: '', to: '' })

    const [, config] = getMock.mock.calls[0]
    expect(config.params.kind).toBeUndefined()
    expect(config.params.q).toBeUndefined()
    expect(config.params.from).toBeUndefined()
    expect(config.params.to).toBeUndefined()
  })

  it('builds the accounts path', async () => {
    await fetchIncomeAccounts(ORG, { from: '2026-09-01T00:00:00Z' })

    const [path, config] = getMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/income/accounts`)
    expect(config.params.from).toBe('2026-09-01T00:00:00Z')
  })
})

describe('the two-basis helpers', () => {
  const reconciled: BoutiqueIncomeReconciliation = {
    verifiedTotal: 1000,
    derivedTotal: 0,
    refundTotal: 0,
    netVerified: 1000,
    unverifiedGap: 0,
    isReconciled: true,
  }

  it('reports a gap when billed value is uncollected', () => {
    expect(hasUnverifiedGap({ ...reconciled, derivedTotal: 5000, unverifiedGap: 5000, isReconciled: false })).toBe(true)
    expect(hasUnverifiedGap(reconciled)).toBe(false)
  })

  it('treats a missing gap as no gap rather than as a zero measurement to render', () => {
    // `null` is "not measured"; the helper must not turn it into a claim either way.
    expect(hasUnverifiedGap({ ...reconciled, unverifiedGap: null, isReconciled: false })).toBe(false)
  })

  it('never returns a single collected figure without its refunds beside it', () => {
    const parts = collectedBreakdown({
      verifiedTotal: 10000,
      derivedTotal: 0,
      refundTotal: 2500,
      netVerified: 7500,
      unverifiedGap: 0,
      isReconciled: true,
    })

    expect(parts).toEqual({ collected: 10000, refunds: 2500, net: 7500 })
    // There is no field a caller could mistake for "the income" on its own.
    expect(Object.keys(parts).sort()).toEqual(['collected', 'net', 'refunds'])
  })

  it('passes null through untouched so the formatter can say "not measured"', () => {
    const parts = collectedBreakdown({
      verifiedTotal: null,
      derivedTotal: null,
      refundTotal: null,
      netVerified: null,
      unverifiedGap: null,
      isReconciled: false,
    })

    expect(parts.collected).toBeNull()
    expect(parts.net).toBeNull()
  })
})
