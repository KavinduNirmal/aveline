import { Button } from '@/components/ui/button'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'

import type { SystemAlertDto } from '@/types/admin'

const STATUS_TONE: Record<string, string> = {
  Firing: 'text-destructive',
  Acknowledged: 'text-warning',
  Resolved: 'text-success',
}

const SEVERITY_TONE: Record<string, string> = {
  Critical: 'text-destructive',
  Warning: 'text-warning',
  Info: 'text-muted-foreground',
}

function formatObserved(value: number | null): string {
  if (value === null) return 'not measured'
  return String(value)
}

/**
 * The firing alerts.
 *
 * `ruleName` comes from the **list row** (`SystemAlertDto`) and is what labels the alert. The
 * acknowledge response has no `ruleName`, so it must never be the source of this column.
 */
export function AlertList({
  alerts,
  onAcknowledge,
}: {
  alerts: SystemAlertDto[]
  onAcknowledge: (alert: SystemAlertDto) => void
}) {
  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Alert</TableHead>
          <TableHead>Rule</TableHead>
          <TableHead>Severity</TableHead>
          <TableHead>Status</TableHead>
          <TableHead>Observed</TableHead>
          <TableHead>Threshold</TableHead>
          <TableHead />
        </TableRow>
      </TableHeader>
      <TableBody>
        {alerts.length === 0 ? (
          <TableRow>
            <TableCell colSpan={7} className="text-xs text-muted-foreground">
              No firing alerts.
            </TableCell>
          </TableRow>
        ) : (
          alerts.map((alert) => (
            <TableRow key={alert.id} data-testid={`alert-row-${alert.id}`}>
              <TableCell className="text-xs font-medium text-foreground">
                {alert.title}
                {alert.detail !== null && (
                  <div className="text-[11px] text-muted-foreground">{alert.detail}</div>
                )}
              </TableCell>
              <TableCell
                className="text-xs text-muted-foreground"
                data-testid={`alert-rule-${alert.id}`}
              >
                {alert.ruleName ?? alert.metricName}
              </TableCell>
              <TableCell
                className={`text-xs ${SEVERITY_TONE[alert.severity] ?? 'text-muted-foreground'}`}
              >
                {alert.severity}
              </TableCell>
              <TableCell
                className={`text-xs ${STATUS_TONE[alert.status] ?? ''}`}
                data-testid={`alert-status-${alert.id}`}
              >
                {alert.status}
              </TableCell>
              <TableCell className="font-mono text-xs text-muted-foreground">
                {formatObserved(alert.observedValue)}
              </TableCell>
              <TableCell className="font-mono text-xs text-muted-foreground">
                {formatObserved(alert.threshold)}
              </TableCell>
              <TableCell className="text-right">
                {alert.status === 'Firing' ? (
                  <Button
                    variant="outline"
                    size="sm"
                    className="h-7 text-xs"
                    onClick={() => onAcknowledge(alert)}
                  >
                    Acknowledge
                  </Button>
                ) : (
                  <span className="text-[11px] text-muted-foreground">
                    {alert.status === 'Acknowledged' ? 'Acknowledged' : 'Resolved'}
                  </span>
                )}
              </TableCell>
            </TableRow>
          ))
        )}
      </TableBody>
    </Table>
  )
}
