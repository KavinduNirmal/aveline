import { describe, expect, it } from 'vitest'

import type { AuditLogEntry } from '@/types/admin'
import { bucketActivity, deltaPct, peakIndex } from './activity-series'

function entry(occurredAt: string, action = 'Order Approved'): AuditLogEntry {
  return { id: occurredAt, occurredAt, action } as AuditLogEntry
}

const NOW = new Date('2026-06-15T12:00:00Z')

describe('bucketActivity', () => {
  it('returns one bucket per slot, oldest first, even when empty', () => {
    const series = bucketActivity([], { unit: 'day', buckets: 7, now: NOW })
    expect(series).toHaveLength(7)
    expect(series.every((point) => point.value === 0)).toBe(true)
  })

  it('counts entries into the day they fall on', () => {
    const series = bucketActivity(
      [
        entry('2026-06-15T09:00:00Z'),
        entry('2026-06-15T10:00:00Z'),
        entry('2026-06-14T10:00:00Z'),
      ],
      { unit: 'day', buckets: 3, now: NOW },
    )
    expect(series.map((point) => point.value)).toEqual([0, 1, 2])
    // The last bucket is today.
    expect(series[2].key).toBe('2026-06-15')
  })

  it('ignores entries outside the window rather than clamping them into the first bucket', () => {
    const series = bucketActivity(
      [entry('2026-01-01T00:00:00Z'), entry('2026-06-15T09:00:00Z')],
      { unit: 'day', buckets: 3, now: NOW },
    )
    expect(series.map((point) => point.value)).toEqual([0, 0, 1])
  })

  it('buckets by month when asked', () => {
    const series = bucketActivity(
      [
        entry('2026-06-01T00:00:00Z'),
        entry('2026-05-20T00:00:00Z'),
        entry('2026-05-02T00:00:00Z'),
      ],
      { unit: 'month', buckets: 3, now: NOW },
    )
    expect(series.map((point) => point.key)).toEqual(['2026-04', '2026-05', '2026-06'])
    expect(series.map((point) => point.value)).toEqual([0, 2, 1])
  })

  it('labels buckets for an axis', () => {
    const days = bucketActivity([], { unit: 'day', buckets: 2, now: NOW })
    expect(days[1].label).toMatch(/15/)
    const months = bucketActivity([], { unit: 'month', buckets: 1, now: NOW })
    expect(months[0].label).toMatch(/Jun/i)
  })
})

const points = (...values: number[]) =>
  values.map((value, i) => ({ key: String(i), label: String(i), value }))

describe('peakIndex', () => {
  it('finds the tallest bucket', () => {
    expect(peakIndex(points(1, 5, 3))).toBe(1)
  })

  it('is -1 for an all-zero series, so nothing is marked as a peak when nothing happened', () => {
    expect(peakIndex(points(0, 0, 0))).toBe(-1)
  })
})

describe('deltaPct', () => {
  it('compares the last bucket with the one before it', () => {
    expect(deltaPct(points(10, 8, 10))).toBeCloseTo(25)
  })

  it('is null when there is no previous figure to compare with', () => {
    expect(deltaPct(points(4))).toBeNull()
    expect(deltaPct([])).toBeNull()
  })

  it('is null rather than Infinity when the previous bucket was zero', () => {
    expect(deltaPct(points(0, 5))).toBeNull()
  })
})
