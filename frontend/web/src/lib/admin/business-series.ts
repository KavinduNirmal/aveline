/**
 * Pure shaping between the business-KPI wire shapes and the chart layer. **No React**, so the
 * honesty rule is unit-testable on its own.
 *
 * The rule this file enforces, once: **a `null` is a gap; a count `0` is a measurement.**
 * Recharts draws a line through `0`, and its tooltip then reports the previous value's shape
 * over a gap — the classic silent lie. Every series the business surface renders goes through
 * {@link toSeries} so that cannot happen by accident.
 *
 * Authority: `BR-7.10` — a metric whose value cannot be determined is omitted, never `0`.
 */

export type BusinessGranularity = 'day' | 'week' | 'month'

export interface SeriesDefinition {
  /** The measure's key on the wire point (e.g. `newUsers`). */
  key: string
  label: string
}

export interface BusinessSeriesInput {
  bucketStart: string
  isPartial?: boolean | undefined
  values: Record<string, number | null | undefined>
}

export interface BusinessSeriesPoint {
  bucket: string
  label: string
  isPartial: boolean
  [key: string]: string | number | boolean | null
}

const MONTHS = [
  'Jan',
  'Feb',
  'Mar',
  'Apr',
  'May',
  'Jun',
  'Jul',
  'Aug',
  'Sep',
  'Oct',
  'Nov',
  'Dec',
]

function startOfUtcDay(date: Date): Date {
  return new Date(
    Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate(), 0, 0, 0, 0),
  )
}

function startOfUtcWeek(date: Date): Date {
  // ISO weeks are Monday-anchored: Sunday (0) belongs to the week that began six days earlier.
  const day = startOfUtcDay(date)
  const offset = (day.getUTCDay() + 6) % 7
  return new Date(day.getTime() - offset * 86_400_000)
}

function startOfUtcMonth(date: Date): Date {
  return new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1, 0, 0, 0, 0))
}

function truncate(value: string | Date, granularity: BusinessGranularity): Date {
  const date = value instanceof Date ? value : new Date(value)
  switch (granularity) {
    case 'week':
      return startOfUtcWeek(date)
    case 'month':
      return startOfUtcMonth(date)
    default:
      return startOfUtcDay(date)
  }
}

function advance(bucket: Date, granularity: BusinessGranularity): Date {
  switch (granularity) {
    case 'week':
      return new Date(bucket.getTime() + 7 * 86_400_000)
    case 'month':
      return new Date(
        Date.UTC(bucket.getUTCFullYear(), bucket.getUTCMonth() + 1, 1, 0, 0, 0, 0),
      )
    default:
      return new Date(bucket.getTime() + 86_400_000)
  }
}

/** The bucket's display label, per granularity. */
export function formatBucket(bucketStart: string, granularity: BusinessGranularity = 'day'): string {
  const date = new Date(bucketStart)
  if (Number.isNaN(date.getTime())) return bucketStart
  if (granularity === 'month') {
    return `${MONTHS[date.getUTCMonth()]} ${date.getUTCFullYear()}`
  }
  return `${date.getUTCDate()} ${MONTHS[date.getUTCMonth()]}`
}

/**
 * The dense axis between `from` (inclusive) and `to` (exclusive), as ISO instants.
 *
 * Dense, so a bucket with no row is still a point on the chart — which is what lets the gap be
 * drawn as a gap rather than as a joining line. An empty window yields an empty axis rather than
 * a single invented bucket.
 */
export function buildBusinessAxis(
  from: string,
  to: string,
  granularity: BusinessGranularity = 'day',
): string[] {
  const end = new Date(to)
  if (Number.isNaN(end.getTime())) return []

  const axis: string[] = []
  let cursor = truncate(from, granularity)
  let guard = 0
  while (cursor.getTime() < end.getTime() && guard < 2000) {
    axis.push(cursor.toISOString())
    cursor = advance(cursor, granularity)
    guard += 1
  }

  // The server floors its bucket count at one, so a degenerate window still yields a bucket
  // rather than an axis that disagrees with the series the server sent.
  if (axis.length === 0 && !Number.isNaN(new Date(from).getTime())) {
    axis.push(truncate(from, granularity).toISOString())
  }
  return axis
}

/**
 * True when the bucket does not occupy its full natural interval inside the window: the leading
 * bucket clipped by `from`, or the trailing bucket the window's `to` falls inside.
 */
export function isPartialBucket(
  bucketStart: string,
  to: string,
  granularity: BusinessGranularity = 'day',
  from?: string,
): boolean {
  const bucket = new Date(bucketStart)
  if (Number.isNaN(bucket.getTime())) return false

  if (from !== undefined) {
    const windowStart = new Date(from)
    if (!Number.isNaN(windowStart.getTime()) && bucket.getTime() < windowStart.getTime()) {
      return true
    }
  }

  const windowEnd = new Date(to)
  if (Number.isNaN(windowEnd.getTime())) return false
  return advance(bucket, granularity).getTime() > windowEnd.getTime()
}

/**
 * Projects the server's sparse points onto the dense axis.
 *
 * A point the server did not send becomes `null`; a point it sent as `0` stays `0`; a point it
 * sent as `null` stays `null`; a measure that is `undefined` becomes `null` rather than the key
 * disappearing, because a missing key and an absent measurement are different things to Recharts.
 * A point off the axis is ignored, never clamped into the first bucket.
 */
export function toSeries(
  axis: readonly string[],
  points: readonly BusinessSeriesInput[],
  granularity: BusinessGranularity = 'day',
  keys: readonly string[] = [],
): BusinessSeriesPoint[] {
  const byBucket = new Map<string, BusinessSeriesInput>()
  for (const point of points) {
    const key = truncate(point.bucketStart, granularity).toISOString()
    byBucket.set(key, point)
  }

  return axis.map((bucket) => {
    const source = byBucket.get(bucket)
    const projected: BusinessSeriesPoint = {
      bucket,
      label: formatBucket(bucket, granularity),
      isPartial: source?.isPartial ?? false,
    }

    // Every declared key exists on every point. A missing key and an absent measurement are
    // different things to Recharts: the first drops the series, the second draws a gap.
    for (const key of keys) {
      const value = source?.values[key]
      projected[key] = value === undefined ? null : value
    }

    for (const [key, value] of Object.entries(source?.values ?? {})) {
      projected[key] = value === undefined ? null : value
    }

    return projected
  })
}
