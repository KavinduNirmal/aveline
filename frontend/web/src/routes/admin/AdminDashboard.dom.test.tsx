import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'

import { AdminDashboardView } from './AdminDashboard'

vi.mock('@/contexts/AdminSessionContext', () => ({
  useAdminSession: () => ({
    status: 'ready',
    email: 'owner@aveline.lk',
    userId: 'user-1',
    roles: ['owner'],
    // The business-KPI section is gated on this permission, and the gate reads the set
    // directly, so a denied caller issues no business request at all.
    permissions: new Set<string>(['analytics:business:read']),
    accountState: 'Active',
    hasCompletedOnboarding: true,
    error: null,
    can: () => true,
    refresh: async () => undefined,
  }),
}))

const fetchSystemOverview = vi.fn()
const queryAuditEntries = vi.fn()
const listAdminRequests = vi.fn()
const fetchSystemAlerts = vi.fn()
const fetchAgentOverview = vi.fn()
const fetchBusinessGrowth = vi.fn()
const fetchBusinessActiveUsers = vi.fn()
const fetchBusinessPlanMix = vi.fn()
vi.mock('@/lib/admin/api', () => ({
  fetchSystemOverview: () => fetchSystemOverview(),
  queryAuditEntries: (...args: unknown[]) => queryAuditEntries(...args),
  listAdminRequests: () => listAdminRequests(),
  fetchSystemAlerts: (...args: unknown[]) => fetchSystemAlerts(...args),
  fetchAgentOverview: () => fetchAgentOverview(),
  fetchBusinessGrowth: (...args: unknown[]) => fetchBusinessGrowth(...args),
  fetchBusinessActiveUsers: (...args: unknown[]) => fetchBusinessActiveUsers(...args),
  fetchBusinessPlanMix: () => fetchBusinessPlanMix(),
}))

const FABRICATED = [
  '128,450',
  '99.98%',
  '18.4',
  'a542a5e',
  'ORD-94021',
  'Order Approved',
  'Blossom Debit',
  'Delivery Dispatched',
]

function renderDashboard() {
  return render(
    <MemoryRouter>
      <AdminDashboardView />
    </MemoryRouter>,
  )
}


/** A well-formed business answer with nothing in it, for the tests that are not about it. */
function seedEmptyBusiness() {
  fetchBusinessGrowth.mockReset()
  fetchBusinessActiveUsers.mockReset()
  fetchBusinessPlanMix.mockReset()
  fetchBusinessGrowth.mockResolvedValue({
    window: {
      from: '2026-08-21T00:00:00Z',
      to: '2026-09-20T00:00:00Z',
      granularity: 'day',
      timeZone: 'UTC',
      bucketCount: 31,
    },
    observedFrom: null,
    series: [],
    totals: { newUsers: 0, newOrganizations: 0, newAdminRequests: 0, approvedAdminRequests: 0 },
    previousTotals: { newUsers: 0, newOrganizations: 0, newAdminRequests: 0, approvedAdminRequests: 0 },
    dataQuality: {
      userAttributionAvailable: true,
      unresolvedAttributionCount: 0,
      subscriptionHistoryBackfilled: false,
      lastActivityIsReconstructed: false,
      agentMetricsUninstrumented: false,
      notes: [],
    },
  })
  fetchBusinessActiveUsers.mockResolvedValue({
    window: {
      from: '2026-08-21T00:00:00Z',
      to: '2026-09-20T00:00:00Z',
      granularity: 'day',
      timeZone: 'UTC',
      bucketCount: 31,
    },
    series: [],
    rolling: { dau: null, wau: null, mau: null, stickiness: null },
    dataQuality: {
      userAttributionAvailable: false,
      unresolvedAttributionCount: 0,
      subscriptionHistoryBackfilled: false,
      lastActivityIsReconstructed: false,
      agentMetricsUninstrumented: false,
      notes: [],
    },
  })
  fetchBusinessPlanMix.mockResolvedValue({
    asOf: '2026-09-20T00:00:00Z',
    tiers: [],
    free: { organizationCount: 0, userCount: 0, monthlyPriceLkr: 0, shareOfOrganizations: 0 },
    premium: { organizationCount: 0, userCount: 0, monthlyPriceLkr: 0, shareOfOrganizations: 0 },
    organizationsTotal: 0,
    organizationsWithBillingRow: 0,
    totalMonthlyPriceLkr: 0,
    dataQuality: {
      userAttributionAvailable: true,
      unresolvedAttributionCount: 0,
      subscriptionHistoryBackfilled: false,
      lastActivityIsReconstructed: false,
      agentMetricsUninstrumented: false,
      notes: [],
    },
  })
}

