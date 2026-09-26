import { describe, expect, it } from 'vitest'

import type { IncomeDataQuality } from '@/types/admin'
import { describeRevenueQuality } from './revenue-quality'

function quality(overrides: Partial<IncomeDataQuality> = {}): IncomeDataQuality {
  return {
    revenueProviderSettlementAvailable: false,
    subscriptionPricesConfigured: true,
    derivedEntriesUnverified: 0,
    checkedAt: '2026-09-20T00:00:00Z',
    notes: [],
    ...overrides,
  }
}

/**
 * The fifth `dataQuality` vocabulary, rendered.
 *
 * Each false flag has to name itself, because a reader who is not told *why* a figure is absent
 * will read the absence as a zero. The two messages this module must never conflate:
 *
 * - **"no list price is configured"** — the price is missing, so MRR is unmeasurable.
 * - **"no paying organizations"** — the price exists and simply nobody is paying.
 *
 * Those are different facts about the business, and a single "MRR unavailable" would state
 * neither.
 */
describe('describeRevenueQuality', () => {
  it('names whether a provider settles money, in either state', () => {
    const noProvider = describeRevenueQuality(quality())

    // `false` is the server's answer for a `manual` deployment, and the line has to say so.
    expect(noProvider.some((line) => /no payment provider settles money/i.test(line))).toBe(true)

    // Once a provider client settles charges the same line flips rather than leaving a stale
    // "no payment provider" claim beside settled money.
    const withProvider = describeRevenueQuality(
      quality({ revenueProviderSettlementAvailable: true }),
    )
    expect(withProvider.some((line) => /a payment provider is settling money/i.test(line))).toBe(
      true,
    )
    expect(withProvider.some((line) => /no payment provider/i.test(line))).toBe(false)
  })

  it('says the price is missing when no subscription has one', () => {
    const described = describeRevenueQuality(quality({ subscriptionPricesConfigured: false }))

    expect(
      described.some((line) => /no list price is configured/i.test(line)),
    ).toBe(true)
    // And it must not blame demand for a missing price.
    expect(described.some((line) => /no paying organizations/i.test(line))).toBe(false)
  })

  it('keeps "no price configured" and "no paying organizations" as different messages', () => {
    const missingPrice = describeRevenueQuality(
      quality({ subscriptionPricesConfigured: false }),
    ).join(' ')
    const noPayers = describeRevenueQuality(quality({ subscriptionPricesConfigured: true })).join(' ')

    expect(missingPrice).not.toBe(noPayers)
    expect(missingPrice).toMatch(/no list price is configured/i)
  })

  it('names the unverified count when anything is billed but uncollected', () => {
    const described = describeRevenueQuality(quality({ derivedEntriesUnverified: 3 }))

    expect(described.some((line) => /3/.test(line) && /unverified|uncollected/i.test(line))).toBe(
      true,
    )
  })

  it('says nothing about the gap when there is none', () => {
    const described = describeRevenueQuality(quality({ derivedEntriesUnverified: 0 }))

    expect(described.some((line) => /unverified/i.test(line))).toBe(false)
  })

  it('passes the server notes through rather than dropping them', () => {
    const described = describeRevenueQuality(
      quality({ notes: ['A cache degradation note from the server.'] }),
    )

    expect(described).toContain('A cache degradation note from the server.')
  })

  it('never returns an empty list, so a surface always has something to render', () => {
    expect(describeRevenueQuality(quality()).length).toBeGreaterThan(0)
  })
})
