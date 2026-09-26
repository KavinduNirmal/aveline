import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import type { EntitlementItem } from '@/lib/billing-api'

interface EntitlementsTableProps {
  items: EntitlementItem[]
}

const LABELS: Record<string, string> = {
  'blossoms.monthly': 'Blossoms per month',
  'staff.max': 'Staff seats',
  'customers.active.max': 'Active clients',
  'api.requests.monthly': 'API requests per month',
  'api.requests.perMinute': 'API requests per minute',
  'whatsapp.monthly': 'WhatsApp messages per month',
  'stats.retentionDays': 'Statistics retention (days)',
}

/**
 * Renders an entitlement value. The value column is polymorphic on the server (a string tier name,
 * a number limit or a boolean flag), so an absent value reads "not measured" rather than being
 * coerced into a number or an empty cell.
 */
function formatValue(value: unknown): string {
  if (typeof value === 'number') return value.toLocaleString()
  if (typeof value === 'boolean') return value ? 'Yes' : 'No'
  if (typeof value === 'string' && value.length > 0) return value
  return 'not measured'
}

/**
 * The plan's resolved entitlement table. The `source` column is shown because a limit can come from
 * the plan default, an override or a negotiated contract, and a reader comparing two boutiques
 * needs to know which.
 */
export function EntitlementsTable({ items }: EntitlementsTableProps) {
  return (
    <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
      <CardHeader>
        <CardTitle className="font-serif text-lg font-medium">Plan entitlements</CardTitle>
        <CardDescription>
          The effective limits for this boutique, with the source each one resolved from.
        </CardDescription>
      </CardHeader>
      <CardContent>
        {items.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            No entitlements are recorded for this plan, so no limits are shown rather than assumed.
          </p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Entitlement</TableHead>
                <TableHead>Value</TableHead>
                <TableHead>Source</TableHead>
                <TableHead>Effective from</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {items.map((item) => (
                <TableRow key={item.key}>
                  <TableCell className="font-medium">
                    {LABELS[item.key] ?? item.key}
                    <span className="ml-2 text-xs text-muted-foreground">{item.key}</span>
                  </TableCell>
                  <TableCell>{formatValue(item.value)}</TableCell>
                  <TableCell className="text-muted-foreground">{item.source}</TableCell>
                  <TableCell className="text-muted-foreground">
                    {new Date(item.effectiveFrom).toLocaleDateString([], {
                      day: 'numeric',
                      month: 'short',
                      year: 'numeric',
                    })}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  )
}
