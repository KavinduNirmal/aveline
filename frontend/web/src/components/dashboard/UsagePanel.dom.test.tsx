import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const fetchBlossomBalance = vi.fn()
const fetchBurnRate = vi.fn()
const fetchEntitlementUsage = vi.fn()
const fetchBlossomUsage = vi.fn()
const fetchApiRequests = vi.fn()
const fetchApiLatency = vi.fn()
const fetchApiQuota = vi.fn()

vi.mock('@/lib/billing-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/billing-api')>('@/lib/billing-api')
  return {
    ...actual,
    fetchBlossomBalance: (...a: unknown[]) => fetchBlossomBalance(...a),
    fetchBurnRate: (...a: unknown[]) => fetchBurnRate(...a),
    fetchEntitlementUsage: (...a: unknown[]) => fetchEntitlementUsage(...a),
    fetchBlossomUsage: (...a: unknown[]) => fetchBlossomUsage(...a),
  }
})

vi.mock('@/lib/statistics-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/statistics-api')>('@/lib/statistics-api')
  return {
    ...actual,
    fetchApiRequests: (...a: unknown[]) => fetchApiRequests(...a),
    fetchApiLatency: (...a: unknown[]) => fetchApiLatency(...a),
    fetchApiQuota: (...a: unknown[]) => fetchApiQuota(...a),
  }
})

import { UsagePanel } from './UsagePanel'

const ORG = '11111111-1111-1111-1111-111111111111'

const ORGANIZATION = {
  id: ORG,
  name: 'Test Boutique',
  slug: 'test-boutique',
  planTier: 'Bloom',
} as never

const BALANCE = {
  organizationId: ORG,
  periodStart: '2026-09-01T00:00:00Z',
  periodEnd: '2026-10-01T00:00:00Z',
  periodIsClosed: false,
  planTier: 'Bloom',
  monthlyBlossomLimit: 150,
  blossomGranted: 0,
  blossomAdjusted: 0,
  blossomUsed: 30,
  blossomRemaining: 120,
  percentUsed: 20,
  lowBalanceThresholdPercent: 20,
  asOf: '2026-09-20T00:00:00Z',
}

const BILLING_QUALITY = {
  latencyInstrumented: false,
  nodeFailuresObserved: false,
  perStepAttribution: false,
  toolInstrumented: false,
  costInstrumented: false,
}

