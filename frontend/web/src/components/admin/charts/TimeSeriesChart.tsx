import { CartesianGrid, Line, LineChart, ReferenceArea, XAxis, YAxis } from "recharts"

import { ChartTooltip, ChartTooltipContent } from "@/components/ui/chart"
import { CONNECT_NULLS } from "@/lib/admin/data-quality"

export interface SeriesPoint {
  bucket: string
  [key: string]: string | number | boolean | null
}

export interface SeriesDef {
  dataKey: string
  label: string
}

/**
 * A time series with honest gaps.
 *
 * `connectNulls={false}` is the whole point: a `null` is a **gap**, never a line through zero.
 * The data parser must emit `null` — never `undefined`, never `0` — because Recharts' tooltip
 * otherwise shows the previous value's shape.
 */
export function TimeSeriesChart({
  data,
  series,
  xKey = "bucket",
  partialBucket,
  backfilledBuckets = [],
}: {
  data: SeriesPoint[]
  series: SeriesDef[]
  xKey?: string
  /**
   * The x value of the trailing (or leading) bucket the window has not closed. It is shaded,
   * because a bucket that has not finished is not comparable with a closed one — the plan's
   * non-negotiable #2.
   */
  partialBucket?: string | undefined
  /**
   * x values whose data was reconstructed rather than measured. Drawn as a dashed reference so
   * "approximate" is visible on the chart, not only in a footnote.
   */
  backfilledBuckets?: readonly string[]
}) {
  return (
    <LineChart data={data} accessibilityLayer margin={{ left: 4, right: 8, top: 8 }}>
      <CartesianGrid vertical={false} />
      <XAxis dataKey={xKey} tickLine={false} axisLine={false} tickMargin={8} />
      <YAxis tickLine={false} axisLine={false} width={48} />
      <ChartTooltip
        content={
          <ChartTooltipContent
            indicator="line"
            // A `null` bucket is reported as "no data" rather than as the previous value's
            // shape, which is the classic silent lie a tooltip tells over a gap.
            formatter={(value) => (value === null ? 'no data' : String(value))}
          />
        }
      />
      {partialBucket !== undefined && (
        <ReferenceArea
          x1={partialBucket}
          x2={partialBucket}
          fill="var(--muted)"
          fillOpacity={0.35}
        />
      )}
      {backfilledBuckets.map((bucket) => (
        <ReferenceArea
          key={bucket}
          x1={bucket}
          x2={bucket}
          fill="var(--chart-4)"
          fillOpacity={0.25}
          strokeDasharray="4 4"
        />
      ))}
      {series.map((definition) => (
        <Line
          key={definition.dataKey}
          type="monotone"
          dataKey={definition.dataKey}
          name={definition.label}
          stroke={`var(--color-${definition.dataKey})`}
          strokeWidth={2}
          dot={false}
          connectNulls={CONNECT_NULLS}
        />
      ))}
    </LineChart>
  )
}
