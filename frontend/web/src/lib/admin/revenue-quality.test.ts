import { describe, expect, it } from 'vitest'

import { INCOME_QUALITY_FIELDS } from './revenue-quality'

/**
 * The fifth `dataQuality` vocabulary.
 *
 * The console already carries four — system, agent, api and business — and each one is a
 * separate interface rather than a shared one with unused fields, because a flag that does not
 * mean anything for a family is a flag a reader will misread. `admin-console.md` records the
 * rule: *"`lib/admin/data-quality.ts` implements the three vocabularies and never coerces one
 * into another."*
 *
 * Money needs its own, because none of the four existing flags is true of it: attribution and
 * backfill say nothing about whether a price was configured or whether a receipt was verified.
 * This test pins the field set so the vocabulary cannot silently grow or collapse back into
 * `BusinessDataQuality`.
 */
describe('the income data-quality vocabulary', () => {
  it('names exactly the five money-specific measures', () => {
    expect([...INCOME_QUALITY_FIELDS]).toEqual([
      'revenueProviderSettlementAvailable',
      'subscriptionPricesConfigured',
      'derivedEntriesUnverified',
      'checkedAt',
      'notes',
    ])
  })

  it('carries no field borrowed from another vocabulary', () => {
    // A shared field would be the beginning of one vocabulary replacing another.
    for (const foreign of [
      'userAttributionAvailable',
      'unresolvedAttributionCount',
      'subscriptionHistoryBackfilled',
      'lastActivityIsReconstructed',
      'agentMetricsUninstrumented',
    ]) {
      expect(INCOME_QUALITY_FIELDS, foreign).not.toContain(foreign)
    }
  })

  it('states the two facts a reader would otherwise have to assume', () => {
    // Nothing in this repository settles money, and every subscription currently has
    // `PriceLkr = 0`. Both must be reported rather than inferred.
    expect(INCOME_QUALITY_FIELDS).toContain('revenueProviderSettlementAvailable')
    expect(INCOME_QUALITY_FIELDS).toContain('subscriptionPricesConfigured')
    // The gap between derived and verified is the surface's most important number.
    expect(INCOME_QUALITY_FIELDS).toContain('derivedEntriesUnverified')
  })
})