describe('UsagePanel', () => {
  beforeEach(() => {
    fetchBlossomBalance.mockReset().mockResolvedValue(BALANCE)
    fetchBurnRate.mockReset().mockResolvedValue({
      window: '30d',
      burnRatePerDay: 3,
      projectedExhaustionAt: null,
      currentBalance: 120,
      averageDailyUsage: 3,
      dataQuality: BILLING_QUALITY,
    })
    fetchEntitlementUsage.mockReset().mockResolvedValue({
      items: [
        {
          key: 'blossoms.monthly',
          observed: 30,
          allowed: 150,
          percentUsed: 20,
          hardLimit: true,
        },
        {
          key: 'whatsapp.monthly',
          observed: 0,
          allowed: 500,
          percentUsed: 0,
          hardLimit: false,
        },
      ],
      materialisedAt: '2026-09-20T00:00:00Z',
      dataQuality: { materialisedCounts: true },
    })
    fetchBlossomUsage.mockReset().mockResolvedValue({
      from: '2026-08-21T00:00:00Z',
      to: '2026-09-20T00:00:00Z',
      totalBlossoms: 30,
      totalNormalizedUnits: 0,
      series: [{ key: '2026-09-01', blossoms: 5, normalizedUnits: 0, workflowCount: 1 }],
      generatedAt: '2026-09-20T00:00:00Z',
    })
    fetchApiRequests.mockReset().mockResolvedValue({
      from: '2026-08-21T00:00:00Z',
      to: '2026-09-20T00:00:00Z',
      requestCount: 10,
      successCount: 9,
      errorCount: 1,
      clientErrorCount: 1,
      serverErrorCount: 0,
      throttledCount: 0,
      dataQuality: { rollupComplete: true, rawLogSampled: true, latencyBuckets: true },
    })
    fetchApiLatency.mockReset().mockResolvedValue({
      from: '2026-08-21T00:00:00Z',
      to: '2026-09-20T00:00:00Z',
      requestCount: 10,
      avgMs: 12,
      p50Ms: 10,
      p95Ms: null,
      p99Ms: null,
      maxDurationMs: 40,
      precision: 'rollup',
      reason: 'below the sample floor',
      series: [],
      dataQuality: { rollupComplete: true, rawLogSampled: true, latencyBuckets: true },
    })
    fetchApiQuota.mockReset().mockResolvedValue({
      items: [
        {
          metricKey: 'api.requests.monthly',
          apiKeyId: null,
          limit: 1000,
          used: 10,
          remaining: 990,
          percentUsed: 1,
          periodStart: '2026-09-01T00:00:00Z',
          periodEnd: '2026-10-01T00:00:00Z',
          warnedAt: null,
          exhaustedAt: null,
        },
      ],
      dataQuality: { rollupComplete: true, rawLogSampled: true, latencyBuckets: true },
    })
  })

  it('shows a staff member the balance and nothing they may not read', async () => {
    render(
      <UsagePanel
        organization={ORGANIZATION}
        role="org:boutique_staff"
        window="30d"
        onUpgrade={() => {}}
      />,
    )

    expect(await screen.findByText('Blossom balance')).toBeTruthy()
    // A panel behind `billing:view` / `stats:view` is never fetched, so it can never 403.
    expect(fetchBurnRate).not.toHaveBeenCalled()
    expect(fetchApiRequests).not.toHaveBeenCalled()
    expect(screen.queryByText('Agentic usage')).toBeNull()
    expect(screen.getByText(/available to a manager or owner/i)).toBeTruthy()
  })

  it('renders the WhatsApp limit as not measured rather than as a zero', async () => {
    render(<UsagePanel organization={ORGANIZATION} role="org:boutique_owner" window="30d" onUpgrade={() => {}} />)

    expect(await screen.findByText('Usage against plan limits')).toBeTruthy()
    expect(screen.getByText('no outbound send log exists')).toBeTruthy()
  })

  it('gives the owner no agentic, token or provider-cost view', async () => {
    // The boutique's usage unit is the Blossom. Agent runs, tokens and provider cost are agent
    // internals whose org-scoped routes were removed, so an owner must not see a panel for them.
    render(<UsagePanel organization={ORGANIZATION} role="org:boutique_owner" window="30d" onUpgrade={() => {}} />)

    expect(await screen.findByText('Blossom balance')).toBeTruthy()
    expect(screen.queryByText('Agentic usage')).toBeNull()
    expect(screen.queryByText(/total tokens/i)).toBeNull()
    expect(screen.queryByText(/provider cost/i)).toBeNull()
  })

  it('shows the API panels to a manager with stats:view and no agent panel', async () => {
    render(<UsagePanel organization={ORGANIZATION} role="org:boutique_manager" window="30d" onUpgrade={() => {}} />)

    expect(await screen.findByText('API consumption')).toBeTruthy()
    expect(screen.queryByText('Agentic usage')).toBeNull()
  })
  it('offers the upgrade path and reports the click to the shell', async () => {
    // Outgrowing the plan is why this page is open; the CTA is the way out, and routing is the
    // shell's job, so the panel only reports the intent.
    const onUpgrade = vi.fn()
    render(
      <UsagePanel
        organization={ORGANIZATION}
        role="org:boutique_owner"
        window="30d"
        onUpgrade={onUpgrade}
      />,
    )

    const cta = await screen.findByRole('button', { name: /upgrade plan/i })
    await userEvent.click(cta)

    expect(onUpgrade).toHaveBeenCalledTimes(1)
  })
})
