import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { KpiCard } from '@/components/dashboard/kpi/KpiCard'
import { formatCount } from '@/lib/format-money'
import type { BurnRate } from '@/lib/billing-api'

interface BurnRatePanelProps {
  burnRate: BurnRate
}

/**
 * Burn rate and projected exhaustion.
 *
 * The projection is deliberately the server's own timestamp and is **not** recomputed from the
 * balance, so the tile and the balance card cannot disagree. A `null` projection means the server
 * could not project one, which is rendered as "not projected" rather than "never" — the product
 * does not know that the balance will not run out.
 */
export function BurnRatePanel({ burnRate }: BurnRatePanelProps) {
  const projected = burnRate.projectedExhaustionAt
    ? new Date(burnRate.projectedExhaustionAt).toLocaleDateString([], {
        day: 'numeric',
        month: 'short',
        year: 'numeric',
      })
    : null

  return (
    <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
      <CardHeader>
        <CardTitle className="font-serif text-lg font-medium">Burn rate</CardTitle>
        <CardDescription>
          How fast this period is consuming its allowance, and when it would run out at that rate.
        </CardDescription>
      </CardHeader>
      <CardContent className="grid gap-4 sm:grid-cols-3">
        <KpiCard
          label="Per day"
          value={burnRate.burnRatePerDay}
          format="count"
          note="Blossoms consumed per day over the window."
        />
        <KpiCard
          label="Average daily"
          value={burnRate.averageDailyUsage}
          format="count"
          note="Mean daily consumption across the window."
        />
        <div>
          <p className="text-xs uppercase tracking-wide text-muted-foreground">
            Projected exhaustion
          </p>
          <p className="mt-1 font-serif text-lg font-medium">
            {projected ?? <span className="italic text-muted-foreground">not projected</span>}
          </p>
          <p className="mt-1 text-xs text-muted-foreground">
            Balance at read time: {formatCount(burnRate.currentBalance)} Blossoms.
          </p>
        </div>
      </CardContent>
    </Card>
  )
}
