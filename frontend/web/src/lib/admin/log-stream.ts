/**
 * The pure log engine (A7).
 *
 * No React and no `fetch`: the whole streaming contract is testable in the `node` project.
 * What it encodes, and the defect each rule pins:
 *
 * - **The cursor is the `id`, not a timestamp.** `/admin/audit` orders by `CreatedAt DESC` then
 *   `Id DESC` (`AuditRepository.cs:77-78`), which makes an offset read of the list correct, and
 *   a UUIDv7 `id` is the only cursor that stays stable under concurrent inserts. The delivered
 *   viewer used `occurredAt` and a rolling 60 s window, so an entry inserted at the same instant
 *   as the newest one was skipped forever.
 * - **A poll that emits more than `pageSize` rows is paged, not truncated.** A full page is
 *   followed by the next page at the **oldest** row of the window, so no entry is lost even when
 *   the server clamps the page size below what was requested.
 * - **Single flight.** A tick that lands while a request is in flight is **dropped and counted**,
 *   never queued: the delivered fixed-1500 ms interval accumulated a backlog of concurrent
 *   requests around a stalled response.
 * - **No requests while the document is hidden**, and the in-flight request is aborted on stop.
 * - **A bounded ring buffer with a visible drop count**, so the viewer can say what it discarded
 *   rather than silently trimming.
 * - **Adaptive 1500 → 10 000 ms**, so an idle console stops hammering the endpoint.
 *
 * Authority: plan §5 (the A7 row), §6 tests 18/19, strategy §2.3 (C-3), and Q3.
 */

import type { AuditLogEntry } from '@/types/admin'

export type LogEntry = AuditLogEntry

export interface LogPage {
  items: LogEntry[]
  page: number
  pageSize: number
  total: number
}

export interface LogPageRequest {
  page: number
  /** The lower bound of the window, inclusive, taken from the oldest row already held. */
  from?: string
  /**
   * That same row's `id`. The endpoint filters by instant only, so one row sharing the boundary
   * instant always comes back; skipping it by `id` is what lets a full page advance without
   * dropping a neighbour.
   */
  cursorId?: string
  signal: AbortSignal
}

export type LogConnectionStatus = 'idle' | 'polling' | 'live' | 'paused' | 'hidden' | 'error'

/** The ring buffer's ceiling. 2000 rows is the plan's number. */
export const LOG_BUFFER_LIMIT = 2000

/** The live feed's floor interval. */
export const BASE_POLL_INTERVAL_MS = 1500

/** The ceiling the adaptive interval backs off to. */
export const MAX_POLL_INTERVAL_MS = 10000

/** Q3: the default retention window is 24 h. */
export const DEFAULT_WINDOW_HOURS = 24

/** Q3: the maximum retention window is 7 days. */
export const MAX_WINDOW_DAYS = 7

/** Q3: the maximum in hours, so the UI can state both bound and unit. */
export const MAX_WINDOW_HOURS = MAX_WINDOW_DAYS * 24

export const WINDOW_HOUR_OPTIONS = [1, 6, 24, 72, 168] as const

export type WindowHours = (typeof WINDOW_HOUR_OPTIONS)[number]

/** The newest `id` in the buffer, which is the cursor the next poll resumes from. */
export function computeCursor(entries: readonly LogEntry[]): string | null {
  let cursor: string | null = null
  for (const entry of entries) {
    if (cursor === null || entry.id > cursor) cursor = entry.id
  }
  return cursor
}

/**
 * UUIDv7 is lexicographically and temporally ordered, so `id > cursor` is the live-edge test.
 * A `null` cursor accepts everything, which is the first poll.
 */
export function isNewer(id: string, cursor: string | null): boolean {
  if (cursor === null) return true
  return id > cursor
}

/**
 * Merges a page into the buffer: deduplicate by `id`, newest first, capped at the ring limit.
 * Idempotent, because the overlap window deliberately re-reads the boundary instant.
 */
export function mergeEntries(
  existing: readonly LogEntry[],
  incoming: readonly LogEntry[],
  limit: number = LOG_BUFFER_LIMIT,
): LogEntry[] {
  const seen = new Set<string>()
  const merged: LogEntry[] = []
  for (const entry of [...existing, ...incoming]) {
    if (seen.has(entry.id)) continue
    seen.add(entry.id)
    merged.push(entry)
  }
  merged.sort((left, right) => (left.id < right.id ? 1 : left.id > right.id ? -1 : 0))
  return merged.slice(0, limit)
}

