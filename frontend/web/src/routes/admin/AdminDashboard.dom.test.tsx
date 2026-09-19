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
    permissions: new Set<string>(),
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
vi.mock('@/lib/admin/api', () => ({
  fetchSystemOverview: () => fetchSystemOverview(),
  queryAuditEntries: (...args: unknown[]) => queryAuditEntries(...args),
  listAdminRequests: () => listAdminRequests(),
  fetchSystemAlerts: (...args: unknown[]) => fetchSystemAlerts(...args),
  fetchAgentOverview: () => fetchAgentOverview(),
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

describe('AdminDashboardView when every call fails', () => {
  it('renders an error and none of the delivered fabricated figures', async () => {
    fetchSystemOverview.mockReset()
    queryAuditEntries.mockReset()
    listAdminRequests.mockReset()
    fetchSystemAlerts.mockReset()
    fetchAgentOverview.mockReset()
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
      expect(screen.getByText(/business action volume/i)).toBeInTheDocument()
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
      expect(screen.getByText(/business action volume/i)).toBeInTheDocument()
    })
    expect(screen.queryByText(/^[+-]\d/)).toBeNull()
  })
})
