import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const getMock = vi.fn()

vi.mock('@/lib/api', () => ({
  apiClient: { get: (...args: unknown[]) => getMock(...args) },
}))

import {
  fetchApiKeyUsage,
  fetchApiLatency,
  fetchApiQuota,
  fetchApiRequests,
} from './statistics-api'
import * as statisticsApi from './statistics-api'

const ORG = '11111111-1111-1111-1111-111111111111'

describe('statistics-api', () => {
  beforeEach(() => getMock.mockReset().mockResolvedValue({ data: {} }))
  afterEach(() => vi.clearAllMocks())

  it('reads the API quota from its own route with no window, because a quota is a period fact', async () => {
    await fetchApiQuota(ORG)

    expect(getMock.mock.calls[0][0]).toBe(`/api/v1/orgs/${ORG}/statistics/api/quota`)
  })

  it('omits blank API-statistics filters and keeps paging defaults', async () => {
    await fetchApiRequests(ORG, {
      from: '2026-08-01T00:00:00Z',
      to: '2026-08-31T00:00:00Z',
      routeTemplate: '',
      status: '500',
      groupBy: 'day',
    })

    const [path, config] = getMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/statistics/api/requests`)
    expect(config.params.from).toBe('2026-08-01T00:00:00Z')
    expect(config.params.routeTemplate).toBeUndefined()
    expect(config.params.status).toBe('500')
    expect(config.params.groupBy).toBe('day')
    expect(config.params.page).toBe(1)
  })

  it('reads the API latency series with the requested bucket', async () => {
    await fetchApiLatency(ORG, { groupBy: 'hour' })

    const [path, config] = getMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/statistics/api/latency`)
    expect(config.params.groupBy).toBe('hour')
  })

  it('reads per-key usage from the api-keys statistics route', async () => {
    await fetchApiKeyUsage(ORG)

    expect(getMock.mock.calls[0][0]).toBe(`/api/v1/orgs/${ORG}/statistics/api-keys`)
  })
})

/**
 * The lockout, pinned mechanically. A boutique reads its usage in Blossoms; runs, tokens and
 * provider cost are agent internals whose org-scoped routes were removed. If an agent fetcher is
 * ever reintroduced to this module, this fails before a panel can call it.
 */
describe('the tenant statistics client carries no agentic surface', () => {
  it('exports no agent fetch function', () => {
    const exported = Object.keys(statisticsApi)

    expect(exported.filter((name) => name.startsWith('fetchAgent'))).toEqual([])
    expect(exported).not.toContain('canReadAgentStatistics')
    expect(exported).not.toContain('canReadApiStatistics')
  })
})
