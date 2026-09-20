import { Badge } from '@/components/ui/badge'

/**
 * A period-over-period delta, extracted from the inline badge the dashboard hand-rolled.
 *
 * `null` renders **nothing**, not `0%`: "there is no previous window to compare against" and
 * "nothing changed" are different statements, and only the second is `0%`.
 */
export function DeltaBadge({ delta }: { delta: number | null | undefined }) {
  if (delta === null || delta === undefined || Number.isNaN(delta)) return null

  const rounded = Math.round(delta * 10) / 10
  const sign = rounded > 0 ? '+' : ''

  return (
    <Badge variant="secondary" className="font-mono text-[10px]">
      {`${sign}${rounded}%`}
    </Badge>
  )
}
