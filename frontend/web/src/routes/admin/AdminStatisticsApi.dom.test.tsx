import { render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { AdminStatisticsApiView } from './AdminStatisticsApi'

const fetchSystemOverview = vi.fn()
const fetchSystemAlerts = vi.fn()
vi.mock('@/lib/admin/api', () => ({
  fetchSystemOverview: () => fetchSystemOverview(),
  fetchSystemAlerts: (...args: unknown[]) => fetchSystemAlerts(...args),
}))

function overview(overrides: Record<string, unknown> = {}) {
  return {
    version: {
      gitSha: 'abc1234',
      buildTime: '2026-01-01T00:00:00Z',
      assemblyVersion: '1.0.0.0',
      environment: 'Production',
    },
    readiness: { status: 'Healthy', checks: [] },
    uptimeSeconds: 3600,
    alerts: { critical: 0, warning: 0, top: [] },
    throughput: {
      requestsPerSecond: 12.5,
      agentRunsPerMinute: null,
      blossomsPerHour: null,
      omitted: ['agent_runs_per_minute'],
    },
    errors: {
      errorRate: null,
      requestCount: 1000,
      errorCount: 12,
      windowSize: 'hour',
      unhandledExceptionsMeasured: true,
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
    omitted: ['error_rate'],
    generatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

describe('AdminStatisticsApiView', () => {
  it('passes status=Firing explicitly so Resolved rows are never counted as active', async () => {
    fetchSystemOverview.mockReset()
    fetchSystemAlerts.mockReset()
    fetchSystemOverview.mockResolvedValue(overview())
    fetchSystemAlerts.mockResolvedValue({ items: [], page: 1, pageSize: 50, total: 0 })

    render(<AdminStatisticsApiView />)

    await waitFor(() => {
      expect(fetchSystemAlerts).toHaveBeenCalled()
    })

    const params = fetchSystemAlerts.mock.calls[0]?.[0] as { status?: string }
    expect(params.status).toBe('Firing')
  })

  it('renders a null error rate as "not measured", never 0.00%', async () => {
    fetchSystemOverview.mockReset()
    fetchSystemAlerts.mockReset()
    fetchSystemOverview.mockResolvedValue(overview())
    fetchSystemAlerts.mockResolvedValue({ items: [], page: 1, pageSize: 50, total: 0 })

    render(<AdminStatisticsApiView />)

    await waitFor(() => {
      expect(screen.getAllByText('not measured').length).toBeGreaterThan(0)
    })
    expect(screen.queryByText('0.00%')).toBeNull()
  })

  it('renders the API family omissions it can read from the system payload', async () => {
    fetchSystemOverview.mockReset()
    fetchSystemAlerts.mockReset()
    fetchSystemOverview.mockResolvedValue(overview())
    fetchSystemAlerts.mockResolvedValue({ items: [], page: 1, pageSize: 50, total: 0 })

    render(<AdminStatisticsApiView />)

    await waitFor(() => {
      expect(screen.getByText(/error_rate/)).toBeInTheDocument()
    })
  })

  it('renders an error rather than a fabricated healthy API when the overview fails', async () => {
    fetchSystemOverview.mockReset()
    fetchSystemAlerts.mockReset()
    fetchSystemOverview.mockRejectedValue(new Error('503 Service Unavailable'))
    fetchSystemAlerts.mockResolvedValue({ items: [], page: 1, pageSize: 50, total: 0 })

    render(<AdminStatisticsApiView />)

    await waitFor(() => {
      expect(screen.getByText(/could not be loaded/i)).toBeInTheDocument()
    })
    expect(screen.queryByText(/Healthy/)).toBeNull()
  })
})
