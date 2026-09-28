import type { BlossomStatementItem } from '@/types/admin'

/**
 * The balance across the statement window, as one bar per row.
 *
 * The table states each row's balance but not the *shape* of the movement, so a discontinuity is
 * something an operator has to notice by reading down a column of numbers. This draws it instead.
 *
 * It is a bar list rather than a chart library call on purpose: the points are the statement's own
 * page, not a time series, and a Recharts axis would imply a continuity the data does not have —
 * the rows are events, not samples. A `PeriodAllocation` row is marked separately for the same
 * reason: the plan allowance steps the balance once a month, and drawing that as ordinary movement
 * would present a cliff as a slope.
 */
const STEP_ENTRY_TYPES = new Set(['PeriodAllocation', 'PlanUpgradeProration'])

export function BalanceTimeline({ items }: { items: BlossomStatementItem[] }) {
  if (items.length === 0) {
    return (
      <p className="text-xs text-muted-foreground">
        No movement in this window, so there is no balance curve to draw.
      </p>
    )
  }

  // The rows arrive newest-first, so the curve reads left-to-right only when reversed.
  const ordered = [...items].reverse()
  const balances = ordered.map((entry) => entry.balanceAfter)
  const high = Math.max(...balances, 0)
  const low = Math.min(...balances, 0)
  const span = high - low || 1
  const hasStep = ordered.some(
    (entry) => entry.entryType !== null && STEP_ENTRY_TYPES.has(entry.entryType),
  )

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-baseline justify-between text-[11px] text-muted-foreground">
        <span>
          {/* The first and last balances are the statement's own figures, stated directly rather
              than left to be inferred from the drawing. The axis is anchored at zero so a bar's
              height reads as a proportion of the window's peak. */}
          {ordered[0].balanceAfter} → {ordered[ordered.length - 1].balanceAfter} across{" "}
          {ordered.length} entries
        </span>
        {hasStep && (
          <span data-testid="timeline-step" className="font-medium">
            Includes a period step
          </span>
        )}
      </div>

      <div
        className="flex h-16 items-end gap-0.5"
        role="img"
        aria-label={`Balance movement ending at ${ordered[ordered.length - 1].balanceAfter}`}
      >
        {ordered.map((entry) => {
          const height = Math.max(2, ((entry.balanceAfter - low) / span) * 100)
          const isStep = entry.entryType !== null && STEP_ENTRY_TYPES.has(entry.entryType)
          return (
            <span
              key={entry.id}
              data-testid="timeline-mark"
              title={`${entry.balanceAfter} after ${entry.reason}`}
              // A step is drawn in the chart's primary tone and ordinary movement muted, so the
              // two read differently without a legend.
              className={
                isStep
                  ? 'flex-1 rounded-t-sm bg-[var(--chart-1)]'
                  : 'flex-1 rounded-t-sm bg-muted-foreground/40'
              }
              style={{ height: `${height}%` }}
            />
          )
        })}
      </div>
    </div>
  )
}
