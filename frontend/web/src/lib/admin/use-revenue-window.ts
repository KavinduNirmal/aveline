import { useMemo } from "react"

import { currentWindow } from "./revenue-series"

/**
 * The revenue surface's window, stable across renders.
 *
 * This exists because of a real bug: `currentWindow` calls the clock, so calling it inline in a
 * component produced a **new window on every render**. A query key built from that is a new key
 * every render, which React Query reads as a new query — the first version of `AdminRevenue`
 * issued 167 requests before its test gave up waiting.
 *
 * The fix is not to memoise `currentWindow(preset)` alone, because its `useMemo` dependency would
 * still be a fresh `Date`. The clock is bucketed to a coarse boundary instead, so "now" is a stable
 * string for a whole bucket and the window only moves when the bucket does.
 */
const BUCKET_MS = 60 * 60 * 1000

export function useRevenueWindow(preset: string): { from: string; to: string } {
  // Rounded down to the hour. A revenue window is measured in days, so an hour's granularity is
  // irrelevant to the figures and keeps the key stable across renders and remounts.
  const bucket = Math.floor(Date.now() / BUCKET_MS)

  return useMemo(
    () => currentWindow(preset, new Date(bucket * BUCKET_MS)),
    [preset, bucket],
  )
}
