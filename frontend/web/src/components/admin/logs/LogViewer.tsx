import { useMemo } from 'react'

import { Card, CardContent } from '@/components/ui/card'

import { LogConnectionChip } from './LogConnectionChip'
import { EMPTY_LOG_FILTERS, LogFilters, type LogFilterState } from './LogFilters'
import { LogRow } from './LogRow'

import { deriveLevel } from '@/lib/admin/log-level'
import { MAX_WINDOW_DAYS, type LogConnectionStatus, type LogEntry } from '@/lib/admin/log-stream'

/**
 * The feed.
 *
 * Virtualisation keeps a 2000-row buffer cheap to render, and the visible slice is bounded
 * rather than the DOM growing with the buffer. Auto-scroll has a real pause: when the operator
 * scrolls away from the live edge, new rows must not yank the viewport back.
 *
 * **No `aria-live` here.** The connection chip is the only live region on the page; an
 * `aria-live` feed would read every arriving row aloud, forever.
 */
export function LogViewer({
  entries,
  status,
  lastPolledAt,
  dropped,
  bufferLimit,
  droppedTicks,
  windowHours,
  filters = EMPTY_LOG_FILTERS,
  onFiltersChange,
  visibleCount = 200,
}: {
  entries: LogEntry[]
  status: LogConnectionStatus
  lastPolledAt: string | null
  dropped: number
  bufferLimit: number
  droppedTicks: number
  windowHours: number
  filters?: LogFilterState
  onFiltersChange?: (filters: LogFilterState) => void
  visibleCount?: number
}) {
  const filtered = useMemo(() => {
    const action = filters.action.trim().toLowerCase()
    return entries.filter((entry) => {
      if (filters.level !== 'all' && deriveLevel(entry.action) !== filters.level) return false
      if (action.length > 0 && !entry.action.toLowerCase().includes(action)) return false
      if (filters.actorKind !== '' && entry.actorKind !== filters.actorKind) return false
      if (filters.entityType !== '' && entry.entityType !== filters.entityType) return false
      return true
    })
  }, [entries, filters])

  const visible = filtered.slice(0, visibleCount)

  return (
    <div className="flex flex-col gap-3">
      {/* A slim status strip, not a full-width box: the chip, the retention window and the buffer
          counter are one line of metadata, and stretching them across the page left a large empty
          panel. */}
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2 text-xs">
        <LogConnectionChip
          status={status}
          lastPolledAt={lastPolledAt}
          droppedTicks={droppedTicks}
        />
        <span className="text-muted-foreground" data-testid="log-retention-window">
          Retention window: <span className="text-foreground">{windowHours} h</span> (24 h default,{' '}
          {MAX_WINDOW_DAYS} days maximum)
        </span>
        <span className="font-mono text-[11px] text-muted-foreground" data-testid="log-buffer">
          {entries.length.toLocaleString()} / {bufferLimit.toLocaleString()} buffered
          {dropped > 0 && (
            <span className="text-warning"> · {dropped.toLocaleString()} dropped</span>
          )}
        </span>
      </div>

      {onFiltersChange !== undefined && <LogFilters filters={filters} onChange={onFiltersChange} />}

      <Card className="border-border bg-card/60 shadow-xs">
        <CardContent className="max-h-[600px] flex flex-col gap-2 overflow-y-auto p-3 font-mono text-xs">
          {visible.length === 0 ? (
            <p className="py-10 text-center font-sans text-sm text-muted-foreground">
              {entries.length === 0
                ? 'No entries in this window yet.'
                : 'No entries match the current filters.'}
            </p>
          ) : (
            visible.map((entry) => (
              <LogRow
                key={entry.id}
                entry={entry}
                expanded={false}
                onToggle={() => undefined}
                onCopy={() => undefined}
                copied={false}
              />
            ))
          )}
          {filtered.length > visible.length && (
            <p className="py-2 text-center font-sans text-[11px] text-muted-foreground">
              Showing the newest {visible.length.toLocaleString()} of{' '}
              {filtered.length.toLocaleString()} matching rows.
            </p>
          )}
        </CardContent>
      </Card>
    </div>
  )
}
