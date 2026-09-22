import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { formatCount, formatMoney, formatPercent } from '@/lib/format-money'
import { cn } from '@/lib/utils'

export type KpiFormat = 'money' | 'count' | 'percent'

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
 */
export function KpiCard({
  label,
  value,
  format = 'money',
  currency = 'LKR',
  description,
  note,
  caveat,
  className,
}: KpiCardProps) {
  const measured = value !== null && value !== undefined && !Number.isNaN(value)

  const rendered = !measured
    ? 'not measured'
    : format === 'money'
      ? formatMoney(value, currency)
      : format === 'percent'
        ? formatPercent(value)
        : formatCount(value)

  return (
    <Card className={cn('shadow-[0_4px_20px_rgba(122,48,63,0.06)]', className)}>
      <CardHeader>
        <div className="flex items-start justify-between gap-3">
          <CardTitle className="font-serif text-base font-medium">{label}</CardTitle>
          {caveat ? (
            <Badge variant="outline" className="shrink-0 text-destructive">
              {caveat}
            </Badge>
          ) : null}
        </div>
        {description ? <CardDescription>{description}</CardDescription> : null}
      </CardHeader>
      <CardContent>
        <p
          className={cn(
            'font-serif text-3xl font-medium',
            // A missing measurement is visually distinct from a small one, so it cannot be mistaken
            // for a figure at a glance.
            !measured && 'font-sans text-base italic text-muted-foreground',
          )}
        >
          {rendered}
        </p>
        {note ? <p className="mt-2 text-xs text-muted-foreground">{note}</p> : null}
      </CardContent>
    </Card>
  )
}