describe('AdminDashboardView when every call fails', () => {
  it('renders an error and none of the delivered fabricated figures', async () => {
    fetchSystemOverview.mockReset()
    queryAuditEntries.mockReset()
    listAdminRequests.mockReset()
    fetchSystemAlerts.mockReset()
    fetchAgentOverview.mockReset()
    seedEmptyBusiness()
    fetchSystemOverview.mockRejectedValue(new Error('503'))
    queryAuditEntries.mockRejectedValue(new Error('503'))
    listAdminRequests.mockRejectedValue(new Error('503'))
    fetchSystemAlerts.mockRejectedValue(new Error('503'))
    fetchAgentOverview.mockRejectedValue(new Error('503'))

    const { container } = renderDashboard()

    await waitFor(() => {
      expect(container.textContent).toMatch(/could not be loaded|unavailable|failed/i)
    })

    for (const value of FABRICATED) {
      expect(container.textContent, `fabricated value ${value} leaked`).not.toContain(value)
    }
  })
})

describe('AdminDashboardView when the API answers', () => {
  it('renders only values that trace to the mocked fetch', async () => {
    fetchSystemOverview.mockReset()
    queryAuditEntries.mockReset()
    listAdminRequests.mockReset()
    fetchSystemAlerts.mockReset()
    fetchAgentOverview.mockReset()
    seedEmptyBusiness()
    fetchSystemOverview.mockResolvedValue({
      version: {
        gitSha: 'deadbee',
        buildTime: '2026-01-01T00:00:00Z',
        assemblyVersion: '1.0.0.0',
        environment: 'Production',
      },
      readiness: { status: 'Degraded', checks: [] },
      uptimeSeconds: 120,
      alerts: { critical: 1, warning: 2, top: [] },
      throughput: {
        requestsPerSecond: 3.5,
        agentRunsPerMinute: null,
        blossomsPerHour: null,
        omitted: ['blossoms_per_hour'],
      },
      errors: {
        errorRate: null,
        requestCount: 10,
        errorCount: 0,
        windowSize: 'hour',
        unhandledExceptionsMeasured: false,
        omitted: ['error_rate'],
      },
      queues: {
        telemetryChannelDepth: null,
        eventBusBacklog: null,
        notificationBacklog: null,
        inboundMessageBacklog: null,
        agentRunsRunning: null,
        omitted: [],
      },
      omitted: [],
      generatedAt: '2026-01-01T00:00:00Z',
    })
    queryAuditEntries.mockResolvedValue({
      items: [
        {
          id: 'audit-1',
          occurredAt: '2026-01-01T00:00:00Z',
          organizationId: null,
          actorKind: 'User',
          actorUserId: 'user-1',
          actorRef: 'owner@aveline.lk',
          action: 'REAL_ACTION',
          entityType: 'Order',
          entityId: 'REAL-1',
          reason: null,
          requestId: null,
          before: null,
          after: null,
        },
      ],
      page: 1,
      pageSize: 5,
      total: 1,
    })
    listAdminRequests.mockResolvedValue([])
    fetchSystemAlerts.mockResolvedValue({
      items: [
        {
          id: 'alert-1',
          ruleId: 'rule-1',
          ruleName: 'api.error_rate',
          organizationId: null,
          metricName: 'api.error_rate',
          severity: 'Warning',
          status: 'Firing',
          title: 'REAL_ALERT',
          detail: null,
          observedValue: 0.2,
          threshold: 0.05,
          occurrenceCount: 3,
          firedAt: '2026-01-01T00:00:00Z',
          lastObservedAt: '2026-01-01T00:05:00Z',
          acknowledgedAt: null,
          resolvedAt: null,
        },
      ],
      page: 1,
      pageSize: 5,
      total: 1,
    })
    // totalRuns === 0 must read "no runs recorded yet", which is a *different* message from
    // "not instrumented" — the five flags below are all false and must not produce that copy.
    fetchAgentOverview.mockResolvedValue({
      totalRuns: 0,
      running: 0,
      pausedForApproval: 0,
      succeeded: 0,
      failed: 0,
      successRate: null,
      dataQuality: {
        latencyInstrumented: false,
        nodeFailuresObserved: false,
        perStepAttribution: false,
        toolInstrumented: false,
        costInstrumented: false,
      },
    })

    renderDashboard()

    await waitFor(() => {
      expect(screen.getByText('REAL_ACTION')).toBeInTheDocument()
    })
    expect(screen.getAllByText(/not measured/i).length).toBeGreaterThan(0)
    for (const value of FABRICATED) {
      expect(screen.queryByText(value)).toBeNull()
    }

    // V2 asks the server for firing alerts explicitly, so a Resolved row is never counted.
    expect(fetchSystemAlerts).toHaveBeenCalledWith(
      expect.objectContaining({ status: 'Firing' }),
    )
    // V7 renders every omitted metric by name.
    expect(screen.getByText(/blossoms_per_hour: not measured on this host/)).toBeInTheDocument()
    // V10 distinguishes an empty history from an uninstrumented one.
    expect(screen.getByText('no runs recorded yet')).toBeInTheDocument()
    expect(screen.queryByText(/latency is not instrumented/)).toBeNull()
  })
})

