import { Area, AreaChart, CartesianGrid, ReferenceArea, XAxis, YAxis } from "recharts"

import { ChartContainer, ChartTooltip, ChartTooltipContent, type ChartConfig } from "@/components/ui/chart"
import { CONNECT_NULLS } from "@/lib/admin/data-quality"

/**
 * An area trend with honest gaps.
 *
 * It shares `TimeSeriesChart`'s contract — a `null` is a **gap**, never a line through zero
 * (`CONNECT_NULLS === false`) — and differs only in the mark it draws: a gradient-filled area,
 * so a landing-page KPI does not read as one more line chart. The data path is identical, so
 * adding the chart type introduced no second way to compute a series.
 *
 * The gradient id is derived from the first series key, so two areas on one page cannot collide.
 */
export function AreaTrendChart({
  data,
  series,
  config,
  xKey = "bucket",
  partialBucket,
}: {
  data: ReadonlyArray<Record<string, string | number | boolean | null>>
  series: ReadonlyArray<{ dataKey: string; label: string }>
  config: ChartConfig
  xKey?: string
  partialBucket?: string | undefined
}) {
  if (data.length === 0) {
    return (
      <div className="flex h-full items-center justify-center text-xs text-muted-foreground">
        no data
      </div>
    )
  }

  const gradientId = `area-fill-${series[0]?.dataKey ?? "series"}`

  return (
    <ChartContainer config={config} className="h-full w-full">
      <AreaChart data={data as Array<Record<string, string | number | boolean | null>>} accessibilityLayer margin={{ left: 4, right: 8, top: 8 }}>
        <defs>
          {series.map((definition) => (
            <linearGradient
              key={definition.dataKey}
              id={`${gradientId}-${definition.dataKey}`}
              x1="0"
              y1="0"
              x2="0"
              y2="1"
            >
              <stop offset="5%" stopColor={`var(--color-${definition.dataKey})`} stopOpacity={0.35} />
              <stop offset="95%" stopColor={`var(--color-${definition.dataKey})`} stopOpacity={0.02} />
            </linearGradient>
          ))}
        </defs>
        <CartesianGrid vertical={false} />
        <XAxis dataKey={xKey} tickLine={false} axisLine={false} tickMargin={8} />
        <YAxis tickLine={false} axisLine={false} width={48} allowDecimals={false} />
        <ChartTooltip
          content={
            <ChartTooltipContent
              indicator="line"
              // A `null` bucket is reported as "no data" rather than as the previous value's shape.
              formatter={(value) => (value === null ? "no data" : String(value))}
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
        {series.map((definition) => (
          <Area
            key={definition.dataKey}
            type="monotone"
            dataKey={definition.dataKey}
            name={definition.label}
            stroke={`var(--color-${definition.dataKey})`}
            strokeWidth={2}
            fill={`url(#${gradientId}-${definition.dataKey})`}
            dot={false}
            connectNulls={CONNECT_NULLS}
          />
        ))}
      </AreaChart>
    </ChartContainer>
  )
}
