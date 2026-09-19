import { afterEach, describe, expect, it, vi } from 'vitest'

import { GRAFANA_DASHBOARD_UIDS, grafanaEnabled, grafanaLink } from './grafana'

afterEach(() => {
  vi.unstubAllEnvs()
})

describe('GRAFANA_DASHBOARD_UIDS', () => {
  it('uses the four provisioned dashboard UIDs, so links survive re-provisioning', () => {
    expect(GRAFANA_DASHBOARD_UIDS).toEqual({
      overview: 'aveline-overview',
      business: 'aveline-business',
      database: 'aveline-database',
      notifications: 'aveline-notifications',
    })
  })
})

describe('grafanaEnabled', () => {
  it('is false unless VITE_GRAFANA_ENABLED is exactly "true"', () => {
    vi.stubEnv('VITE_GRAFANA_ENABLED', '')
    expect(grafanaEnabled()).toBe(false)
    vi.stubEnv('VITE_GRAFANA_ENABLED', 'false')
    expect(grafanaEnabled()).toBe(false)
    vi.stubEnv('VITE_GRAFANA_ENABLED', 'yes')
    expect(grafanaEnabled()).toBe(false)
    vi.stubEnv('VITE_GRAFANA_ENABLED', 'true')
    expect(grafanaEnabled()).toBe(true)
  })
})

describe('grafanaLink', () => {
  it('renders a disabled link with a stated reason when Grafana is not configured', () => {
    vi.stubEnv('VITE_GRAFANA_ENABLED', 'false')
    vi.stubEnv('VITE_GRAFANA_BASE_URL', '')
    const link = grafanaLink('overview')
    expect(link.enabled).toBe(false)
    expect(link.href).toBeNull()
    expect(link.reason).toMatch(/not configured for this environment/i)
  })

  it('refuses a stale enabled flag with no base URL rather than emitting a broken link', () => {
    vi.stubEnv('VITE_GRAFANA_ENABLED', 'true')
    vi.stubEnv('VITE_GRAFANA_BASE_URL', '')
    const link = grafanaLink('overview')
    expect(link.enabled).toBe(false)
    expect(link.href).toBeNull()
    expect(link.reason).toMatch(/not configured/i)
  })

  it('builds the dashboard URL from the configured base and the provisioned UID', () => {
    vi.stubEnv('VITE_GRAFANA_ENABLED', 'true')
    vi.stubEnv('VITE_GRAFANA_BASE_URL', 'https://grafana.example/')
    expect(grafanaLink('overview')).toEqual({
      enabled: true,
      href: 'https://grafana.example/d/aveline-overview',
      reason: null,
    })
    expect(grafanaLink('business').href).toBe('https://grafana.example/d/aveline-business')
  })

  it('never hard-codes a Grafana host', () => {
    vi.stubEnv('VITE_GRAFANA_ENABLED', 'true')
    vi.stubEnv('VITE_GRAFANA_BASE_URL', 'https://ops.internal.example')
    expect(grafanaLink('database').href).toBe(
      'https://ops.internal.example/d/aveline-database',
    )
  })

  it('links to the Grafana root when no dashboard is named', () => {
    vi.stubEnv('VITE_GRAFANA_ENABLED', 'true')
    vi.stubEnv('VITE_GRAFANA_BASE_URL', 'https://grafana.example')
    expect(grafanaLink(null).href).toBe('https://grafana.example')
  })
})
