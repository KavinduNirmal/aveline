import { render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { AdminStatisticsAgentsView } from './AdminStatisticsAgents'

const fetchAgentOverview = vi.fn()
vi.mock('@/lib/admin/api', () => ({
  fetchAgentOverview: () => fetchAgentOverview(),
}))

function overview(overrides: Record<string, unknown> = {}) {
  return {
    totalRuns: 42,
    running: 2,
    pausedForApproval: 1,
    succeeded: 30,
    failed: 9,
    successRate: 30 / 39,
    dataQuality: {
      latencyInstrumented: true,
      nodeFailuresObserved: true,
      perStepAttribution: false,
      toolInstrumented: true,
      costInstrumented: false,
    },
    ...overrides,
  }
}

describe('AdminStatisticsAgentsView', () => {
  it('renders the agent family vocabulary, naming each false flag', async () => {
    fetchAgentOverview.mockReset()
    fetchAgentOverview.mockResolvedValue(overview())

    render(<AdminStatisticsAgentsView />)

    await waitFor(() => {
      expect(screen.getByText(/per-step attribution is not instrumented/i)).toBeInTheDocument()
    })
    expect(screen.getByText(/cost is not instrumented/i)).toBeInTheDocument()
    expect(screen.getByText('Agent instrumentation')).toBeInTheDocument()
  })

  it('says "no runs recorded yet" — not "not instrumented" — when totalRuns is zero', async () => {
    fetchAgentOverview.mockReset()
    fetchAgentOverview.mockResolvedValue(
      overview({
        totalRuns: 0,
        running: 0,
        pausedForApproval: 0,
        succeeded: 0,
        failed: 0,
        successRate: null,
      }),
    )

    render(<AdminStatisticsAgentsView />)

    await waitFor(() => {
      expect(screen.getByText('no runs recorded yet')).toBeInTheDocument()
    })
    expect(screen.queryByText(/latency is not instrumented/)).toBeNull()
    expect(screen.queryByText(/not measured on this host/)).toBeNull()
  })

  it('renders a null success rate as "not measured", never as 0%', async () => {
    fetchAgentOverview.mockReset()
    fetchAgentOverview.mockResolvedValue(overview({ successRate: null }))

    render(<AdminStatisticsAgentsView />)

    await waitFor(() => {
      expect(screen.getAllByText('not measured').length).toBeGreaterThan(0)
    })
    expect(screen.queryByText('0.00%')).toBeNull()
  })

  it('renders an error rather than a fabricated all-clear when the call fails', async () => {
    fetchAgentOverview.mockReset()
    fetchAgentOverview.mockRejectedValue(new Error('403 Forbidden'))

    render(<AdminStatisticsAgentsView />)

    await waitFor(() => {
      expect(screen.getByText(/could not be loaded/i)).toBeInTheDocument()
    })
  })
})
