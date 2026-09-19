/**
 * Grafana deep links (strategy §13, Q11).
 *
 * **Grafana owns the platform's time series; the console owns decisions and actions.** Where the
 * two would show the same number over time, the console shows the number once and points here.
 *
 * The base URL is **never hard-coded**: `docker-compose.yml:272` publishes the Grafana port
 * "DEV ONLY" and no production route exists in the repository, so an unset or disabled
 * configuration renders a disabled link with a stated reason rather than a wall of 404s.
 */

/** The four provisioned dashboard UIDs, taken from each dashboard JSON's `uid`. */
export const GRAFANA_DASHBOARD_UIDS = {
  overview: 'aveline-overview',
  business: 'aveline-business',
  database: 'aveline-database',
  notifications: 'aveline-notifications',
} as const

export type GrafanaDashboard = keyof typeof GRAFANA_DASHBOARD_UIDS

export interface GrafanaLink {
  enabled: boolean
  href: string | null
  reason: string | null
}

const NOT_CONFIGURED = 'Grafana is not configured for this environment'

/** True only when `VITE_GRAFANA_ENABLED` is exactly `"true"`. */
export function grafanaEnabled(): boolean {
  return import.meta.env.VITE_GRAFANA_ENABLED === 'true'
}

function baseUrl(): string | null {
  const value = import.meta.env.VITE_GRAFANA_BASE_URL
  if (typeof value !== 'string') return null
  const trimmed = value.trim()
  return trimmed.length > 0 ? trimmed.replace(/\/+$/, '') : null
}

/** A deep link to a provisioned dashboard, or to the Grafana root when none is named. */
export function grafanaLink(dashboard: GrafanaDashboard | null): GrafanaLink {
  const base = baseUrl()
  if (!grafanaEnabled() || base === null) {
    return { enabled: false, href: null, reason: NOT_CONFIGURED }
  }
  const href = dashboard === null ? base : `${base}/d/${GRAFANA_DASHBOARD_UIDS[dashboard]}`
  return { enabled: true, href, reason: null }
}