/**
 * The dashboard's own chart. Its source is `GET /admin/audit` — Postgres, and not something Grafana
 * renders — which is what keeps it inside the Q11 boundary.
 */
describe('AdminDashboardView business-action chart', () => {
  function seedChartData(items: unknown[]) {
    fetchSystemOverview.mockReset()
    queryAuditEntries.mockReset()
    listAdminRequests.mockReset()
    fetchSystemAlerts.mockReset()
    fetchAgentOverview.mockReset()
    seedEmptyBusiness()

    fetchSystemOverview.mockResolvedValue({
      version: { gitSha: 'deadbee', buildTime: '', assemblyVersion: '', environment: 'Production' },
      readiness: { status: 'Healthy', checks: [] },
      uptimeSeconds: 60,
      alerts: { critical: 0, warning: 0, top: [] },
      throughput: { requestsPerSecond: 1, agentRunsPerMinute: null, blossomsPerHour: null, omitted: [] },
      errors: {
        errorRate: 0,
        requestCount: 1,
        errorCount: 0,
        windowSize: 'hour',
        unhandledExceptionsMeasured: true,
        omitted: [],
      },
      queues: {
        telemetryChannelDepth: 0,
        eventBusBacklog: 0,
        notificationBacklog: 0,
        inboundMessageBacklog: 0,
        agentRunsRunning: 0,
        omitted: [],
      },
      omitted: [],
      generatedAt: '',
    })
    queryAuditEntries.mockResolvedValue({ items, page: 1, pageSize: 200, total: items.length })
    listAdminRequests.mockResolvedValue([])
    fetchSystemAlerts.mockResolvedValue({ items: [], page: 1, pageSize: 5, total: 0 })
    fetchAgentOverview.mockResolvedValue({
      totalRuns: 0,
      running: 0,
      pausedForApproval: 0,
      succeeded: 0,
      failed: 0,
      successRate: null,
      dataQuality: {
        latencyInstrumented: false,
        nodeFailuresObserved: false,
        perStepAttribution: false,
        toolInstrumented: false,
        costInstrumented: false,
      },
    })
  }

  function auditRow(id: string, occurredAt: string, action: string) {
    return {
      id,
      occurredAt,
      organizationId: null,
      actorKind: 'User',
      actorUserId: 'u1',
      actorRef: 'owner@aveline.lk',
      action,
      entityType: 'Order',
      entityId: id,
      reason: null,
      requestId: null,
      before: null,
      after: null,
    }
  }

  it('renders the chart card and reads a window wide enough to bucket', async () => {
    const today = new Date()
    seedChartData([auditRow('a1', today.toISOString(), 'Order Approved')])

    renderDashboard()

    await waitFor(() => {
      expect(screen.getByText(/business actions logged/i)).toBeInTheDocument()
    })
    // One request feeds both the table and the chart; it must not be a paging read of 8.
    expect(queryAuditEntries).toHaveBeenCalledWith(
      expect.objectContaining({ pageSize: 200 }),
    )
  })

  it('switches the bucket unit from weekly to monthly', async () => {
    seedChartData([auditRow('a1', new Date().toISOString(), 'Order Approved')])

    renderDashboard()

    // A single-select ToggleGroup is a radio group, not a set of buttons.
    expect(await screen.findByRole('radio', { name: /weekly/i })).toHaveAttribute(
      'data-state',
      'on',
    )

    await userEvent.click(screen.getByRole('radio', { name: /monthly/i }))
    await waitFor(() => {
      expect(screen.getByRole('radio', { name: /monthly/i })).toHaveAttribute('data-state', 'on')
    })
  })

  it('shows no delta badge when there is nothing to compare against', async () => {
    // A single empty window has no previous bucket, so "up from nothing" must not be rendered.
    seedChartData([])

    renderDashboard()

    await waitFor(() => {
      expect(screen.getByText(/business actions logged/i)).toBeInTheDocument()
    })
    expect(screen.queryByText(/^[+-]\d/)).toBeNull()
  })
})


