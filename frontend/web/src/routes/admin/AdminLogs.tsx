import { useEffect, useMemo, useRef, useState } from 'react'

import { Button } from '@/components/ui/button'
import { EMPTY_LOG_FILTERS, type LogFilterState } from '@/components/admin/logs/LogFilters'
import { LogViewer } from '@/components/admin/logs/LogViewer'

import { useNotifications } from '@/contexts/NotificationsContext'
import { queryAuditEntries } from '@/lib/admin/api'
import {
  createLogStream,
  DEFAULT_WINDOW_HOURS,
  MAX_WINDOW_DAYS,
  type LogPage,
  type LogStreamSnapshot,
} from '@/lib/admin/log-stream'

/** The audit endpoint's own ceiling (`AuditEndpoints.cs:20`); asking for more is clamped. */
const AUDIT_PAGE_SIZE = 200

/** A SignalR alert is a hint, not a command: at most one hinted poll per 10 s. */
const ALERT_HINT_INTERVAL_MS = 10_000

/**
 * The live log stream.
 *
 * Everything streaming-related lives in the pure `createLogStream` engine: the `id` cursor, the
 * bounded ring buffer, the 1500 → 10 000 ms adaptive interval, the single-flight guard that
 * **drops** a tick rather than queueing it, and the visibility suspension. This component only
 * renders the engine's snapshots and drives the ticks.
 *
 * The default window is **24 h** with a **7 day** maximum (Q3), and both are stated in the UI.
 * The SignalR `SystemAlert` notification is used as a refresh hint, rate-limited so an alert
 * burst cannot become a request burst.
 */
export function AdminLogsView() {
  const { lastNotification } = useNotifications()
  const [snapshot, setSnapshot] = useState<LogStreamSnapshot | null>(null)
  const [filters, setFilters] = useState<LogFilterState>(EMPTY_LOG_FILTERS)
  const hintRef = useRef(0)

  const engine = useMemo(
    () =>
      createLogStream({
        pageSize: AUDIT_PAGE_SIZE,
        windowHours: DEFAULT_WINDOW_HOURS,
        // The audit endpoint filters by instant, so the oldest held row's instant is the
        // continuation bound. The engine removes the boundary row it already holds.
        fetchPage: async ({ page, from }): Promise<LogPage> =>
          queryAuditEntries({ from, page, pageSize: AUDIT_PAGE_SIZE }),
      }),
    [],
  )

  useEffect(() => {
    setSnapshot(engine.snapshot())
    const unsubscribe = engine.subscribe(setSnapshot)
    engine.start()
    return () => {
      unsubscribe()
      engine.stop()
    }
  }, [engine])

  useEffect(() => {
    if (lastNotification === null) return
    const now = Date.now()
    if (now - hintRef.current < ALERT_HINT_INTERVAL_MS) return
    hintRef.current = now
    void engine.tick()
  }, [engine, lastNotification])

  const current = snapshot ?? engine.snapshot()
  const paused = current.status === 'paused'

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="font-serif text-2xl font-medium tracking-tight text-foreground">
            Real-time audit stream
          </h2>
          <p className="text-sm text-muted-foreground">
            Incremental reads keyed on the entry id, over a {DEFAULT_WINDOW_HOURS} h window with a{' '}
            {MAX_WINDOW_DAYS} day maximum. Severity is derived by the console and labelled as
            derived; the server sends no level.
          </p>
        </div>

        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            className="h-8 text-xs"
            onClick={() => engine.setPaused(!paused)}
          >
            {paused ? 'Resume stream' : 'Pause stream'}
          </Button>
          <Button
            variant="outline"
            size="sm"
            className="h-8 text-xs"
            onClick={() => void engine.tick()}
          >
            Poll now
          </Button>
        </div>
      </div>

      <LogViewer
        entries={current.entries}
        status={current.status}
        lastPolledAt={current.lastPolledAt}
        dropped={current.dropped}
        bufferLimit={current.bufferLimit}
        droppedTicks={current.droppedTicks}
        windowHours={engine.windowHours()}
        filters={filters}
        onFiltersChange={setFilters}
      />
    </div>
  )
}
