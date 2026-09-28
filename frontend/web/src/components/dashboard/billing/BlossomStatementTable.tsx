import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { formatCount } from '@/lib/format-money'
import type { BlossomStatement } from '@/lib/billing-api'

interface BlossomStatementTableProps {
  statement: BlossomStatement
  onPage: (page: number) => void
}

/**
 * The Blossom statement of account.
 *
 * **A statement, never an invoice.** Nothing here is a demand for payment: no provider is
 * connected and a top-up is a recorded grant, not a charge (D8).
 *
 * The reconciliation banner has three states and never collapses them: "consistent" only when the
 * server actually checked, "status unknown" when `reconciliationChecked` is false, and the drift
 * otherwise. A failed check rendering as "consistent" would hide exactly the condition the field
 * exists to expose.
 */
export function BlossomStatementTable({ statement, onPage }: BlossomStatementTableProps) {
  const checked = statement.dataQuality?.reconciliationChecked ?? false
  const lastPage = Math.max(1, Math.ceil(statement.total / statement.pageSize))

  return (
    <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
      <CardHeader>
        <CardTitle className="font-serif text-lg font-medium">Blossom statement</CardTitle>
        <CardDescription>
          A statement of account. No payment provider is connected, so these rows record grants and
          consumption rather than charges.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <div className="flex flex-wrap items-center gap-2 text-sm">
          {!checked ? (
            <Badge variant="outline" className="text-muted-foreground">
              reconciliation status unknown
            </Badge>
          ) : statement.reconciliation.isConsistent ? (
            <Badge variant="outline">reconciled</Badge>
          ) : (
            <Badge variant="outline" className="text-destructive">
              drift {formatCount(statement.reconciliation.drift)}
            </Badge>
          )}
          <span className="text-muted-foreground">
            Opening {formatCount(statement.openingBalance)} · closing{' '}
            {formatCount(statement.closingBalance)}
          </span>
          {statement.dataQuality?.windowCapped ? (
            <span className="text-xs text-muted-foreground">
              window capped at {formatCount(statement.dataQuality.maxWindowDays)} days
            </span>
          ) : null}
        </div>

        {statement.items.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            No ledger entries in this window.
          </p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>When</TableHead>
                <TableHead>Entry</TableHead>
                <TableHead>Delta</TableHead>
                <TableHead>Balance after</TableHead>
                <TableHead>Expires</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {statement.items.map((item) => (
                <TableRow key={item.id}>
                  <TableCell className="text-muted-foreground">
                    {new Date(item.occurredAt).toLocaleDateString([], {
                      day: 'numeric',
                      month: 'short',
                    })}
                  </TableCell>
                  <TableCell className="font-medium">
                    {item.entryType ?? item.kind}
                  </TableCell>
                  <TableCell>{formatCount(item.blossomDelta)}</TableCell>
                  <TableCell>{formatCount(item.balanceAfter)}</TableCell>
                  {/* A grant may expire; consumption does not. This column used to render the ledger
                      `reason`, whose consumption text named the provider and model behind the charge —
                      agent internals a boutique does not read. */}
                  <TableCell className="text-muted-foreground">
                    {item.expiresAt
                      ? new Date(item.expiresAt).toLocaleDateString([], {
                          day: 'numeric',
                          month: 'short',
                          year: 'numeric',
                        })
                      : '—'}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}

        {statement.total > statement.pageSize ? (
          <div className="flex items-center justify-between">
            <p className="text-xs text-muted-foreground">
              Page {statement.page} of {lastPage} · {formatCount(statement.total)} entries
            </p>
            <div className="flex gap-2">
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={statement.page <= 1}
                onClick={() => onPage(statement.page - 1)}
              >
                Previous
              </Button>
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={statement.page >= lastPage}
                onClick={() => onPage(statement.page + 1)}
              >
                Next
              </Button>
            </div>
          </div>
        ) : null}
      </CardContent>
    </Card>
  )
}
