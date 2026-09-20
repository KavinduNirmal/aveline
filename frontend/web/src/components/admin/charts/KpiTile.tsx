import type { ReactNode } from "react"

import { DeltaBadge } from "@/components/admin/kpi/DeltaBadge"
import { Card, CardContent } from "@/components/ui/card"
import { formatMetricValue } from "@/lib/admin/data-quality"

export type MetricUnit = 'percent' | 'count' | 'seconds' | 'raw'

/**
 * A single number, with its source named and its honesty label attached.
 *
 * `value === null` renders **"not measured"** — never `0.00%`. `approximate` marks a
 * `precision: "bucket-interpolated"` figure as approximate on the tile, not only in a tooltip.
 *
 * `delta` is optional and additive, so the eight delivered call sites are unaffected. It has no
 * effect on `value`, and `DeltaBadge` renders nothing for a null delta.
 */
export function KpiTile({
  label,
  value,
  unit = 'raw',
  source,
  approximate = false,
  action,
  delta,
}: {
  label: string
  value: number | null | undefined
  unit?: MetricUnit
  source: string
  approximate?: boolean
  action?: ReactNode
  delta?: number | null
}) {
  return (
    <Card className="border-border shadow-xs">
      <CardContent className="p-4">
        <div className="flex items-center justify-between gap-2">
          <span className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
            {label}
          </span>
          {action}
        </div>
        <div className="text-xl font-serif font-semibold mt-2 text-foreground">
          {formatMetricValue(value, unit)}
          {delta !== undefined && <span className="ml-2 align-middle"><DeltaBadge delta={delta} /></span>}
          {approximate && value !== null && value !== undefined && (
            <span className="ml-1.5 text-[10px] font-normal text-muted-foreground">
              approximate
            </span>
          )}
        </div>
        <div className="text-[11px] text-muted-foreground mt-1 truncate">{source}</div>
      </CardContent>
    </Card>
  )
}
