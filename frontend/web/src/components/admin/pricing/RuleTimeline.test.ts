import { describe, expect, it } from 'vitest'

import type { PricingRule } from '@/types/admin'
import { computeTimelineLayout } from './RuleTimeline'

function rule(overrides: Partial<PricingRule>): PricingRule {
  return {
    id: 'rule-1',
    scopeKind: 'Global',
    provider: null,
    model: null,
    unitsPerBlossom: 1000,
    minimumChargeBlossoms: 1,
    roundingMode: 'Up',
    roundingDecimals: 2,
    effectiveFrom: '2026-01-01T00:00:00Z',
    effectiveTo: null,
    status: 'Active',
    version: 1,
    changeReason: 'seed',
    createdByUserId: 'u1',
    approvedByUserId: null,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

const NOW = Date.parse('2026-06-01T00:00:00Z')

describe('computeTimelineLayout', () => {
  it('returns nothing for no rules', () => {
    expect(computeTimelineLayout([], NOW)).toEqual([])
  })

  it('draws an open-ended rule to now, not to zero width', () => {
    const [bar] = computeTimelineLayout([rule({ effectiveTo: null })], NOW)
    expect(bar.endsAt).toBe('in force')
    expect(bar.widthPct).toBeGreaterThan(0)
  })

  it('places a later window to the right of an earlier one', () => {
    const bars = computeTimelineLayout(
      [
        rule({ id: 'early', effectiveFrom: '2026-01-01T00:00:00Z', effectiveTo: '2026-03-01T00:00:00Z' }),
        rule({ id: 'late', effectiveFrom: '2026-04-01T00:00:00Z', effectiveTo: '2026-06-01T00:00:00Z' }),
      ],
      NOW,
    )
    const early = bars.find((bar) => bar.id === 'early')
    const late = bars.find((bar) => bar.id === 'late')
    expect(early).toBeDefined()
    expect(late).toBeDefined()
    expect((late?.leftPct ?? 0)).toBeGreaterThan(early?.leftPct ?? 0)
  })

  it('labels a bar by model, then provider, then scope kind', () => {
    const bars = computeTimelineLayout(
      [
        rule({ id: 'a', model: 'gpt-4o' }),
        rule({ id: 'b', model: null, provider: 'openai' }),
        rule({ id: 'c', model: null, provider: null, scopeKind: 'Global' }),
      ],
      NOW,
    )
    expect(bars.map((bar) => bar.label)).toEqual(['gpt-4o', 'openai', 'Global'])
  })
})
