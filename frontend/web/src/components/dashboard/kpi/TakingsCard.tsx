import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Coins, Info } from 'lucide-react'

import type { TenantTakings } from '@/lib/dashboard-api'
import { formatMoney } from '@/lib/format-money'

interface TakingsCardProps {
  takings: TenantTakings
}

/**
 * The **reduced takings view** (E-13) — what every boutique role sees on Overview.
 *
 * Two rules, and they are the whole design:
 *
 * 1. **The two figures are always both rendered, both labelled.** "Collected" is money a person or
 *    the payment flow confirmed was taken; "Billed, unconfirmed" is what the orders say was sold,
 *    with no evidence money moved. There is never one unlabelled earnings number, because adding
 *    them would report billed value as income.
 * 2. **It renders exactly what the server sent.** No margin, no series, no per-client split — the
 *    server's reduced read does not carry them, and inventing a client-side figure here would be the
 *    fabrication the reduction exists to prevent.
 */
export function TakingsCard({ takings }: TakingsCardProps) {
  const collected = takings.collected
  const billed = takings.billedUnconfirmed
  const measured = collected !== null || billed !== null

  return (
    <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
      <CardHeader>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div className="flex items-center gap-2">
            <Coins className="size-4 text-primary" aria-hidden />
            <CardTitle className="font-serif text-lg font-medium">Takings</CardTitle>
          </div>
          <Badge variant="outline">{takings.window}</Badge>
        </div>
        <CardDescription>
          What the boutique took from its clients, and what the orders say was sold. The two are
          never added together.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {!measured ? (
          <p className="text-sm text-muted-foreground">
            No takings were measured for this window, so nothing is claimed about them.
          </p>
        ) : (
          <dl className="grid gap-4 sm:grid-cols-2">
            <div>
              <dt className="text-xs uppercase tracking-wide text-muted-foreground">Collected</dt>
              <dd className="font-serif text-2xl font-medium">
                {formatMoney(collected, takings.currency)}
              </dd>
              <p className="mt-1 text-xs text-muted-foreground">
                Money confirmed as taken, net of refunds.
              </p>
            </div>
            <div>
              <dt className="text-xs uppercase tracking-wide text-muted-foreground">
                Billed, unconfirmed
              </dt>
              <dd className="font-serif text-2xl font-medium">
                {formatMoney(billed, takings.currency)}
              </dd>
              <p className="mt-1 text-xs text-muted-foreground">
                What the orders say was sold. Not evidence that money moved.
              </p>
            </div>
          </dl>
        )}

        {!takings.paymentRowsPresent ? (
          <p className="flex items-start gap-1.5 text-xs text-muted-foreground">
            <Info className="mt-0.5 size-3 shrink-0" aria-hidden />
            No payment rows exist for this boutique yet, so these figures come from counter sales
            only. This is not a measurement of zero.
          </p>
        ) : null}

        {takings.ledgerBackfilled ? (
          <p className="text-xs text-muted-foreground">
            The ledger contains repaired entries, so it begins at a date rather than covering the
            boutique&apos;s full history.
          </p>
        ) : null}
      </CardContent>
    </Card>
  )
}
