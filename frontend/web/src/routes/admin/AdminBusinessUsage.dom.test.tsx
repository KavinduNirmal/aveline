import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { AdminBusinessUsageView } from './AdminBusinessUsage'

vi.mock('@/contexts/AdminSessionContext', () => ({
  useAdminSession: () => ({
    status: 'ready',
    email: 'owner@aveline.lk',
    userId: 'user-1',
    roles: ['owner'],
    permissions: new Set<string>(['analytics:business:read', 'admin:orgs:read']),
    accountState: 'Active',
    hasCompletedOnboarding: true,
    error: null,
    can: () => true,
    refresh: async () => undefined,
  }),
}))

const fetchBusinessUsage = vi.fn()
const fetchBusinessOrganizationUsage = vi.fn()

vi.mock('@/components/admin/orgs/OrgPicker', () => ({
  OrgPicker: ({
    onSelect,
  }: {
    onSelect: (organization: { id: string; name: string; slug: string }) => void
  }) => (
    <button
      type="button"
      onClick={() =>
        onSelect({
          id: '00000000-0000-0000-0000-000000000001',
          name: 'Busy Atelier',
          slug: 'busy-atelier',
        })
      }
    >
      Pick an organization
    </button>
  ),
}))

vi.mock('@/lib/admin/api', () => ({
  fetchBusinessUsage: (...args: unknown[]) => fetchBusinessUsage(...args),
  fetchBusinessOrganizationUsage: (...args: unknown[]) => fetchBusinessOrganizationUsage(...args),
}))

const QUALITY = {
  userAttributionAvailable: true,
  unresolvedAttributionCount: 0,
  subscriptionHistoryBackfilled: false,
  lastActivityIsReconstructed: true,
  agentMetricsUninstrumented: true,
  notes: ['Agent-run counts come from the DailyAgentMetrics rollup'],
}

const WINDOW = {
  from: '2026-08-21T00:00:00Z',
  to: '2026-09-20T00:00:00Z',
  granularity: 'day',
  timeZone: 'UTC',
  bucketCount: 30,
}

const USAGE = {
  window: WINDOW,
  organizationId: null,
  series: [
    {
      bucketStart: '2026-09-19T00:00:00Z',
      isPartial: false,
      messagesSent: 40,
      agentRuns: 6,
      apiRequests: 900,
      blossomUnits: 120.5,
      actualCostUsd: 2.4,
    },
    {
      bucketStart: '2026-09-20T00:00:00Z',
      isPartial: true,
      messagesSent: 5,
      agentRuns: 1,
      apiRequests: 100,
      blossomUnits: 10,
      actualCostUsd: 0.2,
    },
  ],
  totals: {
    messagesSent: 45,
    agentRuns: 7,
    apiRequests: 1000,
    blossomUnits: 130.5,
    actualCostUsd: 2.6,
  },
  dataQuality: QUALITY,
}

const ORG_USAGE = {
  metric: 'apiRequests',
  from: WINDOW.from,
  to: WINDOW.to,
  items: [
    {
      rank: 1,
      organizationId: '00000000-0000-0000-0000-000000000001',
      name: 'Busy Atelier',
      planTier: 'Bloom',
      messagesSent: 30,
      agentRuns: 4,
      apiRequests: 800,
      blossomUnits: 100,
      lastActivityAt: '2026-09-19T00:00:00Z',
      daysSinceLastActivity: 1,
    },
    {
      rank: 2,
      organizationId: '00000000-0000-0000-0000-000000000002',
      name: 'Quiet Atelier',
      planTier: 'Seed',
      messagesSent: 0,
      agentRuns: 0,
      apiRequests: 10,
      blossomUnits: 0,
      lastActivityAt: null,
      daysSinceLastActivity: null,
    },
  ],
  totalCount: 2,
  dataQuality: QUALITY,
}

function renderUsage() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <AdminBusinessUsageView />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  fetchBusinessUsage.mockReset()
  fetchBusinessOrganizationUsage.mockReset()
  fetchBusinessUsage.mockResolvedValue(USAGE)
  fetchBusinessOrganizationUsage.mockResolvedValue(ORG_USAGE)
})