export interface ShouldPollInput {
  paused: boolean
  visible: boolean
  inFlight: boolean
}

/**
 * A tick is dropped — never queued — when the feed is paused, the document is hidden, or a
 * response has not settled. The caller counts the drop.
 */
export function shouldPoll({ paused, visible, inFlight }: ShouldPollInput): boolean {
  if (paused || !visible || inFlight) return false
  return true
}

/** 1500 ms, doubling on each consecutive empty poll, capped at 10 000 ms. */
export function deriveAdaptiveInterval(emptyPolls: number): number {
  let interval = BASE_POLL_INTERVAL_MS
  const doublings = Math.min(Math.max(emptyPolls - 1, 0), 8)
  for (let index = 0; index < doublings; index += 1) {
    interval = Math.min(interval * 2, MAX_POLL_INTERVAL_MS)
  }
  return interval
}

export interface LogStreamSnapshot {
  entries: LogEntry[]
  cursor: string | null
  /** Rows discarded by the ring buffer. Visible, so the buffer never silently trims. */
  dropped: number
  bufferLimit: number
  /** Ticks skipped because a response was still in flight. */
  droppedTicks: number
  /** Ticks skipped because the document was hidden. */
  hiddenTicks: number
  inFlight: boolean
  status: LogConnectionStatus
  lastPolledAt: string | null
  emptyPolls: number
  /** Consecutive failed polls, so a run of errors is visible rather than indistinguishable. */
  errorCount: number
}

export interface LogStreamOptions {
  fetchPage: (request: LogPageRequest) => Promise<LogPage>
  /** Wire page size. The server clamps it, so the response's own `pageSize` is authoritative. */
  pageSize?: number
  bufferLimit?: number
  windowHours?: number
  /** Injected so tests can suspend and resume polling without a real document. */
  visible?: () => boolean
  /** Injected so the adaptive interval can be asserted without waiting. */
  setTimer?: (handler: () => void, ms: number) => unknown
  clearTimer?: (handle: unknown) => void
}

export interface LogStream {
  snapshot: () => LogStreamSnapshot
  subscribe: (listener: (snapshot: LogStreamSnapshot) => void) => () => void
  start: () => void
  stop: () => void
  /** One poll opportunity: it either issues a request or drops the tick and counts it. */
  tick: () => Promise<void>
  setPaused: (paused: boolean) => void
  isPaused: () => boolean
  setWindowHours: (hours: number) => void
  windowHours: () => number
  /** The delay the next tick should be scheduled at. */
  nextIntervalMs: () => number
}

const MAX_CONSECUTIVE_ERRORS = 5

/**
 * A hard ceiling on one poll's page chain. The window itself bounds the feed to 7 days, so this
 * is a runaway guard, not a truncation: a server that ignored `page` and always returned a full
 * page would otherwise spin forever. Reaching it is treated as a failure rather than a silent
 * partial read.
 */
const MAX_PAGES_PER_POLL = 200

function defaultVisible(): boolean {
  if (typeof document === 'undefined') return true
  return document.visibilityState !== 'hidden'
}

/**
 * Builds the engine. The returned object owns no timer of its own when `setTimer` is omitted:
 * `start` schedules the loop, and a React caller may instead drive `tick` from an effect.
 */
