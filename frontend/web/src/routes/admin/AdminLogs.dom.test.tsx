import { render, screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { AdminLogsView } from './AdminLogs'

const queryAuditEntries = vi.fn()
vi.mock('@/lib/admin/api', () => ({
  queryAuditEntries: (...args: unknown[]) => queryAuditEntries(...args),
}))

vi.mock('@/contexts/NotificationsContext', () => ({
  useNotifications: () => ({ connectionState: 'Connected', lastNotification: null }),
}))

const DEFAULT_WINDOW_MS = 24 * 3_600_000
const MAX_WINDOW_MS = 7 * 24 * 3_600_000

function entry(overrides: Record<string, unknown> = {}) {
  return {
    id: '01900000-0000-7000-8000-000000000001',
    occurredAt: new Date().toISOString(),
    organizationId: null,
    actorKind: 'User',
    actorUserId: 'user-1',
    actorRef: null,
    action: 'users.state.updated',
    entityType: 'User',
    entityId: 'entity-1',
    reason: null,
    requestId: 'req-1',
    before: null,
    after: null,
    ...overrides,
  }
}

function paged(items: unknown[], pageSize = 200) {
  return { items, page: 1, pageSize, total: items.length }
}

describe('AdminLogsView', () => {
  it('never marks the feed as an aria-live region', async () => {
    queryAuditEntries.mockReset()
    queryAuditEntries.mockResolvedValue(
      paged([
        entry({ id: '01900000-0000-7000-8000-000000000002', action: 'users.state.update_failed' }),
        entry({ id: '01900000-0000-7000-8000-000000000001' }),
      ]),
    )

    const { container } = render(<AdminLogsView />)

    await waitFor(() => {
      expect(container.querySelectorAll('[data-entry-id]').length).toBeGreaterThan(0)
    })

    // An aria-live feed reads every arriving row aloud, forever. The connection chip is the
    // only live region on the page, and it is `role="status"` with a controlled message.
    expect(container.querySelectorAll('[aria-live]')).toHaveLength(0)
    expect(container.querySelectorAll('[role="log"]')).toHaveLength(0)
  })

  it('states the 24 h default and the 7 day maximum window', async () => {
    queryAuditEntries.mockReset()
    queryAuditEntries.mockResolvedValue(paged([entry()]))

    render(<AdminLogsView />)

    const statement = await screen.findByTestId('log-retention-window')
    expect(statement).toHaveTextContent('24 h')
    expect(statement).toHaveTextContent('7 days')
  })

  it('requests only the retention window, never an unbounded feed', async () => {
    queryAuditEntries.mockReset()
    queryAuditEntries.mockResolvedValue(paged([entry()]))

    render(<AdminLogsView />)

    await waitFor(() => {
      expect(queryAuditEntries).toHaveBeenCalled()
    })

    const firstCall = queryAuditEntries.mock.calls[0]?.[0] as { from?: string; pageSize?: number }
    expect(firstCall.from).toBeDefined()
    const from = Date.parse(String(firstCall.from))
    const now = Date.now()
    expect(now - from).toBeLessThanOrEqual(DEFAULT_WINDOW_MS + 5_000)
    expect(now - from).toBeGreaterThan(DEFAULT_WINDOW_MS - 60_000)
    expect(now - from).toBeLessThan(MAX_WINDOW_MS)
  })

  it('advances by id across a poll that emits more than pageSize rows without losing an entry', async () => {
    queryAuditEntries.mockReset()
    const rows = Array.from({ length: 250 }, (_, index) =>
      entry({
        id: `01900000-0000-7000-8000-${String(400 - index).padStart(12, '0')}`,
        occurredAt: new Date(Date.now() - index * 1000).toISOString(),
      }),
    )
    let calls = 0
    queryAuditEntries.mockImplementation(async (params: { from?: string } = {}) => {
      calls += 1
      // Read 1 is the window floor: an inclusive lower bound. Every later read is the exact
      // continuation cursor, so it is strictly below the oldest row already held.
      const bounded =
        calls === 1
          ? rows.filter(
              (row) => params.from === undefined || row.occurredAt >= String(params.from),
            )
          : rows.filter((row) => row.occurredAt < String(params.from))
      return paged(bounded.slice(0, 200), 200)
    })

    render(<AdminLogsView />)

    // A burst of 250 rows is two reads: a full page, then the remainder. The second read resumes
    // at the oldest row already held, never at the newest instant.
    await waitFor(() => {
      expect(queryAuditEntries.mock.calls.length).toBeGreaterThanOrEqual(2)
    })
    const firstFrom = Date.parse(String(queryAuditEntries.mock.calls[0]?.[0]?.from))
    const secondFrom = Date.parse(String(queryAuditEntries.mock.calls[1]?.[0]?.from))
    expect(secondFrom).toBeGreaterThan(firstFrom)

    // Every one of the 250 rows is in the buffer, and the feed renders a bounded window of it.
    await waitFor(() => {
      expect(screen.getByTestId('log-buffer')).toHaveTextContent('250 / 2,000 buffered')
    })
    expect(document.querySelectorAll('[data-entry-id]').length).toBe(200)
  })

  it('shows the buffer bound and the dropped count', async () => {
    queryAuditEntries.mockReset()
    queryAuditEntries.mockResolvedValue(paged([entry()], 200))

    render(<AdminLogsView />)

    await waitFor(() => {
      expect(screen.getByText(/2,?000/)).toBeInTheDocument()
    })
  })

  it('labels derived severity rather than presenting it as server truth', async () => {
    queryAuditEntries.mockReset()
    queryAuditEntries.mockResolvedValue(paged([entry({ action: 'users.state.update_failed' })]))

    render(<AdminLogsView />)

    await waitFor(() => {
      expect(screen.getAllByText(/derived/i).length).toBeGreaterThan(0)
    })
  })

  it('names the actorKind as the primary source', async () => {
    queryAuditEntries.mockReset()
    queryAuditEntries.mockResolvedValue(paged([entry({ actorKind: 'Scheduler' })]))

    render(<AdminLogsView />)

    await waitFor(() => {
      expect(screen.getByText('Scheduler')).toBeInTheDocument()
    })
  })

  it('renders the connection chip as the only status region', async () => {
    queryAuditEntries.mockReset()
    queryAuditEntries.mockResolvedValue(paged([]))

    render(<AdminLogsView />)

    const status = await screen.findByRole('status')
    expect(within(status).getByText(/live|polling|paused|disconnected/i)).toBeInTheDocument()
  })
})
