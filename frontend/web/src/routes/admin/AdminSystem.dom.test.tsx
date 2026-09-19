import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { AdminSystemView } from './AdminSystem'

const fetchSystemOverview = vi.fn()
const fetchSystemAlerts = vi.fn()
const acknowledgeAlert = vi.fn()
vi.mock('@/lib/admin/api', () => ({
  fetchSystemOverview: () => fetchSystemOverview(),
  fetchSystemAlerts: (...args: unknown[]) => fetchSystemAlerts(...args),
  acknowledgeAlert: (...args: unknown[]) => acknowledgeAlert(...args),
}))

function overview(overrides: Record<string, unknown> = {}) {
  return {
    version: {
      gitSha: 'abc1234',
      buildTime: '2026-01-01T00:00:00Z',
      assemblyVersion: '1.0.0.0',
      environment: 'Production',
    },
    readiness: {
      status: 'Degraded',
      checks: [
        { name: 'postgres', status: 'Healthy', durationMs: 4, message: null },
        { name: 'redis', status: 'Unhealthy', durationMs: 3001, message: 'timeout' },
      ],
    },
    uptimeSeconds: 3600,
    alerts: { critical: 1, warning: 2, top: [] },
    throughput: {
      requestsPerSecond: 12.5,
      agentRunsPerMinute: null,
      blossomsPerHour: null,
      omitted: ['agent_runs_per_minute'],
    },
    errors: {
      errorRate: null,
      requestCount: 0,
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
      omitted: ['inbound_message_backlog'],
    },
    omitted: ['inbound_message_backlog'],
    generatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

function alertRow(overrides: Record<string, unknown> = {}) {
  return {
    id: '01900000-0000-7000-8000-0000000000aa',
    ruleId: 'rule-1',
    ruleName: 'api.error_rate',
    organizationId: null,
    metricName: 'api.error_rate',
    severity: 'Critical',
    status: 'Firing',
    title: 'Error rate high',
    detail: 'above threshold',
    observedValue: 0.12,
    threshold: 0.05,
    occurrenceCount: 3,
    firedAt: '2026-01-01T00:00:00Z',
    lastObservedAt: '2026-01-01T00:05:00Z',
    acknowledgedAt: null,
    resolvedAt: null,
    ...overrides,
  }
}

/**
 * The acknowledgement response is the **entity**, not the list row: no `ruleName`, six extra
 * fields (`SystemStatisticsEndpoints.cs:140`). The delivered client reused one type for both,
 * so a patch that read `ruleName` off the response silently lost the label.
 */
function ackResponse(overrides: Record<string, unknown> = {}) {
  return {
    id: '01900000-0000-7000-8000-0000000000aa',
    ruleId: 'rule-1',
    organizationId: null,
    metricName: 'api.error_rate',
    severity: 'Critical',
    status: 'Acknowledged',
    title: 'Error rate high',
    detail: 'above threshold',
    observedValue: 0.12,
    threshold: 0.05,
    occurrenceCount: 3,
    consecutiveOkCount: 0,
    firedAt: '2026-01-01T00:00:00Z',
    lastObservedAt: '2026-01-01T00:05:00Z',
    acknowledgedAt: '2026-01-01T00:06:00Z',
    acknowledgedByUserId: 'user-1',
    resolvedAt: null,
    resolvedByUserId: null,
    resolutionNote: null,
    notificationRecordId: null,
    ...overrides,
  }
}

describe('AdminSystemView', () => {
  function prime() {
    fetchSystemOverview.mockReset()
    fetchSystemAlerts.mockReset()
    acknowledgeAlert.mockReset()
    fetchSystemOverview.mockResolvedValue(overview())
    fetchSystemAlerts.mockResolvedValue({ items: [alertRow()], page: 1, pageSize: 50, total: 1 })
  }

  it('passes status=Firing explicitly so Resolved rows are never counted as active', async () => {
    prime()

    render(<AdminSystemView />)

    await waitFor(() => {
      expect(fetchSystemAlerts).toHaveBeenCalled()
    })

    const params = fetchSystemAlerts.mock.calls[0]?.[0] as { status?: string }
    expect(params.status).toBe('Firing')
  })

  it('renders the readiness probes with their status and latency', async () => {
    prime()

    render(<AdminSystemView />)

    await waitFor(() => {
      expect(screen.getByText('postgres')).toBeInTheDocument()
    })
    expect(screen.getByText('redis')).toBeInTheDocument()
    expect(screen.getByText('Unhealthy')).toBeInTheDocument()
  })

  it('renders each omitted metric name rather than a zero', async () => {
    prime()

    render(<AdminSystemView />)

    await waitFor(() => {
      expect(screen.getByText(/inbound_message_backlog/)).toBeInTheDocument()
    })
    expect(screen.queryByText('0.00%')).toBeNull()
  })

  it('renders an error and never a fabricated healthy system when the overview fails', async () => {
    fetchSystemOverview.mockReset()
    fetchSystemAlerts.mockReset()
    fetchSystemOverview.mockRejectedValue(new Error('503 Service Unavailable'))
    fetchSystemAlerts.mockResolvedValue({ items: [], page: 1, pageSize: 50, total: 0 })

    render(<AdminSystemView />)

    await waitFor(() => {
      expect(screen.getAllByText(/could not be loaded/i).length).toBeGreaterThan(0)
    })
    expect(screen.queryByText(/Healthy/)).toBeNull()
    expect(screen.queryByText(/84200|23h/)).toBeNull()
  })

  it('patches the acknowledged row by id, without reading ruleName off the response', async () => {
    prime()
    acknowledgeAlert.mockResolvedValue(ackResponse())

    render(<AdminSystemView />)

    await waitFor(() => {
      expect(screen.getByText('Error rate high')).toBeInTheDocument()
    })

    await userEvent.click(screen.getByRole('button', { name: /acknowledge/i }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: /^acknowledge$/i }))

    await waitFor(() => {
      expect(acknowledgeAlert).toHaveBeenCalled()
    })

    // The row is found again by `id` and patched in place; the label survives because it was
    // never read off the acknowledgement response.
    const id = '01900000-0000-7000-8000-0000000000aa'
    const row = await screen.findByTestId(`alert-row-${id}`)
    await waitFor(() => {
      expect(screen.getByTestId(`alert-status-${id}`)).toHaveTextContent('Acknowledged')
    })
    expect(within(row).getByText('Error rate high')).toBeInTheDocument()
    // The list row's own `ruleName` survives: the response has none, so reading it off the
    // response would have blanked this label.
    expect(screen.getByTestId(`alert-rule-${id}`)).toHaveTextContent('api.error_rate')
  })

  it('offers a Grafana deep link that is disabled with a stated reason when unconfigured', async () => {
    prime()

    render(<AdminSystemView />)

    const link = await screen.findByTestId('grafana-link')
    expect(within(link).getByRole('button', { name: /grafana/i })).toBeDisabled()
    expect(within(link).getByText(/not configured/i)).toBeInTheDocument()
  })
})
