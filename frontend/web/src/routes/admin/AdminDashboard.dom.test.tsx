import { render, screen, waitFor } from '@testing-library/react'
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
vi.mock('@/lib/admin/api', () => ({
  fetchSystemOverview: () => fetchSystemOverview(),
  queryAuditEntries: (...args: unknown[]) => queryAuditEntries(...args),
  listAdminRequests: () => listAdminRequests(),
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
    fetchSystemOverview.mockRejectedValue(new Error('503'))
    queryAuditEntries.mockRejectedValue(new Error('503'))
    listAdminRequests.mockRejectedValue(new Error('503'))

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

    renderDashboard()

    await waitFor(() => {
      expect(screen.getByText('REAL_ACTION')).toBeInTheDocument()
    })
    expect(screen.getAllByText(/not measured/i).length).toBeGreaterThan(0)
    for (const value of FABRICATED) {
      expect(screen.queryByText(value)).toBeNull()
    }
  })
})
