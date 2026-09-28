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
import { formatCount, formatPercent } from '@/lib/format-money'
import { limitPercent, type EntitlementUsage } from '@/lib/billing-api'

interface UsageVsLimitsTableProps {
  usage: EntitlementUsage
}

/**
 * The entitlement keys that bound a shop. The limit is a real number from the plan; the **meter**
 * is what may be missing.
 */
const METER_LABELS: Record<string, string> = {
  'blossoms.monthly': 'Blossoms per month',
  'staff.max': 'Staff seats',
  'customers.active.max': 'Active clients',
  'api.requests.monthly': 'API requests per month',
  'api.requests.perMinute': 'API requests per minute',
  'whatsapp.monthly': 'WhatsApp messages per month',
  'stats.retentionDays': 'Statistics retention (days)',
}

/**
 * The metrics this product has **no writer for**. `whatsapp.monthly` is the one that matters: there
 * is no outbound WhatsApp send log (`InboundMessageLogs` records inbound only, and Salon `Messages`
 * are not WhatsApp sends), so any observed figure would be invented. The row therefore states
 * "not measured" with the reason rather than rendering the zero the server would have to return.
 */
const UNMETERED_KEYS = new Set(['whatsapp.monthly'])

/**
 * Usage against the plan's limits. Each row carries its limit and its observed value separately; a
 * percentage is only computed when there is a limit to measure against (`limitPercent` returns
 * `null` for a zero or absent allowance, which renders "not measured" rather than "0%").
 */
export function UsageVsLimitsTable({ usage }: UsageVsLimitsTableProps) {
  return (
    <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
      <CardHeader>
        <CardTitle className="font-serif text-lg font-medium">Usage against plan limits</CardTitle>
        <CardDescription>
          What this period has consumed, against the allowance the plan grants. A metric with no
          meter says so instead of showing a zero.
        </CardDescription>
      </CardHeader>
      <CardContent>
        {usage.items.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            No entitlement limits are recorded for this plan.
          </p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Metric</TableHead>
                <TableHead>Limit</TableHead>
                <TableHead>Observed</TableHead>
                <TableHead>Used</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {usage.items.map((item) => {
                const unmetered = UNMETERED_KEYS.has(item.key)
                return (
                  <TableRow key={item.key}>
                    <TableCell className="font-medium">
                      {METER_LABELS[item.key] ?? item.key}
                      {item.hardLimit ? null : (
                        <Badge variant="outline" className="ml-2 text-xs">
                          soft
                        </Badge>
                      )}
                    </TableCell>
                    <TableCell>{formatCount(item.allowed)}</TableCell>
                    <TableCell>
                      {unmetered ? (
                        <span className="italic text-muted-foreground">not measured</span>
                      ) : (
                        formatCount(item.observed)
                      )}
                    </TableCell>
                    <TableCell>
                      {unmetered ? (
                        <span className="text-xs text-muted-foreground">
                          no outbound send log exists
                        </span>
                      ) : (
                        formatPercent(limitPercent(item.observed, item.allowed))
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
