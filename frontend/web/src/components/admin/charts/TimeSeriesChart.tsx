import { CartesianGrid, Line, LineChart, XAxis, YAxis } from "recharts"

import { ChartTooltip, ChartTooltipContent } from "@/components/ui/chart"
import { CONNECT_NULLS } from "@/lib/admin/data-quality"

export interface SeriesPoint {
  bucket: string
  [key: string]: string | number | null
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
}: {
  data: SeriesPoint[]
  series: SeriesDef[]
  xKey?: string
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