/**
 * The three business KPIs on the landing page. They are the **only** part of the dashboard that
 * reads the business endpoints, they are gated on `analytics:business:read`, and each one uses a
 * different chart type so the page does not read as one repeated chart.
 */
describe('AdminDashboardView business KPIs', () => {
  const QUALITY = {
    userAttributionAvailable: true,
    unresolvedAttributionCount: 0,
    subscriptionHistoryBackfilled: false,
    lastActivityIsReconstructed: false,
    agentMetricsUninstrumented: false,
    notes: [],
  }

  const WINDOW = {
    from: '2026-08-21T00:00:00Z',
    to: '2026-09-20T12:00:00Z',
    granularity: 'day',
    timeZone: 'UTC',
    bucketCount: 31,
  }

  function seedBusiness(options: { activeUsers?: number | null } = {}) {
    fetchSystemOverview.mockReset()
    queryAuditEntries.mockReset()
    listAdminRequests.mockReset()
    fetchSystemAlerts.mockReset()
    fetchAgentOverview.mockReset()
    fetchBusinessGrowth.mockReset()
    fetchBusinessActiveUsers.mockReset()
    fetchBusinessPlanMix.mockReset()

    fetchSystemOverview.mockResolvedValue({
      version: { gitSha: 'deadbee', buildTime: '', assemblyVersion: '', environment: 'Production' },
      readiness: { status: 'Healthy', checks: [] },
      uptimeSeconds: 60,
      alerts: { critical: 0, warning: 0, top: [] },
      throughput: { requestsPerSecond: 1, agentRunsPerMinute: null, blossomsPerHour: null, omitted: [] },
      errors: {
        errorRate: 0,
        requestCount: 1,
        errorCount: 0,
        windowSize: 'hour',
        unhandledExceptionsMeasured: true,
        omitted: [],
      },
      queues: {
        telemetryChannelDepth: 0,
        eventBusBacklog: 0,
        notificationBacklog: 0,
        inboundMessageBacklog: 0,
        agentRunsRunning: 0,
        omitted: [],
      },
      omitted: [],
      generatedAt: '',
    })
    queryAuditEntries.mockResolvedValue({ items: [], page: 1, pageSize: 200, total: 0 })
    listAdminRequests.mockResolvedValue([])
    fetchSystemAlerts.mockResolvedValue({ items: [], page: 1, pageSize: 5, total: 0 })
    fetchAgentOverview.mockResolvedValue({
      totalRuns: 0,
      running: 0,
      pausedForApproval: 0,
      succeeded: 0,
      failed: 0,
      successRate: null,
      dataQuality: {
        latencyInstrumented: false,
        nodeFailuresObserved: false,
        perStepAttribution: false,
        toolInstrumented: false,
        costInstrumented: false,
      },
    })

    fetchBusinessGrowth.mockResolvedValue({
      window: { ...WINDOW, granularity: 'week' },
      observedFrom: '2026-08-21T00:00:00Z',
      series: [
        {
          bucketStart: '2026-09-14T00:00:00Z',
          isPartial: false,
          newUsers: 6,
          newOrganizations: 2,
          newAdminRequests: 0,
          approvedAdminRequests: 0,
        },
      ],
      totals: { newUsers: 6, newOrganizations: 2, newAdminRequests: 0, approvedAdminRequests: 0 },
      previousTotals: { newUsers: 3, newOrganizations: 1, newAdminRequests: 0, approvedAdminRequests: 0 },
      dataQuality: QUALITY,
    })

    fetchBusinessActiveUsers.mockResolvedValue({
      window: WINDOW,
      series: [
        { bucketStart: '2026-09-18T00:00:00Z', isPartial: false, activeUsers: 4, activeOrganizations: 2 },
        {
          bucketStart: '2026-09-19T00:00:00Z',
          isPartial: false,
          activeUsers: options.activeUsers ?? 9,
          activeOrganizations: 3,
        },
        { bucketStart: '2026-09-20T00:00:00Z', isPartial: true, activeUsers: 5, activeOrganizations: 2 },
      ],
      rolling: { dau: 5, wau: 9, mau: 11, stickiness: 0.45 },
      dataQuality: { ...QUALITY, userAttributionAvailable: options.activeUsers !== null },
    })

    fetchBusinessPlanMix.mockResolvedValue({
      asOf: '2026-09-20T12:00:00Z',
      tiers: [
        {
          planTier: 'Seed',
          isFree: true,
          organizationCount: 8,
          activeOrganizationCount: 7,
          billedSubscriptionCount: 0,
          userCount: 8,
          monthlyPriceLkr: 0,
        },
        {
          planTier: 'Bloom',
          isFree: false,
          organizationCount: 4,
          activeOrganizationCount: 4,
          billedSubscriptionCount: 3,
          userCount: 6,
          monthlyPriceLkr: 14000,
        },
      ],
      free: { organizationCount: 8, userCount: 8, monthlyPriceLkr: 0, shareOfOrganizations: 2 / 3 },
      premium: { organizationCount: 4, userCount: 6, monthlyPriceLkr: 14000, shareOfOrganizations: 1 / 3 },
      organizationsTotal: 12,
      organizationsWithBillingRow: 3,
      totalMonthlyPriceLkr: 14000,
      dataQuality: QUALITY,
    })
  }

  it('renders the signup trend as a line and the active-user trend as an area', async () => {
    seedBusiness()

    const { container } = renderDashboard()

    await waitFor(() => expect(screen.getByText('New signups')).toBeInTheDocument())
    expect(screen.getByText('Active users')).toBeInTheDocument()
    expect(screen.getByText('Plan mix')).toBeInTheDocument()

    // Different chart types: the signup trend is a line, the active-user trend is an area with a
    // gradient fill, and the plan mix is a stacked distribution bar.
    expect(container.querySelectorAll('.recharts-line').length).toBeGreaterThan(0)
    expect(container.querySelectorAll('.recharts-area').length).toBeGreaterThan(0)
    expect(container.querySelectorAll('linearGradient').length).toBeGreaterThan(0)
    expect(container.querySelectorAll('.recharts-bar-rectangle').length).toBeGreaterThan(0)
  })

  it('states the free-versus-premium split and the billing-row gap', async () => {
    seedBusiness()

    renderDashboard()

    await waitFor(() => expect(screen.getByText('Plan mix')).toBeInTheDocument())
    // 8 of 12 free is 67%, 4 of 12 premium is 33%.
    expect(screen.getByText(/67%/)).toBeInTheDocument()
    expect(screen.getByText(/33%/)).toBeInTheDocument()
    expect(screen.getByText(/3 of 12 boutiques have a billing record/i)).toBeInTheDocument()
  })

  it('shows the DAU reading as a number alongside the active-user trend', async () => {
    seedBusiness()

    renderDashboard()

    await waitFor(() => expect(screen.getByText('Active users')).toBeInTheDocument())
    expect(screen.getByText(/daily active/i)).toBeInTheDocument()
    expect(screen.getByText('5')).toBeInTheDocument()
  })

  it('reads "not measured" for a null active-user measure rather than a zero', async () => {
    seedBusiness({ activeUsers: null })

    renderDashboard()

    await waitFor(() => expect(screen.getByText('Active users')).toBeInTheDocument())
    expect(screen.getAllByText(/not measured/i).length).toBeGreaterThan(0)
  })

  it('renders a failed business read outside the chart container, at full width', async () => {
    seedBusiness()
    fetchBusinessGrowth.mockRejectedValue(new Error('growth is down'))
    fetchBusinessActiveUsers.mockRejectedValue(new Error('active users is down'))
    fetchBusinessPlanMix.mockRejectedValue(new Error('plan mix is down'))

    renderDashboard()

    const alerts = await screen.findAllByRole('alert')
    for (const alert of alerts) {
      // The regression this pins: a message rendered *inside* `ChartContainer` lands in recharts'
      // measured `0×0` box and wraps one character per line. `ChartFrame` renders the state at the
      // card's full width instead, and the chart container is never mounted for it.
      expect(alert.closest('[data-slot="chart"]')).toBeNull()
      expect(alert.className).toContain('w-full')
      expect(alert.querySelector('[data-slot="chart"]')).toBeNull()
    }
  })

  it('does not double the full stop when the server message already ends in one', async () => {
    seedBusiness()
    // The exact shape a 404 produces through `toApiError`.
    fetchBusinessGrowth.mockRejectedValue(new Error('The requested resource was not found.'))
    fetchBusinessActiveUsers.mockRejectedValue(new Error('The requested resource was not found.'))
    fetchBusinessPlanMix.mockRejectedValue(new Error('The requested resource was not found.'))

    const { container } = renderDashboard()

    await waitFor(() =>
      expect(container.textContent).toContain('The requested resource was not found.'),
    )
    expect(container.textContent).not.toContain('not found..')
  })

  it('names each business failure instead of blanking the section', async () => {
    seedBusiness()
    fetchBusinessGrowth.mockRejectedValue(new Error('growth is down'))
    fetchBusinessActiveUsers.mockRejectedValue(new Error('active users is down'))
    fetchBusinessPlanMix.mockRejectedValue(new Error('plan mix is down'))

    renderDashboard()

    await waitFor(() => expect(screen.getAllByText(/growth is down/).length).toBeGreaterThan(0))
    expect(screen.getAllByText(/active users is down/).length).toBeGreaterThan(0)
    expect(screen.getAllByText(/plan mix is down/).length).toBeGreaterThan(0)
  })

  it('asks for a 30-day day-granularity window for the trends', async () => {
    seedBusiness()

    renderDashboard()

    await waitFor(() => expect(fetchBusinessActiveUsers).toHaveBeenCalled())
    expect(fetchBusinessActiveUsers).toHaveBeenCalledWith(
      expect.objectContaining({ granularity: 'day' }),
    )
  })

  it('links each business KPI to the Growth console', async () => {
    seedBusiness()

    renderDashboard()

    await waitFor(() => expect(screen.getByText('Active users')).toBeInTheDocument())
    expect(screen.getAllByRole('link', { name: /growth/i }).length).toBeGreaterThan(0)
  })
})

