import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'

import type { BoutiqueIncomeEntry } from '@/lib/income-api'
import { cn } from '@/lib/utils'

import { Money } from '../Money'

interface IncomeLedgerTableProps {
  items: BoutiqueIncomeEntry[]
}

/**
 * The register.
 *
 * **`Derived` and `Verified` rows are distinguishable in text, not only in colour.** A colour-only
 * distinction is invisible to a colour-blind reader and is lost in a printed or greyscale view, and
 * the one thing this table must never do is let a reader mistake billed value for money taken.
 * The sign is likewise rendered as a word (`+`/`−` beside a basis label), not only as styling.
 */
export function IncomeLedgerTable({ items }: IncomeLedgerTableProps) {
  return (
    <Card>
      <CardContent className="pt-6">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>When</TableHead>
              <TableHead>Kind</TableHead>
              <TableHead>Basis</TableHead>
              <TableHead>Reason</TableHead>
              <TableHead className="text-right">Amount</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {items.map((entry) => {
              const isDerived = entry.chargeBasis === 'Derived'
              const isRefund = entry.kind === 'Refund'
              return (
                <TableRow key={entry.id} className={cn(entry.status === 'Voided' && 'opacity-60')}>
                  <TableCell className="whitespace-nowrap text-xs">
                    {new Date(entry.occurredAt).toLocaleString()}
                  </TableCell>
                  <TableCell className="text-xs capitalize">
                    {entry.kind.replace(/([A-Z])/g, ' $1').trim()}
                  </TableCell>
                  <TableCell className="text-xs">
                    <Badge variant={isDerived ? 'outline' : 'secondary'}>
                      {isDerived ? 'Billed, unconfirmed' : 'Money taken'}
                    </Badge>
                    {entry.status === 'Voided' ? (
                      <span className="ml-1.5 text-muted-foreground">(superseded)</span>
                    ) : null}
                  </TableCell>
                  <TableCell className="max-w-md text-xs">{entry.reason}</TableCell>
                  <TableCell className="whitespace-nowrap text-right text-xs font-medium">
                    {isRefund ? '− ' : '+ '}
                    <Money value={entry.amount} currency={entry.currency} />
                  </TableCell>
                </TableRow>
              )
            })}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  )
}
