import { useId } from 'react'

import { Area, AreaChart, ChartContainer, type ChartConfig } from '@/components/ui/chart'
import { usePrefersReducedMotion } from '@/hooks/usePrefersReducedMotion'
import { CONNECT_NULLS } from '@/lib/dashboard-chart-rules'
import { cn } from '@/lib/utils'

/** One bucket of a series drawn inside a KPI tile. */
export interface KpiSparklinePoint {
  /** The bucket's ISO start. The recharts category key. */
  bucketStart: string
  /** `null` means the server measured nothing in this bucket. A gap, never a zero. */
  value: number | null
}

interface KpiSparklineProps {
  points: KpiSparklinePoint[]
  /** The series name — used for the chart config label and the accessible summary. */
  label: string
  /** The chart colour token, e.g. `var(--chart-1)`. Passed in, never hardcoded. */
  colorToken: string
  /**
   * Size and placement of the glyph's box. **It defaults to a fixed `h-9 w-20`**, so the component is
   * safe to drop anywhere; a caller that wants the full width of a tile passes one of its own.
   * `height="100%"` on the `ResponsiveContainer` collapses the chart, whatever the classes say.
   */
  className?: string
}

/**
 * The most recent measurement the sparkline can draw a line from.
 *
 * With `connectNulls` false, recharts breaks the area at every gap, so a measured bucket whose
 * neighbours are both `null` becomes a run of one point — a **zero-length segment** (verified in a
 * browser: the path is `M5,22.789L5,31Z`, with no `L` to anywhere else, and the line's `stroke` is
 * `none` because the stroke is drawn by a second curve that also has no length). A sparse series
 * therefore paints an empty box, which a reader can only read as "nothing happened".
 *
 * So a glyph needs **two** measurements to be a trend at all. Below that, the honest state is the
 * stated one, and `KpiCard` renders nothing rather than an empty box beside a real figure.
 */
const MIN_MEASURED_BUCKETS = 2

/** Whether these points carry enough measurement for a trend line to mean anything. */
export function canDrawSparkline(points: KpiSparklinePoint[]): boolean {
  return points.filter((point) => point.value !== null).length >= MIN_MEASURED_BUCKETS
}

/**
 * A trend glyph for a KPI tile — the shape of the series, not a read-out of it.
 *
 * **The tile's number is the measurement; this is only its direction.** That is why it carries no
 * axes, no grid and no tooltip: a tooltip in a small box would cover the thing it annotates, and an
 * axis would invite a reader to take a value off a glyph that was never meant to carry one. The
 * `aria-label` states the series, its period and its direction instead, so the information is not
 * available only to sighted readers.
 *
 * Two honesty rules are enforced here, each visible in the code and pinned by a test:
 *
 * 1. **A `null` bucket is a gap.** `connectNulls` comes from the tenant tree's shared constant, so
 *    the line never runs through a measurement the server did not produce.
 * 2. **Too few measurements draws nothing.** An empty axis under a figure reads as "this was flat at
 *    zero", which is a claim nobody made; the stated empty state says exactly that instead, and it
 *    renders **outside `ChartContainer`** because `ResponsiveContainer` measures a `0×0` box and
 *    wraps prose one character per line. The threshold and its reason are `canDrawSparkline`'s.
 *
 * Two more are the caller's: `KpiCard` draws this only beside a **measured** figure whose series
 * clears the threshold, and the tile states it when `windowCapped` says the series covers less than
 * the figure does.
 *
 * The gradient id is generated per instance with `useId`, so two sparklines on one page cannot
 * share a fill — the failure mode a data-key-derived id produces the moment both cards plot the
 * same key. `initialDimension` is supplied rather than left to measurement, so the glyph has a real
 * size on the very first paint instead of appearing a frame later.
 */
export function KpiSparkline({ points, label, colorToken, className }: KpiSparklineProps) {
  const reduceMotion = usePrefersReducedMotion()
  const gradientId = `${useId().replace(/:/g, '')}-fill`

  const data = points.map((point) => ({ bucketStart: point.bucketStart, value: point.value }))
  // `0` is a real measurement, so the test is `=== null`, never falsiness. The type predicate keeps
  // the narrowed shape: a `number | null` passed on as a `number` is how a gap becomes a zero.
  const measured = data.filter(
    (point): point is { bucketStart: string; value: number } => point.value !== null,
  )
  if (measured.length < MIN_MEASURED_BUCKETS) {
    return (
      <p className="text-xs italic text-muted-foreground">
        {label}: not measured in this window.
      </p>
    )
  }

  const config: ChartConfig = { value: { label, color: colorToken } }

  return (
    <div className={cn('h-9 w-20 shrink-0', className)} role="img" aria-label={accessibleSummary(label, measured)}>
      <ChartContainer
        config={config}
        className="h-full w-full"
        initialDimension={{ width: 120, height: 36 }}
      >
        <AreaChart data={data}>
          <defs>
            <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%" stopColor={colorToken} stopOpacity={0.35} />
              <stop offset="95%" stopColor={colorToken} stopOpacity={0.02} />
            </linearGradient>
          </defs>
          <Area
            type="monotone"
            dataKey="value"
            stroke={colorToken}
            strokeWidth={2}
            fill={`url(#${gradientId})`}
            dot={false}
            connectNulls={CONNECT_NULLS}
            isAnimationActive={!reduceMotion}
          />
        </AreaChart>
      </ChartContainer>
    </div>
  )
}

/**
 * The trend in words: the series, then the period it covers and which way it went.
 *
 * A screen-reader user is told what a sighted reader reads off the glyph, and nothing more — the
 * direction is described rather than quantified, because the glyph does not carry a figure either.
 */
function accessibleSummary(
  label: string,
  measured: ReadonlyArray<{ bucketStart: string; value: number }>,
): string {
  const first = measured[0] as { bucketStart: string; value: number }
  const last = measured[measured.length - 1] as { bucketStart: string; value: number }

  const direction =
    last.value === first.value
      ? 'flat over the period'
      : last.value > first.value
        ? 'rising over the period'
        : 'falling over the period'

  return `${label} trend: ${direction}, from ${formatBucket(first.bucketStart)} to ${formatBucket(last.bucketStart)}.`
}

/**
 * A bucket start as a short date.
 *
 * The guard is not decoration: `Intl.DateTimeFormat.format` **throws** on a non-finite date rather
 * than returning a placeholder, so an unparseable bucket would take the whole tile down. The raw
 * string is shown instead — the label says so rather than claiming a date.
 */
function formatBucket(bucketStart: string): string {
  const at = new Date(bucketStart).getTime()
  if (Number.isNaN(at)) return bucketStart
  return new Intl.DateTimeFormat('en', { day: 'numeric', month: 'short' }).format(at)
}
