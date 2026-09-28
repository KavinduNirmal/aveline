import type { AuditLogEntry } from "@/types/admin"

/**
 * Buckets business actions for the dashboard's volume chart.
 *
 * The source is `GET /admin/audit` — Postgres, and **not** something Grafana renders. That is what
 * keeps the chart inside the Q11 boundary: the console charts what it owns (business actions), and
 * links out for the platform's time series.
 *
 * Pure, so the bucketing is unit-testable without a chart library or a DOM.
 */

export type BucketUnit = "day" | "month"

export interface ActivityPoint {
  key: string
  label: string
  value: number
}

const MONTHS = [
  "Jan",
  "Feb",
  "Mar",
  "Apr",
  "May",
  "Jun",
  "Jul",
  "Aug",
  "Sep",
  "Oct",
  "Nov",
  "Dec",
]

function dayKey(date: Date): string {
  return date.toISOString().slice(0, 10)
}

function monthKey(date: Date): string {
  return date.toISOString().slice(0, 7)
}

function shift(date: Date, unit: BucketUnit, amount: number): Date {
  const next = new Date(date.getTime())
  if (unit === "day") next.setUTCDate(next.getUTCDate() + amount)
  else next.setUTCMonth(next.getUTCMonth() + amount)
  return next
}

/**
 * Returns `buckets` slots ending at `now`, oldest first.
 *
 * An entry outside the window is **ignored**, never clamped into the first bucket: clamping would
 * invent activity on a day it did not happen.
 */
export function bucketActivity(
  entries: readonly AuditLogEntry[],
  options: { unit: BucketUnit; buckets: number; now?: Date },
): ActivityPoint[] {
  const now = options.now ?? new Date()
  const unit = options.unit
  const count = Math.max(0, options.buckets)

  const slots: ActivityPoint[] = []
  const index = new Map<string, number>()

  for (let offset = count - 1; offset >= 0; offset -= 1) {
    const date = shift(now, unit, -offset)
    const key = unit === "day" ? dayKey(date) : monthKey(date)
    const label =
      unit === "day"
        ? `${date.getUTCDate()} ${MONTHS[date.getUTCMonth()]}`
        : `${MONTHS[date.getUTCMonth()]} ${String(date.getUTCFullYear()).slice(2)}`
    index.set(key, slots.length)
    slots.push({ key, label, value: 0 })
  }

  for (const entry of entries) {
    const occurred = new Date(entry.occurredAt)
    if (Number.isNaN(occurred.getTime())) continue
    const key = unit === "day" ? dayKey(occurred) : monthKey(occurred)
    const slot = index.get(key)
    if (slot === undefined) continue
    slots[slot].value += 1
  }

  return slots
}

/** The index of the tallest bucket, or `-1` when nothing happened in the window. */
export function peakIndex(series: readonly ActivityPoint[]): number {
  let best = -1
  let bestValue = 0
  series.forEach((point, i) => {
    if (point.value > bestValue) {
      bestValue = point.value
      best = i
    }
  })
  return best
}

/**
 * The last bucket against the one before it, as a whole percentage.
 *
 * `null` when there is no previous bucket, or when the previous bucket was zero — "up from nothing"
 * is not a percentage, and rendering it as one would be a fabricated figure.
 */
export function deltaPct(series: readonly ActivityPoint[]): number | null {
  if (series.length < 2) return null
  const previous = series[series.length - 2].value
  const latest = series[series.length - 1].value
  if (previous === 0) return null
  return Math.round(((latest - previous) / previous) * 1000) / 10
}
