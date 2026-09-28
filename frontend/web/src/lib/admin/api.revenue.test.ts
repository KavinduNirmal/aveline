import { afterEach, describe, expect, it } from 'vitest'

import { apiClient } from '@/lib/api'
import {
  adjustIncome,
  fetchBlossomStatement,
  fetchIncomeAccounts,
  fetchIncomeLedger,
  fetchIncomeOverview,
  fetchRevenueBlossomSales,
  fetchRevenueCollections,
  fetchRevenueTimeseries,
  refundIncome,
  reviseReconciliation,
  verifyIncome,
} from './api'

interface CapturedRequest {
  url: string | undefined
  params: Record<string, unknown> | undefined
  body: unknown
  headers: Record<string, unknown>
}

/**
 * The revenue and statement wrappers. The mapping under test is the one the API documents: a
 * parameter the surface does not set is **omitted**, not sent as an empty string or a zero, because
 * `?from=` would bind as an unparseable date and `?pageSize=0` as an invalid page.
 */
function captureRequests(responseHeaders: Record<string, string> = {}): CapturedRequest[] {
  const captured: CapturedRequest[] = []
  apiClient.defaults.adapter = async (config) => {
    captured.push({
      url: config.url,
      params: config.params as Record<string, unknown> | undefined,
      body:
        typeof config.data === 'string' && config.data.length > 0
          ? JSON.parse(config.data)
          : config.data,
      headers: config.headers as unknown as Record<string, unknown>,
    })
    return {
      status: 201,
      statusText: 'Created',
      headers: responseHeaders,
      config,
      data: { ok: true },
    }
  }
  return captured
}

const originalAdapter = apiClient.defaults.adapter

afterEach(() => {
  apiClient.defaults.adapter = originalAdapter
})

describe('the Blossom statement wrapper', () => {
  it('sends every filter the statement now accepts', async () => {
    const captured = captureRequests()

    await fetchBlossomStatement('org-1', {
      from: '2026-09-01T00:00:00Z',
      to: '2026-10-01T00:00:00Z',
      kind: 'entitlement',
      entryType: 'TopUpGrant',
      sourceKind: 'Admin',
      query: 'goodwill',
      minAmount: 10,
      maxAmount: 500,
      page: 2,
      pageSize: 25,
    })

    expect(captured[0].url).toBe('/api/v1/admin/orgs/org-1/blossoms/statement')
    expect(captured[0].params).toEqual({
      from: '2026-09-01T00:00:00Z',
      to: '2026-10-01T00:00:00Z',
      kind: 'entitlement',
      entryType: 'TopUpGrant',
      sourceKind: 'Admin',
      q: 'goodwill',
      minAmount: 10,
      maxAmount: 500,
      page: 2,
      pageSize: 25,
    })
  })

  it('omits an unset filter rather than sending an empty value', async () => {
    const captured = captureRequests()

    await fetchBlossomStatement('org-1', { page: 1 })

    // `from=` would bind as an unparseable date server-side, and `q=` would filter on the empty
    // string rather than not filtering at all. Axios drops an `undefined` parameter from the query
    // string at serialisation time, so `undefined` — not mere absence — is the assertion.
    const params = captured[0].params ?? {}
    expect(params.q).toBeUndefined()
    expect(params.from).toBeUndefined()
    expect(params.minAmount).toBeUndefined()
    expect(params.entryType).toBeUndefined()
    expect(params.page).toBe(1)
  })
})

describe('the revenue reads', () => {
  it('maps the ledger query onto the documented parameter names', async () => {
    const captured = captureRequests()

    await fetchIncomeLedger({
      from: '2026-09-01T00:00:00Z',
      to: '2026-10-01T00:00:00Z',
      page: 1,
      pageSize: 50,
    })

    expect(captured[0].url).toBe('/api/v1/admin/revenue/ledger')
    expect(captured[0].params).toEqual({
      from: '2026-09-01T00:00:00Z',
      to: '2026-10-01T00:00:00Z',
      page: 1,
      pageSize: 50,
    })
  })

  it('hits each statistics path', async () => {
    const captured = captureRequests()

    await fetchIncomeOverview({})
    await fetchIncomeAccounts({})
    await fetchRevenueTimeseries({ granularity: 'week' })
    await fetchRevenueCollections({ granularity: 'month' })
    await fetchRevenueBlossomSales({})
    await reviseReconciliation({ organizationId: 'org-1' })

    expect(captured.map((request) => request.url)).toEqual([
      '/api/v1/admin/statistics/revenue/overview',
      '/api/v1/admin/revenue/accounts',
      '/api/v1/admin/statistics/revenue/timeseries',
      '/api/v1/admin/statistics/revenue/collections',
      '/api/v1/admin/statistics/revenue/blossoms',
      '/api/v1/admin/statistics/billing/reconciliation',
    ])
    expect(captured[5].params).toEqual({ organizationId: 'org-1' })
  })
})

describe('the revenue writes', () => {
  it('sends a mandatory Idempotency-Key on every verb', async () => {
    const captured = captureRequests()

    await verifyIncome(
      {
        organizationId: 'org-1',
        sourceKind: 'SubscriptionBilling',
        sourceRef: 'period-2026-09',
        amount: 4500,
        reason: 'Payment received against the period.',
      },
      'idem-verify',
    )
    await refundIncome(
      {
        organizationId: 'org-1',
        sourceKind: 'SubscriptionBilling',
        sourceRef: 'period-2026-09',
        amount: 1000,
        reason: 'Refund after a service credit.',
      },
      'idem-refund',
    )
    await adjustIncome(
      { organizationId: 'org-1', amount: 250, reason: 'Rounding correction.' },
      'idem-adjust',
    )

    expect(captured.map((request) => request.headers['Idempotency-Key'])).toEqual([
      'idem-verify',
      'idem-refund',
      'idem-adjust',
    ])
    expect(captured.map((request) => request.url)).toEqual([
      '/api/v1/admin/revenue/ledger/verify',
      '/api/v1/admin/revenue/ledger/refund',
      '/api/v1/admin/revenue/ledger/adjust',
    ])
  })

  it('reports a replay from the Idempotency-Replayed header', async () => {
    captureRequests({ 'Idempotency-Replayed': 'true' })

    const result = await verifyIncome(
      {
        organizationId: 'org-1',
        sourceKind: 'SubscriptionBilling',
        sourceRef: 'period-2026-09',
        amount: 4500,
        reason: 'Payment received against the period.',
      },
      'idem-replay',
    )

    // A replay is a distinct outcome from a fresh application, and the console says which happened.
    expect(result.replayed).toBe(true)
  })

  it('reports a fresh application as not replayed', async () => {
    captureRequests()

    const result = await verifyIncome(
      {
        organizationId: 'org-1',
        sourceKind: 'SubscriptionBilling',
        sourceRef: 'period-2026-09',
        amount: 4500,
        reason: 'Payment received against the period.',
      },
      'idem-fresh',
    )

    expect(result.replayed).toBe(false)
  })
})
