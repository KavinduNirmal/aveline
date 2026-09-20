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
  it('honours an explicit flag, and only "true" means on', () => {
    vi.stubEnv('VITE_GRAFANA_ENABLED', 'false')
    expect(grafanaEnabled()).toBe(false)
    vi.stubEnv('VITE_GRAFANA_ENABLED', 'yes')
    expect(grafanaEnabled()).toBe(false)
    vi.stubEnv('VITE_GRAFANA_ENABLED', 'TRUE')
    expect(grafanaEnabled()).toBe(false)
    vi.stubEnv('VITE_GRAFANA_ENABLED', 'true')
    expect(grafanaEnabled()).toBe(true)
  })

  it('is on in a development build with nothing configured, so the dev port just works', () => {
    vi.stubEnv('VITE_GRAFANA_ENABLED', '')
    vi.stubEnv('VITE_GRAFANA_BASE_URL', '')
    expect(import.meta.env.DEV).toBe(true)
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

  it('points at the compose-published dev port when nothing is configured', () => {
    vi.stubEnv('VITE_GRAFANA_ENABLED', '')
    vi.stubEnv('VITE_GRAFANA_BASE_URL', '')
    expect(grafanaLink('overview')).toEqual({
      enabled: true,
      href: 'http://localhost:3000/d/aveline-overview',
      reason: null,
    })
  })

  it('an explicit base URL always beats the dev default', () => {
    vi.stubEnv('VITE_GRAFANA_ENABLED', '')
    vi.stubEnv('VITE_GRAFANA_BASE_URL', 'https://grafana.staging.example')
    expect(grafanaLink('overview').href).toBe(
      'https://grafana.staging.example/d/aveline-overview',
    )
  })

  it('refuses a stale enabled flag with no base URL in a production build', () => {
    // A production bundle has no default host, so an enabled flag with no base URL must render
    // disabled rather than a broken link.
    vi.stubEnv('DEV', false)
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
