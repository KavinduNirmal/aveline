import { Blossom } from '@/components/auth/Blossom'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { formatCount, formatPercent } from '@/lib/format-money'
import type { BlossomBalance } from '@/lib/billing-api'

interface UsageBalanceCardProps {
  balance: BlossomBalance
}

/** "9 Sep 2026" from an ISO timestamp, or the raw value when it cannot be parsed. */
function formatDate(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return iso
  return date.toLocaleDateString([], { day: 'numeric', month: 'short', year: 'numeric' })
}

/**
 * The current period's Blossom position.
 *
 * Every figure here is a non-null number from the server, so nothing is defaulted. The percentage
 * comes from the server's own `percentUsed` rather than being recomputed locally, so the panel and
 * the top-bar chip cannot disagree about the same period.
 */
export function UsageBalanceCard({ balance }: UsageBalanceCardProps) {
  const percent = Math.min(100, Math.max(0, balance.percentUsed))

  return (
    <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
      <CardHeader>
        <div className="flex items-center justify-between gap-4">
          <CardTitle className="font-serif text-lg font-medium">Blossom balance</CardTitle>
          {balance.planTier ? (
            <Badge variant="outline" className="gap-1">
              <Blossom className="size-3 text-primary" /> {balance.planTier}
            </Badge>
          ) : null}
        </div>
        <CardDescription>
          {formatDate(balance.periodStart)} – {formatDate(balance.periodEnd)}
          {balance.periodIsClosed ? ' · closed' : ''}
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-6">
        <div className="flex items-baseline gap-2">
          <span className="font-serif text-5xl font-medium">
            {formatCount(balance.blossomRemaining)}
          </span>
          <span className="text-sm text-muted-foreground">
            of {formatCount(balance.monthlyBlossomLimit)} Blossoms remaining
          </span>
        </div>

        <div className="flex flex-col gap-1.5">
          <div className="flex items-center justify-between text-xs text-muted-foreground">
            <span>{formatCount(balance.blossomUsed)} used</span>
            <span>{formatPercent(balance.percentUsed)}</span>
          </div>
          <div className="h-2.5 w-full overflow-hidden rounded-full bg-muted">
            <div
              className="h-full rounded-full bg-gradient-to-r from-primary to-primary/70"
              style={{ width: `${percent}%` }}
            />
          </div>
        </div>

        <dl className="grid grid-cols-2 gap-4 text-sm sm:grid-cols-4">
          <div>
            <dt className="text-xs uppercase tracking-wide text-muted-foreground">Allowance</dt>
            <dd className="font-medium">{formatCount(balance.monthlyBlossomLimit)}</dd>
          </div>
          <div>
            <dt className="text-xs uppercase tracking-wide text-muted-foreground">Granted</dt>
            <dd className="font-medium">{formatCount(balance.blossomGranted)}</dd>
          </div>
          <div>
            <dt className="text-xs uppercase tracking-wide text-muted-foreground">Adjusted</dt>
            <dd className="font-medium">{formatCount(balance.blossomAdjusted)}</dd>
          </div>
          <div>
            <dt className="text-xs uppercase tracking-wide text-muted-foreground">Used</dt>
            <dd className="font-medium">{formatCount(balance.blossomUsed)}</dd>
          </div>
        </dl>
      </CardContent>
    </Card>
  )
}
