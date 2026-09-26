import type { ComponentType } from 'react'

import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { accentStyle, FIGURE_ACCENT_CLASS } from '@/lib/dashboard-chart-rules'
import { formatCount, formatMoney, formatPercent } from '@/lib/format-money'
import { cn } from '@/lib/utils'

import { KpiSparkline, canDrawSparkline, type KpiSparklinePoint } from './KpiSparkline'

export type KpiFormat = 'money' | 'count' | 'percent'

/**
 * The soft tint at the top of a card, in the metric's accent at a few percent.
 *
 * It is defined once rather than repeated per card so that "how strong is the brand here" is a single
 * decision. It reads the `--kpi-accent` custom property, so the caller sets one variable and the wash
 * and the icon tint cannot disagree. Purely decorative, so it is hidden from assistive tech.
 */
export function AccentWash() {
  return (
    <span
      aria-hidden
      className="pointer-events-none absolute inset-x-0 top-0 h-24 bg-gradient-to-b from-(--kpi-accent)/12 to-transparent"
    />
  )
}

interface KpiCardProps {
  label: string
  /** `null` means the server did not measure this. It is never rendered as `0`. */
  value: number | null | undefined
  format?: KpiFormat
  currency?: string
  description?: string
  /** Shown when the figure is present and the reader needs to know what it is (or is not). */
  note?: string
  /** A caveat that makes the figure less trustworthy, rendered as a warning badge. */
  caveat?: string
  /**
   * The series behind this figure, drawn as a trend glyph beneath it. Only ever read on the measured
   * branch: a trend line beside "not measured" would imply a measurement the tile just said it does
   * not have.
   */
  sparkline?: KpiSparklinePoint[]
  /** What a reader must know about the sparkline's period that the glyph itself cannot say. */
  sparklineCaption?: string
  /** The metric's mark, the way a reader finds a tile at a glance rather than by re-reading labels. */
  icon?: ComponentType<{ className?: string; 'aria-hidden'?: boolean }>
  /**
   * The tile's colour **token** — `var(--chart-1)`, `var(--primary)` and so on. Passed in from the
   * section that owns the metric, never hardcoded here, and never a literal colour: the tenant
   * conformance gate fails the build on a bare hex. It tints the icon, the wash and the sparkline.
   */
  accent?: string
  className?: string
}

/**
 * One KPI tile.
 *
 * **The whole reason this component exists is the `null` case.** A dashboard that renders `LKR 0`
 * for a figure the server could not compute is stating a fact about the shop that nobody
 * established — the owner reads "we took nothing" where the truth is "nothing measured this". So a
 * null value renders the words **"not measured"** and the note says why, in the same place the number
 * would have been, rather than a zero that looks like data.
 *
 * The value is set in `font-mono tabular-nums` so a column of tiles lines up and does not wobble as
 * it updates. The **"not measured" branch keeps its own type** (`italic font-sans`): if the two
 * states read alike, the distinction this component exists to make is lost.
 *
 * **The trend sits *under* the value, not beside it.** Side by side looked better on a wider tile and
 * did not survive the arithmetic: at `lg` the strip is four columns, so a tile's text column is about
 * 220px, while `LKR 162,550.00` at `text-3xl` in a monospace face is already wider than that. The
 * glyph was squeezed to zero and clipped, which is how it shipped looking absent. Stacking gives the
 * value the full width and the glyph a readable one; the row only returns above `sm`.
 *
 * The colour reaches the card as a token through `accent`, so this component knows no palette. That
 * is what keeps it usable from any section without a second definition of the brand.
 */
export function KpiCard({
  label,
  value,
  format = 'money',
  currency = 'LKR',
  description,
  note,
  caveat,
  sparkline,
  sparklineCaption,
  icon: Icon,
  accent,
  className,
}: KpiCardProps) {
  const measured = value !== null && value !== undefined && !Number.isNaN(value)
  // A series too sparse to draw a line is no series as far as this tile is concerned: showing an
  // empty box, or captions about a glyph that is not there, would describe a picture the reader
  // cannot see. The threshold and its reason live in `KpiSparkline`.
  const drawTrend = measured && sparkline !== undefined && canDrawSparkline(sparkline)

  const rendered = !measured
    ? 'not measured'
    : format === 'money'
      ? formatMoney(value, currency)
      : format === 'percent'
        ? formatPercent(value)
        : formatCount(value)

  return (
    <Card
      className={cn('relative overflow-hidden shadow-[0_4px_20px_rgba(122,48,63,0.06)]', className)}
      style={accentStyle(accent)}
    >
      {/* A wash, not a poster: the accent at a few percent so the tile reads as belonging to a
          family of metrics without competing with the figure. Absent when no accent is given. */}
      {accent ? <AccentWash /> : null}
      <CardHeader className="relative">
        <div className="flex items-start justify-between gap-3">
          <div className="flex min-w-0 items-center gap-2">
            {Icon && accent ? (
              <span className="flex size-7 shrink-0 items-center justify-center rounded-md bg-(--kpi-accent)/12 text-(--kpi-accent)">
                <Icon className="size-4" aria-hidden />
              </span>
            ) : null}
            <CardTitle className="font-serif text-base font-medium">{label}</CardTitle>
          </div>
          {caveat ? (
            <Badge variant="outline" className="shrink-0 text-destructive">
              {caveat}
            </Badge>
          ) : null}
        </div>
        {description ? <CardDescription>{description}</CardDescription> : null}
      </CardHeader>
      <CardContent className="relative flex flex-col gap-3">
        <div>
          <p
            className={cn(
              // `text-2xl`, not `text-3xl`: at the `lg` four-column grid a tile's text column is
              // about 207px, and `LKR 162,550.00` at `text-3xl` in a monospace face is wider than
              // that, so the value was clipped mid-decimals in a real browser. The figure stays the
              // largest thing in the tile; it just cannot be larger than the tile.
              'font-mono text-2xl font-medium tabular-nums',
              // The figure carries the theme's accent rather than the near-black a paragraph
              // inherits: these are the customer's own numbers and the reason the page exists.
              FIGURE_ACCENT_CLASS,
              // A missing measurement is visually distinct from a small one, so it cannot be mistaken
              // for a figure at a glance — and it stays muted, because there is no figure to accent.
              !measured && 'font-sans text-base italic text-muted-foreground',
            )}
          >
            {rendered}
          </p>
          {note ? <p className="mt-2 text-xs text-muted-foreground">{note}</p> : null}
        </div>
        {/* The glyph renders only beside a real figure whose series can carry a line. */}
        {drawTrend && sparkline ? (
          <KpiSparkline
            points={sparkline}
            label={label}
            colorToken={accent ?? 'var(--chart-1)'}
            className="h-10 w-full"
          />
        ) : null}
        {sparklineCaption && drawTrend ? (
          <p className="text-xs text-muted-foreground">{sparklineCaption}</p>
        ) : null}
      </CardContent>
    </Card>
  )
}