export function createLogStream(options: LogStreamOptions): LogStream {
  const {
    fetchPage,
    pageSize = 100,
    bufferLimit = LOG_BUFFER_LIMIT,
    visible = defaultVisible,
    setTimer = (handler, ms) => setTimeout(handler, ms),
    clearTimer = (handle) => clearTimeout(handle as ReturnType<typeof setTimeout>),
  } = options

  let entries: LogEntry[] = []
  let cursor: string | null = null
  let dropped = 0
  let droppedTicks = 0
  let hiddenTicks = 0
  let inFlight = false
  let paused = false
  let status: LogConnectionStatus = 'idle'
  let lastPolledAt: string | null = null
  let emptyPolls = 0
  let errorCount = 0
  let hours = DEFAULT_WINDOW_HOURS
  let controller: AbortController | null = null
  let timer: unknown = null
  /**
   * `running` is the loop gate and `stopped` is the abort gate. They are separate on purpose:
   * `tick()` is a step function a test can drive without ever starting the loop, while `stop()`
   * must abort an in-flight request and refuse later ticks.
   */
  let running = false
  let stopped = false
  const listeners = new Set<(snapshot: LogStreamSnapshot) => void>()

  function snapshot(): LogStreamSnapshot {
    return {
      entries,
      cursor,
      dropped,
      bufferLimit,
      droppedTicks,
      hiddenTicks,
      inFlight,
      status,
      lastPolledAt,
      emptyPolls,
      errorCount,
    }
  }

  function emit(): void {
    const current = snapshot()
    for (const listener of listeners) listener(current)
  }

  function schedule(): void {
    if (!running || stopped) return
    if (timer !== null) clearTimer(timer)
    timer = setTimer(() => {
      timer = null
      void tick().finally(() => schedule())
    }, deriveAdaptiveInterval(emptyPolls))
  }

  /**
   * Pages the live edge until a short page proves there is nothing older still unseen.
   *
   * `resumeFrom` is a later poll's lower bound: the oldest row already held, plus that row's
   * `id` as the exact continuation cursor. The bound is deliberately **not** floored at the
   * retention window here — the floor applies only to the first read, so a busy window cannot
   * lose the entries between the floor and the oldest held row. A server that echoes the
   * boundary row back is handled by skipping it by `id`.
   */
  async function collect(
    signal: AbortSignal,
    resumeFrom: string | undefined,
    resumeCursorId: string | undefined,
  ): Promise<{ page: LogPage; items: LogEntry[] } | null> {
    const collected: LogEntry[] = []
    let requestedPage = 1
    let serverPageSize = pageSize
    let first: LogPage | null = null
    let from = resumeFrom
    let cursorId = resumeCursorId

    // Bounded so a server that ignores `page` and always returns a full page cannot spin.
    for (let request = 0; request < MAX_PAGES_PER_POLL; request += 1) {
      const data = await fetchPage({ page: requestedPage, from, cursorId, signal })
      if (signal.aborted) return null
      if (first === null) first = data
      // The response's own page size is authoritative: the server clamps below what we asked
      // for, and the delivered viewer read `items.length < requestedPageSize` as "done", which
      // silently truncated a busy window.
      serverPageSize = data.pageSize > 0 ? data.pageSize : pageSize
      // The endpoint filters by instant, so the boundary row echoes back on every continuation.
      // Skip it by `id`, which is exact; a mid-batch row that happens to share the instant is
      // still kept.
      const fresh = cursorId === undefined
        ? data.items
        : data.items.filter((row) => row.id !== cursorId)
      collected.push(...fresh)
      if (data.items.length === 0) break
      if (data.items.length < serverPageSize) break
      const oldest = collected.reduce<LogEntry | null>(
        (min, row) => (min === null || row.occurredAt < min.occurredAt ? row : min),
        null,
      )
      const nextFrom = oldest?.occurredAt
      const nextCursorId = oldest?.id
      // A server that echoes the same window forever would otherwise spin: the boundary must
      // move strictly below what we hold before another request is worth making.
      if (nextFrom === undefined || nextFrom === from) break
      if (request === MAX_PAGES_PER_POLL - 1) {
        throw new Error(
          `the audit endpoint returned ${MAX_PAGES_PER_POLL} full pages for one poll; the cursor cannot advance`,
        )
      }
      requestedPage += 1
      from = nextFrom
      cursorId = nextCursorId
    }

    return first === null ? null : { page: { ...first, items: collected }, items: collected }
  }

  /** The oldest instant already held. Never the newest. */
  function oldestHeld(): string | undefined {
    return entries[entries.length - 1]?.occurredAt
  }

  /** The oldest row's `id`, which is the exact continuation cursor. */
  function oldestHeldId(): string | undefined {
    return entries[entries.length - 1]?.id
  }

  /**
   * Bounds a lower bound to the retention window (24 h default, 7 days maximum — Q3). A row
   * older than the window is not asked for again, so the console cannot quietly widen its own
   * retention.
   *
   * The comparison is done on the ISO-8601 strings themselves rather than on `Date.parse` +
   * `toISOString`, because the round trip drops sub-second precision: a bound of `…:01.842Z`
   * became `…:01.000Z`, an **earlier** instant, which would re-read a second of the feed on
   * every poll.
   */
  function boundToWindow(value: string | undefined): string | undefined {
    const floorMs = Date.now() - hours * 3_600_000
    if (value === undefined) return new Date(floorMs).toISOString()
    const floorIso = new Date(floorMs).toISOString()
    if (!Number.isFinite(Date.parse(value))) return floorIso
    return value < floorIso ? floorIso : value
  }

  function apply(items: LogEntry[]): void {
    const before = entries.length
    const merged = mergeEntries(entries, items, bufferLimit)
    // Every row that fell off the tail is a dropped row, and the count is visible. Deduplication
    // is counted out first, so a deliberate same-instant overlap is never reported as a loss.
    const distinct = new Set(items.map((row) => row.id)).size - countOverlap(items)
    dropped += Math.max(0, before + distinct - merged.length)
    entries = merged
    cursor = computeCursor(entries)
  }

  function countOverlap(items: readonly LogEntry[]): number {
    if (items.length === 0) return 0
    const held = new Set(entries.map((row) => row.id))
    let overlap = 0
    for (const row of items) if (held.has(row.id)) overlap += 1
    return overlap
  }

  function dropReason(): 'hidden' | 'inflight' | 'paused' | null {
    if (paused) return 'paused'
    if (!visible()) return 'hidden'
    if (inFlight) return 'inflight'
    return null
  }

  /**
   * One poll opportunity. The gate is **synchronous**: a tick that arrives while a response is
   * in flight is dropped and counted on the spot, so it can never queue behind the stalled
   * request. The delivered fixed interval accumulated exactly that queue.
   */
  function tick(): Promise<void> {
    if (stopped) return Promise.resolve()
    const reason = dropReason()
    if (reason !== null) {
      if (reason === 'hidden') hiddenTicks += 1
      if (reason === 'inflight') droppedTicks += 1
      status = reason === 'hidden' ? 'hidden' : reason === 'paused' ? 'paused' : status
      emit()
      return Promise.resolve()
    }

    inFlight = true
    status = 'polling'
    controller = new AbortController()
    const signal = controller.signal
    emit()
    return poll(signal)
  }

  async function poll(signal: AbortSignal): Promise<void> {
    try {
      // First read: the retention window's floor. Later reads: the oldest row already held, so
      // a same-instant neighbour is never skipped. The held value is inside the window already
      // (the floor was applied when it was fetched), so it needs no further bounding.
      const held = oldestHeld()
      const resumeFrom = held ?? boundToWindow(undefined)
      const result = await collect(signal, resumeFrom, held === undefined ? undefined : oldestHeldId())
      if (result === null || signal.aborted) return
      const measured = result.items
      apply(measured)
      lastPolledAt = new Date().toISOString()
      errorCount = 0
      if (measured.length === 0) {
        emptyPolls += 1
      } else {
        emptyPolls = 0
        droppedTicks = 0
      }
      status = 'live'
    } catch (error: unknown) {
      if (signal.aborted || (error instanceof DOMException && error.name === 'AbortError')) {
        return
      }
      errorCount += 1
      emptyPolls += 1
      // A run of failures is not a live connection, and saying so is the honest state.
      status = errorCount >= MAX_CONSECUTIVE_ERRORS ? 'error' : 'idle'
    } finally {
      inFlight = false
      controller = null
      emit()
    }
  }

  return {
    snapshot,
    subscribe(listener) {
      listeners.add(listener)
      return () => {
        listeners.delete(listener)
      }
    },
    start() {
      if (running || stopped) return
      running = true
      void tick().finally(() => schedule())
    },
    stop() {
      running = false
      stopped = true
      controller?.abort()
      if (timer !== null) {
        clearTimer(timer)
        timer = null
      }
      inFlight = false
      status = 'idle'
      emit()
    },
    tick,
    setPaused(next) {
      paused = next
      status = next ? 'paused' : 'idle'
      if (next) controller?.abort()
      emit()
    },
    isPaused() {
      return paused
    },
    setWindowHours(next) {
      hours = Math.min(Math.max(next, 1), MAX_WINDOW_HOURS)
    },
    windowHours() {
      return hours
    },
    nextIntervalMs() {
      return deriveAdaptiveInterval(emptyPolls)
    },
  }
}