/**
 * The permission gate. A caller without `analytics:business:read` must not see the section **and
 * must not issue the request** — a 403 in the network log is a worse experience than an absent
 * section.
 */
describe('AdminDashboardView business KPIs without the permission', () => {
  it('renders no business section and issues no business request', async () => {
    // The module-level mock grants the permission, so this test re-mocks the module for one call.
    vi.resetModules()
    vi.doMock('@/contexts/AdminSessionContext', () => ({
      useAdminSession: () => ({
        status: 'ready',
        email: 'owner@aveline.lk',
        userId: 'user-1',
        roles: ['owner'],
        permissions: new Set<string>(),
        accountState: 'Active',
        hasCompletedOnboarding: true,
        error: null,
        can: () => false,
        refresh: async () => undefined,
      }),
    }))

    const { AdminDashboardView: View } = await import('./AdminDashboard')
    fetchSystemOverview.mockReset()
    fetchSystemOverview.mockResolvedValue({
      version: { gitSha: 'deadbee', buildTime: '', assemblyVersion: '', environment: 'Production' },
      readiness: { status: 'Healthy', checks: [] },
      uptimeSeconds: 60,
      alerts: { critical: 0, warning: 0, top: [] },
      throughput: { requestsPerSecond: 1, agentRunsPerMinute: null, blossomsPerHour: null, omitted: [] },
      errors: {
        errorRate: 0,
        requestCount: 1,
        errorCount: 0,
        windowSize: 'hour',
        unhandledExceptionsMeasured: true,
        omitted: [],
      },
      queues: {
        telemetryChannelDepth: 0,
        eventBusBacklog: 0,
        notificationBacklog: 0,
        inboundMessageBacklog: 0,
        agentRunsRunning: 0,
        omitted: [],
      },
      omitted: [],
      generatedAt: '',
    })
    queryAuditEntries.mockResolvedValue({ items: [], page: 1, pageSize: 200, total: 0 })
    listAdminRequests.mockResolvedValue([])
    fetchSystemAlerts.mockResolvedValue({ items: [], page: 1, pageSize: 5, total: 0 })
    fetchAgentOverview.mockResolvedValue({
      totalRuns: 0,
      running: 0,
      pausedForApproval: 0,
      succeeded: 0,
      failed: 0,
      successRate: null,
      dataQuality: {
        latencyInstrumented: false,
        nodeFailuresObserved: false,
        perStepAttribution: false,
        toolInstrumented: false,
        costInstrumented: false,
      },
    })
    fetchBusinessGrowth.mockClear()
    fetchBusinessActiveUsers.mockClear()
    fetchBusinessPlanMix.mockClear()

    render(
      <MemoryRouter>
        <View />
      </MemoryRouter>,
    )

    await waitFor(() => expect(screen.getByText(/readiness/i)).toBeInTheDocument())
    expect(screen.queryByText('New signups')).toBeNull()
    expect(screen.queryByText('Plan mix')).toBeNull()
    expect(fetchBusinessGrowth).not.toHaveBeenCalled()
    expect(fetchBusinessActiveUsers).not.toHaveBeenCalled()
    expect(fetchBusinessPlanMix).not.toHaveBeenCalled()
  })
})
