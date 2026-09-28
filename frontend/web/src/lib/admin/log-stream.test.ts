import { describe, expect, it, vi } from 'vitest'

import {
  computeCursor,
  createLogStream,
  DEFAULT_WINDOW_HOURS,
  deriveAdaptiveInterval,
  isNewer,
  LOG_BUFFER_LIMIT,
  MAX_WINDOW_DAYS,
  MAX_WINDOW_HOURS,
  mergeEntries,
  shouldPoll,
  type LogEntry,
  type LogPage,
} from './log-stream'

/**
 * The pure log engine. Every behaviour below is one of the defects the delivered viewer
 * shipped, so the fixture is shaped like the wire: `occurredAt` is the only timestamp the
 * audit endpoint exposes and `id` is the cursor.
 */

let sequence = 0
function entry(overrides: Partial<LogEntry> = {}): LogEntry {
  sequence += 1
  const id = (overrides.id ?? `01900000-0000-7000-8000-${String(sequence).padStart(12, '0')}`)
  return {
    id,
    occurredAt: '2026-01-01T00:00:00Z',
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

describe('mergeEntries', () => {
  it('deduplicates by id and keeps the newest first', () => {
    const older = entry({ id: '01900000-0000-7000-8000-000000000001' })
    const newer = entry({ id: '01900000-0000-7000-8000-000000000002' })

    // The incoming page repeats `older` (same-instant overlap) and adds `newer`.
    const merged = mergeEntries([newer, older], [older])

    expect(merged.map((row) => row.id)).toEqual([newer.id, older.id])
  })

  it('is idempotent for an identical page', () => {
    const rows = [entry(), entry()]
    expect(mergeEntries(rows, rows)).toHaveLength(2)
  })

  it('caps the buffer at the ring limit', () => {
    const existing = Array.from({ length: LOG_BUFFER_LIMIT }, (_, index) =>
      entry({ id: `01900000-0000-7000-8000-${String(index).padStart(12, '0')}` }),
    )
    const incoming = [entry({ id: '01900000-0000-7000-8000-999999999999' })]

    const merged = mergeEntries(existing, incoming)

    expect(merged).toHaveLength(LOG_BUFFER_LIMIT)
  })
})

describe('cursor arithmetic', () => {
  it('is null for an empty buffer', () => {
    expect(computeCursor([])).toBeNull()
  })

  it('returns the newest id', () => {
    const rows = [
      entry({ id: '01900000-0000-7000-8000-000000000003' }),
      entry({ id: '01900000-0000-7000-8000-000000000002' }),
    ]
    expect(computeCursor(rows)).toBe('01900000-0000-7000-8000-000000000003')
  })

  it('compares by id, not by timestamp', () => {
    const a = entry({ id: '01900000-0000-7000-8000-00000000000a', occurredAt: '2026-01-01T00:00:00Z' })
    const b = entry({ id: '01900000-0000-7000-8000-00000000000b', occurredAt: '2025-01-01T00:00:00Z' })
    expect(isNewer(b.id, a.id)).toBe(true)
    expect(isNewer(a.id, b.id)).toBe(false)
    expect(isNewer(a.id, null)).toBe(true)
    expect(isNewer(a.id, a.id)).toBe(false)
  })

  it('bounds the window to 24 h by default and 7 days at most', () => {
    expect(DEFAULT_WINDOW_HOURS).toBe(24)
    expect(MAX_WINDOW_DAYS).toBe(7)
    expect(MAX_WINDOW_HOURS).toBe(MAX_WINDOW_DAYS * 24)
  })
})

describe('adaptive polling', () => {
  it('drops the tick instead of queueing it when the document is hidden', () => {
    expect(shouldPoll({ paused: false, visible: false, inFlight: false })).toBe(false)
    expect(shouldPoll({ paused: true, visible: true, inFlight: false })).toBe(false)
  })

  it('drops the tick while a request is in flight — it never overlaps', () => {
    expect(shouldPoll({ paused: false, visible: true, inFlight: true })).toBe(false)
    expect(shouldPoll({ paused: false, visible: true, inFlight: false })).toBe(true)
  })

  it('backs off 1500 -> 10 000 ms and resets on new rows', () => {
    expect(deriveAdaptiveInterval(0)).toBe(1500)
    expect(deriveAdaptiveInterval(1)).toBe(1500)
    expect(deriveAdaptiveInterval(2)).toBe(3000)
    expect(deriveAdaptiveInterval(3)).toBe(6000)
    expect(deriveAdaptiveInterval(5)).toBe(10000)
  })
})

function page(rows: LogEntry[], total = rows.length, pageSize = rows.length || 1): LogPage {
  return { items: rows, page: 1, pageSize, total }
}

/**
 * The endpoint, with the server's own shapes: newest-first ordering, a `pageSize` the server
 * reports back, and a clamp (`AuditEndpoints.cs:20` caps at 200, below what the console may
 * ask for, so the response's `pageSize` — not the request's — decides whether a page was full).
 *
 * A read with no bound serves the newest window. A read with a bound serves the rows at or
 * below that row, **excluding** the `(instant, id)` pair the client already holds; that pair is
 * the exact continuation cursor (`AuditRepository.cs:77-78` orders by instant then by id), and
 * the engine skips it by `id` when a server echoes it back.
 */
function auditEndpoint(backlog: readonly LogEntry[], serverMax: number) {
  const calls: Array<{ page: number; from?: string | undefined; cursorId?: string | undefined }> = []
  const fetchPage = async ({
    page: requestedPage,
    from,
    cursorId,
  }: {
    page: number
    from?: string | undefined
    cursorId?: string | undefined
  }) => {
    calls.push({ page: requestedPage, from, cursorId })
    const rows = backlog.filter((row) => {
      if (from === undefined || cursorId === undefined) return true
      if (row.occurredAt < from) return true
      if (row.occurredAt > from) return false
      return row.id < cursorId
    })
    return page(rows.slice(0, serverMax), rows.length, serverMax)
  }
  return { calls, fetchPage }
}

/**
 * Allocates `count` entries, newest first, with descending ids. The instants sit inside the
 * 24 h retention window Q3 approved and are anchored to the wall clock, because the window is
 * measured from now: a fixture with fixed dates drops out of the window as time passes.
 */
function newestFirst(count: number, startAt = 1000): LogEntry[] {
  const base = Date.now()
  return Array.from({ length: count }, (_, index) =>
    entry({
      id: `01900000-0000-7000-8000-${String(startAt - index).padStart(12, '0')}`,
      occurredAt: new Date(base - index * 1000).toISOString(),
    }),
  )
}

describe('the log engine across a poll that emits more than pageSize rows', () => {
  it('advances the cursor by id and loses no entry', async () => {
    const backlog = newestFirst(250)
    const { calls, fetchPage } = auditEndpoint(backlog, 100)

    const engine = createLogStream({ pageSize: 100, fetchPage })

    await engine.tick()

    expect(calls).toHaveLength(3)
    expect(engine.snapshot().entries).toHaveLength(250)
    expect(engine.snapshot().cursor).toBe(backlog[0].id)
    expect(engine.snapshot().dropped).toBe(0)
  })

  it('keeps every entry when the server clamps the page size below the request', async () => {
    const backlog = newestFirst(120)
    const { calls, fetchPage } = auditEndpoint(backlog, 50)

    // The console asks for more than the endpoint allows; the delivered viewer treated the
    // shorter-than-requested page as "finished" and silently lost the rest.
    const engine = createLogStream({ pageSize: 500, fetchPage })

    await engine.tick()

    expect(calls.map((call) => call.page)).toEqual([1, 2, 3])
    expect(engine.snapshot().entries).toHaveLength(120)
  })

  it('re-reads the boundary instant rather than skipping a same-instant neighbour', async () => {
    const shared = '2026-01-01T00:00:05.000Z'
    const backlog = [
      entry({ id: '01900000-0000-7000-8000-000000000010', occurredAt: shared }),
      entry({ id: '01900000-0000-7000-8000-000000000009', occurredAt: shared }),
      entry({ id: '01900000-0000-7000-8000-000000000008', occurredAt: shared }),
    ]
    const { fetchPage } = auditEndpoint(backlog, 2)

    const engine = createLogStream({ pageSize: 2, fetchPage })
    await engine.tick()

    expect(engine.snapshot().entries).toHaveLength(3)
  })
})

describe('single-flight and stalled responses', () => {
  it('never overlaps a request and drops ticks while one is stalled', async () => {
    let resolveFirst: ((value: LogPage) => void) | undefined
    let calls = 0
    const fetchPage = vi.fn(() => {
      calls += 1
      // Only the first response stalls. A later continuation (a full page would trigger one)
      // resolves empty so the test pins the stall, not the paging chain.
      if (calls > 1) return Promise.resolve(page([], 0, 1))
      return new Promise<LogPage>((resolve) => {
        resolveFirst = resolve
      })
    })


    const engine = createLogStream({ pageSize: 50, fetchPage })

    const first = engine.tick()
    expect(engine.snapshot().inFlight).toBe(true)

    // Three ticks land while the first response is stalled. None may queue.
    await engine.tick()
    await engine.tick()
    await engine.tick()

    expect(fetchPage).toHaveBeenCalledTimes(1)
    expect(engine.snapshot().droppedTicks).toBe(3)

    // A short page, so the engine stops after this one: a full page would make it keep
    // paging, which is correct but not what this test is pinning.
    resolveFirst?.(page(newestFirst(1), 1, 1))
    await first

    expect(engine.snapshot().inFlight).toBe(false)
    expect(engine.snapshot().entries).toHaveLength(1)
  })

  it('does not poll while the document is hidden', async () => {
    const fetchPage = vi.fn(async () => page(newestFirst(1), 1, 50))
    const engine = createLogStream({ pageSize: 50, fetchPage, visible: () => false })

    await engine.tick()

    expect(fetchPage).not.toHaveBeenCalled()
  })

  it('resumes on visibilitychange and stops polling while hidden', async () => {
    const fetchPage = vi.fn(async () => page(newestFirst(1), 1, 50))
    let visible = true
    const engine = createLogStream({
      pageSize: 50,
      fetchPage,
      visible: () => visible,
    })

    await engine.tick()
    expect(fetchPage).toHaveBeenCalledTimes(1)

    visible = false
    await engine.tick()
    expect(fetchPage).toHaveBeenCalledTimes(1)

    visible = true
    await engine.tick()
    expect(fetchPage).toHaveBeenCalledTimes(2)
  })

  it('aborts the in-flight request when stopped', async () => {
    let captured: AbortSignal | undefined
    const fetchPage = vi.fn(
      (request: { page: number; from?: string; signal: AbortSignal }) =>
        new Promise<LogPage>((_resolve, reject) => {
          captured = request.signal
          request.signal.addEventListener('abort', () => {
            reject(new DOMException('Aborted', 'AbortError'))
          })
        }),
    )

    const engine = createLogStream({ pageSize: 50, fetchPage })
    const pending = engine.tick()
    engine.stop()

    expect(captured?.aborted).toBe(true)
    await pending
    expect(engine.snapshot().inFlight).toBe(false)
  })

  it('counts ring-buffer evictions as a visible dropped total', async () => {
    const backlog = newestFirst(LOG_BUFFER_LIMIT + 25)
    const { fetchPage } = auditEndpoint(backlog, LOG_BUFFER_LIMIT)

    const engine = createLogStream({
      pageSize: LOG_BUFFER_LIMIT,
      bufferLimit: LOG_BUFFER_LIMIT,
      fetchPage,
    })

    await engine.tick()

    expect(engine.snapshot().entries).toHaveLength(LOG_BUFFER_LIMIT)
    expect(engine.snapshot().dropped).toBe(25)
  })
})

describe('paging the live edge with a same-instant overlap', () => {
  it('resumes the next poll at the oldest held instant, not the newest', async () => {
    const rows = newestFirst(3, 3)
    const { calls, fetchPage } = auditEndpoint(rows, 50)
    const engine = createLogStream({ pageSize: 50, fetchPage })

    await engine.tick()
    await engine.tick()

    // First poll: the retention window's floor. Second poll: the oldest held row's instant —
    // never the newest, or two entries sharing that instant could be skipped forever.
    expect(calls[0]?.from).toBeDefined()
    expect(calls[1]?.from).toBe(rows[rows.length - 1]?.occurredAt)
  })

  it('the first poll is bounded by the retention window', async () => {
    const now = Date.now()
    const stale = entry({ occurredAt: new Date(now - 40 * 24 * 3_600_000).toISOString() })
    const { calls, fetchPage } = auditEndpoint([stale], 50)
    const engine = createLogStream({ pageSize: 50, fetchPage, windowHours: 24 })

    await engine.tick()

    const floor = calls[0]?.from
    expect(floor).toBeDefined()
    // 24 h ago, not 40 days ago: the console may not widen its own retention.
    expect(Date.parse(String(floor))).toBeGreaterThan(now - 25 * 3_600_000)
    expect(Date.parse(String(floor))).toBeLessThan(now)
  })
})
