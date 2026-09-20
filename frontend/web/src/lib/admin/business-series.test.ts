import { describe, expect, it } from 'vitest'

import { buildBusinessAxis, formatBucket, isPartialBucket, toSeries } from './business-series'

/**
 * The pure shaping between the business-KPI wire shapes and Recharts. Its single responsibility
 * is the honesty rule the whole surface rests on: **a `null` stays a `null` (a gap), and a count
 * `0` stays `0` (a measurement)**. No React, so it is unit-testable without a DOM.
 */
describe('buildBusinessAxis', () => {
  it('produces a dense day axis covering the half-open window', () => {
    const axis = buildBusinessAxis('2026-09-13T00:00:00Z', '2026-09-20T12:00:00Z', 'day')

    expect(axis).toHaveLength(8)
    expect(axis[0]).toBe('2026-09-13T00:00:00.000Z')
    expect(axis.at(-1)).toBe('2026-09-20T00:00:00.000Z')
  })

  it('produces ISO-Monday-aligned week buckets', () => {
    const axis = buildBusinessAxis('2026-09-13T00:00:00Z', '2026-09-20T12:00:00Z', 'week')

    expect(axis).toEqual(['2026-09-07T00:00:00.000Z', '2026-09-14T00:00:00.000Z'])
  })

  it('produces calendar month buckets across a year boundary', () => {
    const axis = buildBusinessAxis('2026-11-15T00:00:00Z', '2027-02-10T00:00:00Z', 'month')

    expect(axis).toEqual([
      '2026-11-01T00:00:00.000Z',
      '2026-12-01T00:00:00.000Z',
      '2027-01-01T00:00:00.000Z',
      '2027-02-01T00:00:00.000Z',
    ])
  })

  it('returns a single bucket when the window starts and ends inside one', () => {
    const axis = buildBusinessAxis('2026-09-20T01:00:00Z', '2026-09-20T05:00:00Z', 'day')

    expect(axis).toEqual(['2026-09-20T00:00:00.000Z'])
  })

  it('still returns one bucket for a degenerate window, matching the server', () => {
    // The server's CountBuckets floors at one bucket, so a zero-length window is not an empty
    // axis; returning [] here would render an empty chart against a series the server did send.
    expect(buildBusinessAxis('2026-09-20T12:00:00Z', '2026-09-20T12:00:00Z', 'day')).toEqual([
      '2026-09-20T00:00:00.000Z',
    ])
  })
})

