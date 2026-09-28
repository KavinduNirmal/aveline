import { useCallback, useState } from 'react'

import type { DashboardWindow } from '@/lib/dashboard-api'

/**
 * The windows the shell offers, in the order they are presented.
 *
 * The labels are the owner's words rather than the wire tokens: "This month" reads better than
 * "mtd", and the server contract is unaffected.
 */
export const DASHBOARD_WINDOWS: ReadonlyArray<{ value: DashboardWindow; label: string }> = [
  { value: '7d', label: 'Last 7 days' },
  { value: '30d', label: 'Last 30 days' },
  { value: '90d', label: 'Last 90 days' },
  { value: 'mtd', label: 'This month' },
  { value: 'ytd', label: 'This year' },
]

export interface DashboardWindowState {
  window: DashboardWindow
  setWindow: (next: DashboardWindow) => void
  /**
   * The ISO range the window resolves to **on the client**, for panels that take an explicit range
   * (`revenue-series`) rather than a preset token.
   *
   * It is computed from the same preset boundaries the server uses, so a series requested with this
   * range covers the window the tiles describe. It is deliberately recomputed when the window
   * changes and not on every render: a range that shifted mid-session would make two panels disagree
   * about the same period.
   */
  range: { from: string; to: string }
}

/** Resolves a preset to an explicit range, matching the server's own boundaries. */
export function resolveWindowRange(
  window: DashboardWindow,
  now: Date = new Date(),
): { from: string; to: string } {
  const to = now
  let from: Date

  switch (window) {
    case '7d':
      from = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000)
      break
    case '90d':
      from = new Date(now.getTime() - 90 * 24 * 60 * 60 * 1000)
      break
    case 'mtd':
      from = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1))
      break
    case 'ytd':
      from = new Date(Date.UTC(now.getUTCFullYear(), 0, 1))
      break
    default:
      from = new Date(now.getTime() - 30 * 24 * 60 * 60 * 1000)
      break
  }

  return { from: from.toISOString(), to: to.toISOString() }
}

/**
 * The shell's **one** window state.
 *
 * Every KPI panel reads this value, so two panels on the same screen cannot describe different
 * periods — which is the failure a per-panel window produces, and the reason this is lifted rather
 * than held locally.
 */
export function useDashboardWindow(initial: DashboardWindow = '30d'): DashboardWindowState {
  const [window, setWindowState] = useState<DashboardWindow>(initial)
  const [range, setRange] = useState(() => resolveWindowRange(initial))

  const setWindow = useCallback((next: DashboardWindow) => {
    setWindowState(next)
    setRange(resolveWindowRange(next))
  }, [])

  return { window, setWindow, range }
}
