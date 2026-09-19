import { render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { AdminSystemView } from './AdminSystem'

const fetchSystemOverview = vi.fn()
vi.mock('@/lib/admin/api', () => ({
  fetchSystemOverview: () => fetchSystemOverview(),
}))

describe('AdminSystemView when the overview call fails', () => {
  it('renders an error and never the word Healthy', async () => {
    fetchSystemOverview.mockReset()
    fetchSystemOverview.mockRejectedValue(new Error('503 Service Unavailable'))

    render(<AdminSystemView />)

    await waitFor(() => {
      expect(screen.getAllByText(/could not be loaded/i).length).toBeGreaterThan(0)
    })
    // The delivered fallback fabricated a whole healthy system: `readiness.status: "Healthy"`
    // with three invented probe rows and `uptimeSeconds: 84200`.
    expect(screen.queryByText(/Healthy/)).toBeNull()
    expect(screen.queryByText(/84200|23h/)).toBeNull()
  })

  it('renders "not measured" for a null error rate and never 0.00%', async () => {
    fetchSystemOverview.mockReset()
    fetchSystemOverview.mockResolvedValue({
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
        requestsPerSecond: null,
        agentRunsPerMinute: null,
        blossomsPerHour: null,
        omitted: ['blossoms_per_hour'],
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
    })

    render(<AdminSystemView />)

    await waitFor(() => {
      expect(screen.getAllByText(/not measured/i).length).toBeGreaterThan(0)
    })
    expect(screen.queryByText('0.00%')).toBeNull()
  })
})
