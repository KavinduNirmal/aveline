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

/**
 * The compose-published Grafana port, which `docker-compose.yml:274` marks **DEV ONLY**
 * (`"${GRAFANA_PORT:-3000}:3000"`). A development build points at it without configuration; a
 * production build has no default, because no production route exists in the repository.
 */
const DEV_GRAFANA_BASE_URL = 'http://localhost:3000'

function trimmed(value: unknown): string | null {
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : null
}

/**
 * Whether to render links at all.
 *
 * An explicit `VITE_GRAFANA_ENABLED` always wins, so a deployment can turn the links off (or on)
 * deliberately. With nothing configured, a **development** build enables them against the
 * dev-only published port, and a **production** build leaves them off — a production bundle never
 * invents a Grafana host.
 */
export function grafanaEnabled(): boolean {
  const explicit = trimmed(import.meta.env.VITE_GRAFANA_ENABLED)
  if (explicit !== null) return explicit === 'true'
  return Boolean(import.meta.env.DEV)
}

function baseUrl(): string | null {
  const explicit = trimmed(import.meta.env.VITE_GRAFANA_BASE_URL)
  if (explicit !== null) return explicit.replace(/\/+$/, '')
  return import.meta.env.DEV ? DEV_GRAFANA_BASE_URL : null
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
