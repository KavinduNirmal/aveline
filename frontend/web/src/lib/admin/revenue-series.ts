import type { RevenueTimeseriesPoint } from '@/types/admin'

/**
 * The one place that shapes a revenue bucket, so no page invents a second way.
 *
 * Modelled on `business-series.ts`, and it keeps the same honesty contract: a **measured `0` stays
 * `0`**, and a measure that could not be computed stays `null`. The difference here is the gap,
 * which the revenue family carries per bucket because the distance between what a list price says
 * should be billed and what was actually collected *is* the surface's subject.
 */
export interface RevenueBucket {
  /** The instant the server sent. Not re-derived: the axis is the server's, not the client's. */
  bucketStart: string
  label: string
  isPartial: boolean
  derived: number
  verified: number
  refunded: number
  /** `derived − verified`, **signed**: negative means more came in than was billed. */
  gap: number
}

export function buildRevenueBuckets(series: RevenueTimeseriesPoint[]): RevenueBucket[] {
  return series.map((point) => ({
    bucketStart: point.bucketStart,
    label: point.bucketStart,
    isPartial: point.isPartial,
    derived: point.derived,
    verified: point.verified,
    refunded: point.refunded,
    gap: point.derived - point.verified,
  }))
}

export interface RevenueSummary {
  derived: number
  verified: number
  refunded: number
  netVerified: number
  /** Magnitude, matching the server's `unverifiedGap`. */
  unverifiedGap: number
}

/**
 * The window totals.
 *
 * An empty series is **all zeros and never `null`**: the period was observed and genuinely had no
 * activity, which is a measurement. `null` is reserved for a measure that could not be computed at
 * all, and that distinction is the whole reason the server returns nullable fields.
 */
export function summariseRevenue(series: RevenueTimeseriesPoint[]): RevenueSummary {
  let derived = 0
  let verified = 0
  let refunded = 0

  for (const point of series) {
    derived += point.derived
    verified += point.verified
    refunded += point.refunded
  }

  return {
    derived,
    verified,
    refunded,
    netVerified: verified - refunded,
    unverifiedGap: Math.abs(derived - verified),
  }
}

/** The window presets the revenue surface offers, in days. */
const PRESET_DAYS: Record<string, number> = {
  '7d': 7,
  '30d': 30,
  '90d': 90,
  '12m': 365,
}

export const REVENUE_WINDOW_PRESETS = ['7d', '30d', '90d', '12m'] as const
export type RevenueWindowPreset = (typeof REVENUE_WINDOW_PRESETS)[number]

/**
 * Resolves a preset to a day count.
 *
 * An unknown preset falls back to thirty days rather than throwing, because Radix emits an empty
 * string when a toggle is deselected — so `""` is a real input, and `RangePresets` already guards
 * against it by refusing to clear. This is the second line of defence.
 */
export function windowPresetDays(preset: string): number {
  return PRESET_DAYS[preset] ?? 30
}

/**
 * A half-open UTC window ending at `now`.
 *
 * `now` is a parameter rather than an internal `new Date()`, which is not a style preference: a
 * window computed inside a render is a **new value on every render**, and a query key holding it is
 * therefore a new key on every render. The first version of this page did that and issued 167
 * requests before the test gave up. Callers memoize on `[preset, nowKey]`.
 */
export function currentWindow(
  preset: string,
  now: Date = new Date(),
): { from: string; to: string } {
  const days = windowPresetDays(preset)
  const from = new Date(now.getTime() - days * 24 * 60 * 60 * 1000)
  return { from: from.toISOString(), to: now.toISOString() }
}
