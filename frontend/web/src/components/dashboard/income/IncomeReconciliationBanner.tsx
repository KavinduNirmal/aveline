import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { AlertTriangle, CheckCircle2, HelpCircle } from 'lucide-react'

import type { BoutiqueIncomeReconciliation } from '@/lib/income-api'
import { formatMoney } from '@/lib/format-money'

interface IncomeReconciliationBannerProps {
  reconciliation: BoutiqueIncomeReconciliation
  currency: string
  windowCapped: boolean
  windowFrom: string
  windowTo: string
  notes: string[]
  onShowLedger: () => void
}

/**
 * The register's reconciliation banner.
 *
 * Its whole job is to keep three facts apart that a single "income" number would merge:
 * **money taken** (`Verified`), **billed but unconfirmed** (`Derived`) and **refunds**. The two
 * bases are rendered as two labelled figures, always both, and the gap between them is presented as
 * information rather than as an error.
 *
 * The unknown case is rendered as unknown. A banner that cannot reach the figures says
 * "reconciliation status unknown", never "consistent" — claiming a reconciliation nobody performed
 * is exactly the class of lie this surface exists to prevent.
 */
export function IncomeReconciliationBanner({
  reconciliation,
  currency,
  windowCapped,
  windowFrom,
  windowTo,
  notes,
  onShowLedger,
}: IncomeReconciliationBannerProps) {
  const verified = reconciliation.verifiedTotal
  const derived = reconciliation.derivedTotal
  const refunds = reconciliation.refundTotal
  const gap = reconciliation.unverifiedGap

  const measured = verified !== null || derived !== null

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <CardTitle className="font-serif text-lg font-medium">Reconciliation</CardTitle>
            <CardDescription>
              {new Date(windowFrom).toLocaleDateString()} –{' '}
              {new Date(windowTo).toLocaleDateString()}
              {windowCapped ? ' (capped to the server limit)' : ''}
            </CardDescription>
          </div>
          <Badge variant={measured && reconciliation.isReconciled ? 'secondary' : 'outline'} className="gap-1">
            {!measured ? (
              <>
                <HelpCircle className="size-3" aria-hidden /> Reconciliation status unknown
              </>
            ) : reconciliation.isReconciled ? (
              <>
                <CheckCircle2 className="size-3" aria-hidden /> Every sale is confirmed
              </>
            ) : (
              <>
                <AlertTriangle className="size-3" aria-hidden /> Billed value awaiting confirmation
              </>
            )}
          </Badge>
        </div>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {!measured ? (
          <p className="text-sm text-muted-foreground">
            No figures were returned for this window, so nothing is claimed about the reconciliation.
          </p>
        ) : (
          <>
            <dl className="grid gap-4 sm:grid-cols-3">
              <div>
                <dt className="text-xs uppercase tracking-wide text-muted-foreground">Collected</dt>
                <dd className="font-serif text-2xl font-medium">
                  {formatMoney(verified, currency)}
                </dd>
                <p className="mt-1 text-xs text-muted-foreground">
                  Money a person or the payment flow confirmed was taken.
                </p>
              </div>
              <div>
                <dt className="text-xs uppercase tracking-wide text-muted-foreground">
                  Billed, unconfirmed
                </dt>
                <dd className="font-serif text-2xl font-medium">
                  {formatMoney(derived, currency)}
                </dd>
                <p className="mt-1 text-xs text-muted-foreground">
                  What the orders say was sold. Not evidence that money moved.
                </p>
              </div>
              <div>
                <dt className="text-xs uppercase tracking-wide text-muted-foreground">Refunded</dt>
                <dd className="font-serif text-2xl font-medium">
                  {formatMoney(refunds, currency)}
                </dd>
                <p className="mt-1 text-xs text-muted-foreground">
                  Already subtracted from Collected, and stated here so it is never invisible.
                </p>
              </div>
            </dl>

            {gap !== null && gap > 0 ? (
              <p className="text-xs text-muted-foreground">
                {formatMoney(gap, currency)} of the billed value has no confirmation yet. This is the
                gap between what the orders say and what the shop has evidence of collecting — it is
                a number to act on, not an error.
              </p>
            ) : null}
          </>
        )}

        {notes.length > 0 ? (
          <ul className="flex flex-col gap-1 text-xs text-muted-foreground">
            {notes.map((note) => (
              <li key={note}>{note}</li>
            ))}
          </ul>
        ) : null}

        <div>
          <Button type="button" variant="outline" size="sm" onClick={onShowLedger}>
            Open the register
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}