describe('toSeries', () => {
  const axis = buildBusinessAxis('2026-09-18T00:00:00Z', '2026-09-20T12:00:00Z', 'day')

  it('aligns points onto the dense axis, oldest first', () => {
    const series = toSeries(axis, [
      {
        bucketStart: '2026-09-19T00:00:00Z',
        isPartial: false,
        values: { newUsers: 4 },
      },
    ])

    expect(series.map((point) => point.bucket)).toEqual(axis)
    expect(series[1]).toMatchObject({ newUsers: 4, isPartial: false })
  })

  it('emits null, never zero, for an axis bucket the server did not send', () => {
    const series = toSeries(
      axis,
      [{ bucketStart: '2026-09-19T00:00:00Z', isPartial: false, values: { newUsers: 4 } }],
      'day',
      ['newUsers'],
    )

    expect(series[0].newUsers).toBeNull()
    expect(series[2].newUsers).toBeNull()
  })

  it('keeps a measured count of zero as zero, not as a gap', () => {
    const series = toSeries(
      axis,
      [{ bucketStart: '2026-09-19T00:00:00Z', isPartial: false, values: { newUsers: 0 } }],
      'day',
      ['newUsers'],
    )

    expect(series[1].newUsers).toBe(0)
    expect(series[0].newUsers).toBeNull()
  })

  it('keeps a server-sent null as null', () => {
    const series = toSeries(axis, [
      { bucketStart: '2026-09-19T00:00:00Z', isPartial: false, values: { activeUsers: null } },
    ])

    expect(series[1].activeUsers).toBeNull()
  })

  it('maps an undefined measure to null rather than dropping the key', () => {
    const series = toSeries(axis, [
      {
        bucketStart: '2026-09-18T00:00:00Z',
        isPartial: true,
        values: { activeUsers: undefined },
      },
    ])

    expect('activeUsers' in series[0]).toBe(true)
    expect(series[0].activeUsers).toBeNull()
  })

  it('propagates isPartial, and defaults it to false when the server omits it', () => {
    const series = toSeries(axis, [
      { bucketStart: '2026-09-19T00:00:00Z', isPartial: true, values: { newUsers: 1 } },
      { bucketStart: '2026-09-20T00:00:00Z', values: { newUsers: 1 } },
    ])

    expect(series[1].isPartial).toBe(true)
    expect(series[2].isPartial).toBe(false)
  })

  it('ignores a point that is not on the axis rather than inventing a bucket', () => {
    const series = toSeries(
      axis,
      [{ bucketStart: '2026-01-01T00:00:00Z', isPartial: false, values: { newUsers: 99 } }],
      'day',
      ['newUsers'],
    )

    expect(series).toHaveLength(3)
    expect(series.every((point) => point.newUsers === null)).toBe(true)
  })

  it('accepts a non-UTC offset by normalising to the bucket start', () => {
    const series = toSeries(axis, [
      { bucketStart: '2026-09-19T05:30:00+05:30', isPartial: false, values: { newUsers: 2 } },
    ])

    expect(series[1].newUsers).toBe(2)
  })

  it('returns the axis with every declared key null for an empty payload', () => {
    const series = toSeries(axis, [], 'day', ['newUsers', 'activeUsers'])

    expect(series).toHaveLength(3)
    expect(series.every((point) => point.newUsers === null && point.activeUsers === null)).toBe(true)
  })

  it('labels each bucket for the x axis', () => {
    const series = toSeries(axis, [])

    expect(series[0].label).toBe('18 Sep')
    expect(series[2].label).toBe('20 Sep')
  })

  it('labels a week bucket by its ISO week start', () => {
    const weekAxis = buildBusinessAxis('2026-09-14T00:00:00Z', '2026-09-20T00:00:00Z', 'week')
    const series = toSeries(weekAxis, [], 'week')

    expect(series[0].label).toBe('14 Sep')
  })

  it('labels a month bucket by month and year', () => {
    const monthAxis = buildBusinessAxis('2026-09-01T00:00:00Z', '2026-11-01T00:00:00Z', 'month')
    const series = toSeries(monthAxis, [], 'month')

    expect(series[0].label).toBe('Sep 2026')
  })
})

describe('formatBucket', () => {
  it('formats a day, a week start and a month distinctly', () => {
    expect(formatBucket('2026-09-20T00:00:00.000Z', 'day')).toBe('20 Sep')
    expect(formatBucket('2026-09-14T00:00:00.000Z', 'week')).toBe('14 Sep')
    expect(formatBucket('2026-09-01T00:00:00.000Z', 'month')).toBe('Sep 2026')
  })
})

describe('isPartialBucket', () => {
  const window = { from: '2026-09-18T06:00:00Z', to: '2026-09-20T12:00:00Z' }

  it('marks a leading bucket clipped by from', () => {
    const axis = buildBusinessAxis(window.from, window.to, 'day')

    expect(isPartialBucket(axis[0], window.to, 'day', window.from)).toBe(true)
  })

  it('marks the trailing bucket the window ends inside', () => {
    const axis = buildBusinessAxis(window.from, window.to, 'day')

    expect(isPartialBucket(axis.at(-1)!, window.to, 'day', window.from)).toBe(true)
  })

  it('does not mark a full bucket in the middle', () => {
    const axis = buildBusinessAxis(window.from, window.to, 'day')

    expect(isPartialBucket(axis[1], window.to, 'day', window.from)).toBe(false)
  })
})
