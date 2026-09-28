import { Card, CardContent } from '@/components/ui/card'

import type { LogConnectionStatus } from '@/lib/admin/log-stream'

const VIEW: Record<LogConnectionStatus, { label: string; tone: string; description: string }> = {
  idle: {
    label: 'Idle',
    tone: 'text-muted-foreground',
    description: 'no poll has completed yet',
  },
  polling: {
    label: 'Polling',
    tone: 'text-primary',
    description: 'a request is in flight',
  },
  live: {
    label: 'Live',
    tone: 'text-success',
    description: 'receiving new rows',
  },
  paused: {
    label: 'Paused',
    tone: 'text-warning',
    description: 'no requests are issued while paused',
  },
  hidden: {
    label: 'Disconnected',
    tone: 'text-muted-foreground',
    description: 'this tab is hidden; polling is suspended, not queued',
  },
  error: {
    label: 'Disconnected',
    tone: 'text-destructive',
    description: 'the last polls failed; the feed is not live',
  },
}

/**
 * The connection chip, and the feed's **only** status surface.
 *
 * `role="status"` gives one short, controlled announcement when the connection changes. The
 * feed itself is never a live region: an `aria-live` log reads every arriving row aloud,
 * forever. The delivered console's green "Live Connection" dot reflected nothing at all.
 */
export function LogConnectionChip({
  status,
  lastPolledAt,
  droppedTicks = 0,
}: {
  status: LogConnectionStatus
  lastPolledAt: string | null
  droppedTicks?: number
}) {
  const current = VIEW[status]

  return (
    <Card className="border-border inline-flex shadow-xs" data-status={status}>
      <CardContent
        role="status"
        className="flex flex-wrap items-center gap-2 px-3 py-1.5 text-xs text-muted-foreground"
      >
        <span
          className={`inline-flex items-center gap-1.5 font-medium ${current.tone}`}
          aria-hidden="false"
        >
          <span className="size-2 rounded-full bg-current" aria-hidden="true" />
          {current.label}
        </span>
        <span>{current.description}</span>
        {lastPolledAt !== null && (
          <span className="font-mono text-[10px]">
            last poll {new Date(lastPolledAt).toLocaleTimeString()}
          </span>
        )}
        {droppedTicks > 0 && (
          <span className="font-mono text-[10px] text-warning">
            {droppedTicks} tick{droppedTicks === 1 ? '' : 's'} dropped
          </span>
        )}
      </CardContent>
    </Card>
  )
}
