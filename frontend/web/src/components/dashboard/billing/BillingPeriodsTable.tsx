import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { formatCount, formatMoney } from '@/lib/format-money'
import { planListPrice, type BillingPeriod } from '@/lib/billing-api'

interface BillingPeriodsTableProps {
  periods: BillingPeriod[]
  currency?: string
}

function formatPeriod(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return iso
  return date.toLocaleDateString([], { month: 'short', year: 'numeric' })
}

/**
 * E-12. The billing-period history: what each period granted, adjusted, used and left, and the
 * top-ups that landed in it.
 *
 * **This is a statement of account, not an invoice.** There is no invoice entity, no payment
 * provider client and no currency column in the repository, so no row here is a demand for payment.
 * The list price is `null` for every period whose stored price is zero, which today is all of them
 * (C-4); it renders "not configured" rather than "LKR 0".
 */
export function BillingPeriodsTable({ periods, currency = 'LKR' }: BillingPeriodsTableProps) {
  return (
    <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
      <CardHeader>
        <CardTitle className="font-serif text-lg font-medium">Billing periods</CardTitle>
        <CardDescription>
          The most recent periods, newest first. Each column is reported separately, so an adjustment
          is never folded invisibly into a grant.
        </CardDescription>
      </CardHeader>
      <CardContent>
        {periods.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            No billing periods are recorded yet.
          </p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Period</TableHead>
                <TableHead>Plan</TableHead>
                <TableHead>Limit</TableHead>
                <TableHead>Granted</TableHead>
                <TableHead>Adjusted</TableHead>
                <TableHead>Used</TableHead>
                <TableHead>Remaining</TableHead>
                <TableHead>Top-ups</TableHead>
                <TableHead>List price</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {periods.map((period) => {
                const price = planListPrice(period)
                return (
                  <TableRow key={period.periodStart}>
                    <TableCell className="font-medium">
                      {formatPeriod(period.periodStart)}
                      {period.isClosed ? null : (
                        <Badge variant="outline" className="ml-2 text-xs">
                          open
                        </Badge>
                      )}
                    </TableCell>
                    <TableCell>
                      {period.hasSubscriptionRow ? (period.planTier ?? '—') : (
                        <span className="text-xs text-muted-foreground">no subscription row</span>
                      )}
                    </TableCell>
                    <TableCell>{formatCount(period.monthlyBlossomLimit)}</TableCell>
                    <TableCell>{formatCount(period.blossomGranted)}</TableCell>
                    <TableCell>{formatCount(period.blossomAdjusted)}</TableCell>
                    <TableCell>{formatCount(period.blossomUsed)}</TableCell>
                    <TableCell>{formatCount(period.blossomRemaining)}</TableCell>
                    <TableCell>
                      {period.topUpCount === 0
                        ? '—'
                        : `${formatCount(period.topUpBlossoms)} (${period.topUpCount})`}
                    </TableCell>
                    <TableCell>
                      {price === null ? (
                        <span className="text-xs italic text-muted-foreground">
                          not configured
                        </span>
                      ) : (
                        formatMoney(price, currency)
                      )}
                    </TableCell>
                  </TableRow>
                )
              })}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  )
}
