import { Badge } from '@/components/ui/badge'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'

import type { ReadinessDto } from '@/types/admin'

/**
 * Dependency readiness.
 *
 * A missing check is named, never implied healthy: the delivered fallback invented three probe
 * rows so the table looked populated. An empty list says so.
 */
export function ReadinessTable({ readiness }: { readiness: ReadinessDto }) {
  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center gap-2 text-sm">
        <span className="text-muted-foreground">Overall readiness</span>
        <Badge
          variant={readiness.status === 'Healthy' ? 'outline' : 'destructive'}
          className={
            readiness.status === 'Healthy'
              ? 'text-success'
              : readiness.status === 'Degraded'
                ? 'text-warning'
                : undefined
          }
        >
          {readiness.status}
        </Badge>
      </div>

      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Probe</TableHead>
            <TableHead>Status</TableHead>
            <TableHead>Latency</TableHead>
            <TableHead>Message</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {readiness.checks.length === 0 ? (
            <TableRow>
              <TableCell colSpan={4} className="text-xs text-muted-foreground">
                No dependency probes were reported.
              </TableCell>
            </TableRow>
          ) : (
            readiness.checks.map((check) => (
              <TableRow key={check.name}>
                <TableCell className="font-medium text-foreground">{check.name}</TableCell>
                <TableCell>
                  <span
                    className={
                      check.status === 'Healthy'
                        ? 'text-xs text-success'
                        : check.status === 'Degraded'
                          ? 'text-xs text-warning'
                          : 'text-xs text-destructive'
                    }
                  >
                    {check.status}
                  </span>
                </TableCell>
                <TableCell className="font-mono text-xs text-muted-foreground">
                  {check.durationMs} ms
                </TableCell>
                <TableCell className="text-xs text-muted-foreground">
                  {check.message ?? '—'}
                </TableCell>
              </TableRow>
            ))
          )}
        </TableBody>
      </Table>
    </div>
  )
}
