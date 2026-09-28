import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const getMock = vi.fn()

vi.mock('@/lib/api', () => ({
  apiClient: { get: (...args: unknown[]) => getMock(...args) },
}))

import {
  canReadDashboardStrip,
  fetchDashboardSummary,
  fetchRevenueSeries,
  fetchTenantTakings,
  fetchTopItems,
} from './dashboard-api'

const ORG = '11111111-1111-1111-1111-111111111111'

/**
 * The tenant dashboard's KPI client (T4).
 *
 * This module was at 9 % lines until T7 raised the coverage ratchet: the Overview DOM test mocks
 * `dashboard-api`, so the request shapes it builds — the part a server contract can drift away
 * from — had no test of their own. These assertions are deliberately about the **wire**, not about
 * rendering: the path, the query parameters and the defaults.
 */
describe('dashboard-api', () => {
  beforeEach(() => getMock.mockReset().mockResolvedValue({ data: {} }))
  afterEach(() => vi.clearAllMocks())

  it('builds the summary path from the organization id and sends the window', async () => {
    await fetchDashboardSummary(ORG, '7d')

    const [path, config] = getMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/dashboard/summary`)
    expect(config.params).toEqual({ window: '7d' })
  })

  it('builds the reduced takings path, which every role may call', async () => {
    await fetchTenantTakings(ORG, 'mtd')

    const [path, config] = getMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/dashboard/takings`)
    expect(config.params).toEqual({ window: 'mtd' })
  })

  it('forwards an abort signal so a superseded window request is cancelled', async () => {
    const controller = new AbortController()
    await fetchDashboardSummary(ORG, '30d', controller.signal)

    const [, config] = getMock.mock.calls[0]
    expect(config.signal).toBe(controller.signal)
  })

  it('defaults the revenue series to a day bucket and omits empty bounds', async () => {
    await fetchRevenueSeries(ORG)

    const [path, config] = getMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/dashboard/revenue-series`)
    // An explicit `from: undefined` would serialise as the string "undefined" through some clients;
    // omitting the key is the only safe shape.
    expect(config.params.from).toBeUndefined()
    expect(config.params.to).toBeUndefined()
    expect(config.params.bucket).toBe('day')
  })

  it('sends explicit revenue-series bounds when given', async () => {
    await fetchRevenueSeries(ORG, { from: '2026-09-01', to: '2026-09-30', bucket: 'week' })

    const [, config] = getMock.mock.calls[0]
    expect(config.params).toEqual({ from: '2026-09-01', to: '2026-09-30', bucket: 'week' })
  })

  it('defaults the top-items window and limit', async () => {
    await fetchTopItems(ORG)

    const [path, config] = getMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/dashboard/top-items`)
    expect(config.params).toEqual({ window: '30d', limit: 5 })
  })

  it('sends an explicit top-items window and limit', async () => {
    await fetchTopItems(ORG, { window: 'ytd', limit: 10 })

    const [, config] = getMock.mock.calls[0]
    expect(config.params).toEqual({ window: 'ytd', limit: 10 })
  })

  it('gates the strip on the caller holding reports:view', () => {
    expect(canReadDashboardStrip(true)).toBe(true)
    expect(canReadDashboardStrip(false)).toBe(false)
  })
})
