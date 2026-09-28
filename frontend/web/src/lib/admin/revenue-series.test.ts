import { describe, expect, it } from 'vitest'

import type { RevenueTimeseriesPoint } from '@/types/admin'
import {
  buildRevenueBuckets,
  currentWindow,
  summariseRevenue,
  windowPresetDays,
} from './revenue-series'

function point(overrides: Partial<RevenueTimeseriesPoint> = {}): RevenueTimeseriesPoint {
  return {
    bucketStart: '2026-09-01T00:00:00Z',
    isPartial: false,
    derived: 100,
    verified: 80,
    refunded: 0,
    ...overrides,
  }
}

/**
 * The one place that decides how a revenue bucket is shaped, so a page cannot invent a second way.
 *
 * The rule this module exists to keep is the family's honesty contract: a measured `0` stays `0`,
 * and a measure that could not be computed stays `null`. Everything else here is bookkeeping.
 */
describe('buildRevenueBuckets', () => {
  it('keeps a measured zero as zero', () => {
    const buckets = buildRevenueBuckets([point({ derived: 0, verified: 0, refunded: 0 })])

    expect(buckets).toHaveLength(1)
    expect(buckets[0].derived).toBe(0)
    expect(buckets[0].verified).toBe(0)
    expect(buckets[0].refunded).toBe(0)
  })

  it('preserves isPartial on each bucket', () => {
    const buckets = buildRevenueBuckets([
      point({ bucketStart: '2026-09-01T00:00:00Z', isPartial: true }),
      point({ bucketStart: '2026-09-02T00:00:00Z', isPartial: false }),
      point({ bucketStart: '2026-09-03T00:00:00Z', isPartial: true }),
    ])

    expect(buckets.map((bucket) => bucket.isPartial)).toEqual([true, false, true])
  })

  it('carries the unverified gap per bucket, which is the point of the surface', () => {
    const buckets = buildRevenueBuckets([point({ derived: 100, verified: 40 })])

    expect(buckets[0].gap).toBe(60)
  })

  it('reports an over-collected bucket as a negative gap rather than clamping it', () => {
    // A receipt with no matching charge is a finding, not an error to hide.
    const buckets = buildRevenueBuckets([point({ derived: 40, verified: 100 })])

    expect(buckets[0].gap).toBe(-60)
  })

  it('returns an empty list for an empty series rather than throwing', () => {
    expect(buildRevenueBuckets([])).toEqual([])
  })

  it('labels each bucket with the instant the server sent, not a re-derived one', () => {
    const buckets = buildRevenueBuckets([point({ bucketStart: '2026-09-01T00:00:00Z' })])

    expect(buckets[0].label).toBe('2026-09-01T00:00:00Z')
  })
})

describe('summariseRevenue', () => {
  it('totals derived, verified and refunded across the series', () => {
    const summary = summariseRevenue([
      point({ derived: 100, verified: 80, refunded: 0 }),
      point({ derived: 50, verified: 50, refunded: 20 }),
    ])

    expect(summary.derived).toBe(150)
    expect(summary.verified).toBe(130)
    expect(summary.refunded).toBe(20)
    // Net verified is what actually stayed, which is not the same as what was collected.
    expect(summary.netVerified).toBe(110)
  })

  it('reports the gap as a magnitude, matching the server contract', () => {
    const summary = summariseRevenue([point({ derived: 100, verified: 40 })])

    expect(summary.unverifiedGap).toBe(60)
  })

  it('is all zeros for an empty series, and never null', () => {
    // A window with no rows is a measured zero, not an unmeasurable one: the period was observed.
    const summary = summariseRevenue([])

    expect(summary.derived).toBe(0)
    expect(summary.verified).toBe(0)
    expect(summary.netVerified).toBe(0)
    expect(summary.unverifiedGap).toBe(0)
  })
})

describe('windowPresetDays', () => {
  it('maps each preset to its day count', () => {
    expect(windowPresetDays('7d')).toBe(7)
    expect(windowPresetDays('30d')).toBe(30)
    expect(windowPresetDays('90d')).toBe(90)
  })

  it('maps a year preset to twelve months of days', () => {
    expect(windowPresetDays('12m')).toBe(365)
  })

  it('falls back to thirty days for an unknown preset rather than throwing', () => {
    // Radix emits an empty string when a toggle is deselected, so this is a real input.
    expect(windowPresetDays('')).toBe(30)
    expect(windowPresetDays('nonsense')).toBe(30)
  })
})

describe('currentWindow', () => {
  it('produces a half-open window ending at the given instant', () => {
    const now = new Date('2026-09-20T12:00:00Z')

    const window = currentWindow('30d', now)

    expect(window.to).toBe('2026-09-20T12:00:00.000Z')
    expect(window.from).toBe('2026-08-21T12:00:00.000Z')
  })

  it('never produces a window that starts after it ends', () => {
    const window = currentWindow('', new Date('2026-09-20T12:00:00Z'))

    expect(new Date(window.from).getTime()).toBeLessThan(new Date(window.to).getTime())
  })
})
