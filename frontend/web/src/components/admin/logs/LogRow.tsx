import { Badge } from '@/components/ui/badge'
import { cn } from 'cn'

import { deriveLevel, deriveSource, levelLabel, levelTone } from '@/lib/admin/log-level'
import type { LogEntry } from '@/lib/admin/log-stream'

const TONE_CLASS: Record<string, string> = {
  destructive: 'text-destructive',
  warning: 'text-warning',
  primary: 'text-primary',
  muted: 'text-muted-foreground',
}

/**
 * One feed row.
 *
 * Severity is **derived by the console** and labelled as such (Q4); the wire carries no level.
 * The label's colour is a semantic token, never a palette class, so it inverts in dark mode.
 */
export function LogRow({
  entry,
  expanded,
  onToggle,
  onCopy,
  copied,
}: {
  entry: LogEntry
  expanded: boolean
  onToggle: (id: string) => void
  onCopy: (entry: LogEntry) => void
  copied: boolean
}) {
  const level = deriveLevel(entry.action)
  const source = deriveSource(entry)

  return (
    <div
      data-entry-id={entry.id}
      className="rounded-lg border border-border/80 bg-background/90 p-2.5 transition-colors hover:bg-muted/30"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex min-w-0 flex-wrap items-center gap-2">
          <span className="font-mono text-[11px] text-muted-foreground">
            {new Date(entry.occurredAt).toLocaleTimeString()}
          </span>
          <Badge
            variant="outline"
            title="Derived by the console from the action name; the server sends no level."
            className={cn('h-5 gap-1 text-[10px] uppercase tracking-wider', TONE_CLASS[levelTone(level)])}
          >
            {levelLabel(level)}
            <span className="text-[9px] normal-case tracking-normal">(derived)</span>
          </Badge>
          <span className="font-mono text-xs font-semibold text-foreground">{entry.action}</span>
          <span className="font-mono text-[11px] text-muted-foreground">
            {entry.entityType}:{entry.entityId}
          </span>
        </div>

        <div className="flex items-center gap-2">
          <span
            className={cn('inline-flex items-center gap-1.5 text-[11px]', TONE_CLASS[source.tone])}
          >
            {source.label}
            {source.detail !== null && (
              <span className="font-mono text-[10px] text-muted-foreground">{source.detail}</span>
            )}
          </span>
          {/*
            conformance-allow: raw-button — the log viewer's virtualised row. These two inline
            controls are per-row and inside the virtualisation window; a shadcn `Button` in every
            row would add a primitive instance and its focus machinery to a list that re-renders on
            a 1.5 s cadence. This is the allow-list exception named in strategy C6.
          */}
          <button
            type="button"
            className="rounded px-1.5 py-0.5 text-[11px] text-muted-foreground hover:bg-muted/40"
            onClick={() => onToggle(entry.id)}
            aria-expanded={expanded}
          >
            {expanded ? 'Hide diff' : 'Diff'}
          </button>
          <button
            type="button"
            className="rounded px-1.5 py-0.5 text-[11px] text-muted-foreground hover:bg-muted/40"
            onClick={() => onCopy(entry)}
            title="Copy entry id"
          >
            {copied ? 'Copied' : 'Copy id'}
          </button>
        </div>
      </div>

      {entry.reason !== null && entry.reason.length > 0 && (
        <p className="mt-1 border-l-2 border-primary/40 pl-2 text-[11px] italic text-muted-foreground">
          Reason: {entry.reason}
        </p>
      )}

      {expanded && (
        <div className="mt-2.5 grid grid-cols-2 gap-2 border-t border-border pt-2 text-[10px]">
          <div className="rounded border border-border bg-muted/20 p-2">
            <div className="mb-1 font-bold text-muted-foreground">BEFORE</div>
            <pre className="overflow-x-auto whitespace-pre-wrap text-muted-foreground">
              {entry.before === null ? '(none / redacted)' : JSON.stringify(entry.before, null, 2)}
            </pre>
          </div>
          <div className="rounded border border-border bg-muted/20 p-2">
            <div className="mb-1 font-bold text-muted-foreground">AFTER</div>
            <pre className="overflow-x-auto whitespace-pre-wrap text-foreground">
              {entry.after === null ? '(none / redacted)' : JSON.stringify(entry.after, null, 2)}
            </pre>
          </div>
        </div>
      )}
    </div>
  )
}