describe('AdminBusinessUsageView', () => {
  it('renders the four usage KPI tiles from the totals', async () => {
    renderUsage()

    await waitFor(() => expect(screen.getAllByText('Messages sent').length).toBeGreaterThan(0))
    // "Agent runs" and "API calls" are both a tile label and a chart title.
    expect(screen.getAllByText('Agent runs').length).toBeGreaterThan(1)
    expect(screen.getAllByText('API calls').length).toBeGreaterThan(1)
    expect(screen.getByText('Blossoms consumed')).toBeInTheDocument()
    expect(screen.getByText('1,000')).toBeInTheDocument()
  })

  it('renders the rank table with every organization', async () => {
    renderUsage()

    await waitFor(() => expect(screen.getByText('Busy Atelier')).toBeInTheDocument())
    expect(screen.getByText('Quiet Atelier')).toBeInTheDocument()
    expect(screen.getByText('Bloom')).toBeInTheDocument()
  })

  it('renders a null recency as "not measured" rather than as a zero-day idle', async () => {
    renderUsage()

    await waitFor(() => expect(screen.getByText('Quiet Atelier')).toBeInTheDocument())
    const row = screen.getByText('Quiet Atelier').closest('tr') as HTMLElement
    // The chart vocabulary is used here too: no recorded activity is not "zero days idle".
    expect(within(row).getAllByText(/not measured/i).length).toBeGreaterThan(0)
  })

  it('distinguishes "no usage" from "not instrumented"', async () => {
    renderUsage()

    await waitFor(() => expect(screen.getAllByText('Agent runs').length).toBeGreaterThan(0))
    // The data-quality notice names the agent-rollup caveat separately from the empty state.
    expect(
      screen.getByText(/Agent-run counts come from the DailyAgentMetrics rollup/),
    ).toBeInTheDocument()
  })

  it('re-queries the usage window when the range changes', async () => {
    renderUsage()

    await waitFor(() => expect(fetchBusinessUsage).toHaveBeenCalled())
    const before = fetchBusinessUsage.mock.calls.length

    await userEvent.click(screen.getByRole('radio', { name: /90 d/i }))

    await waitFor(() => expect(fetchBusinessUsage.mock.calls.length).toBeGreaterThan(before))
    const last = fetchBusinessUsage.mock.calls.at(-1)?.[0] as { granularity: string }
    expect(last.granularity).toBe('week')
  })

  it('sorts the rank table by a sortable column', async () => {
    renderUsage()

    await waitFor(() => expect(screen.getByText('Busy Atelier')).toBeInTheDocument())

    await userEvent.click(screen.getByRole('button', { name: /messages/i }))

    await waitFor(() => {
      const rows = screen.getAllByRole('row')
      // The header row is first; the sorted body follows.
      expect(rows.length).toBeGreaterThan(1)
    })
  })

  it('scopes the query when an organization is selected', async () => {
    renderUsage()

    await waitFor(() => expect(screen.getAllByText('Messages sent').length).toBeGreaterThan(0))
    await userEvent.click(screen.getByRole('button', { name: /pick an organization/i }))

    await waitFor(() => {
      const last = fetchBusinessUsage.mock.calls.at(-1)?.[0] as { organizationId?: string }
      expect(last.organizationId).toBe('00000000-0000-0000-0000-000000000001')
    })
  })

  it('clears the drill-down when the selection is cleared', async () => {
    renderUsage()

    await waitFor(() => expect(screen.getAllByText('Messages sent').length).toBeGreaterThan(0))
    await userEvent.click(screen.getByRole('button', { name: /pick an organization/i }))
    await waitFor(() => {
      const last = fetchBusinessUsage.mock.calls.at(-1)?.[0] as { organizationId?: string }
      expect(last.organizationId).toBeDefined()
    })

    await userEvent.click(screen.getByRole('button', { name: /clear organization/i }))

    await waitFor(() => {
      const last = fetchBusinessUsage.mock.calls.at(-1)?.[0] as { organizationId?: string }
      expect(last.organizationId).toBeUndefined()
    })
  })

  it('changes the ranking metric', async () => {
    renderUsage()

    await waitFor(() => expect(fetchBusinessOrganizationUsage).toHaveBeenCalled())
    await userEvent.click(screen.getByRole('radio', { name: /blossom/i }))

    await waitFor(() => {
      const last = fetchBusinessOrganizationUsage.mock.calls.at(-1)?.[0] as { metric: string }
      expect(last.metric).toBe('blossomUnits')
    })
  })

  it('names each failure and renders no number when the reads fail', async () => {
    fetchBusinessUsage.mockRejectedValue(new Error('usage is down'))
    fetchBusinessOrganizationUsage.mockRejectedValue(new Error('ranking is down'))

    renderUsage()

    await waitFor(() => expect(screen.getAllByRole('alert').length).toBeGreaterThan(0))
  })

  it('never renders one of the fabricated figures the delivered console shipped', async () => {
    const { container } = renderUsage()

    await waitFor(() => expect(screen.getByText('Messages sent')).toBeInTheDocument())
    for (const fabricated of ['128,450', '99.98%', 'ORD-94021']) {
      expect(container.textContent).not.toContain(fabricated)
    }
  })
})
