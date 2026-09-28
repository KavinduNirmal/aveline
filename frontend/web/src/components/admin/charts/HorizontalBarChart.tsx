import { Bar, BarChart, CartesianGrid, Cell, XAxis, YAxis } from 'recharts'

import { ChartContainer, ChartTooltip, ChartTooltipContent, type ChartConfig } from '@/components/ui/chart'

/**
 * A horizontal distribution chart: one bar per category, longest first.
 *
 * `BarChart layout="vertical"` rather than a `PieChart`, deliberately: nothing in the repository
 * imports `PieChart`, the palette has exactly five `--chart-*` tokens and five plan tiers are an
 * exact fit, and ordered tiers read better as bars than as slices.
 *
 * A `null` count is **not** drawn as a zero-length bar: the category is still named on the axis,
 * so "this tier exists but its measure is not available" is distinguishable from "this tier has
 * none".
 */
export interface HorizontalBarDatum {
  category: string
  count: number | null
  fill: string
}

export function HorizontalBarChart({
  data,
  config,
  height: _height = 260,
}: {
  data: readonly HorizontalBarDatum[]
  config: ChartConfig
  height?: number
}) {
  if (data.length === 0) {
    return (
      <div className="flex h-full items-center justify-center text-xs text-muted-foreground">
        no data
      </div>
    )
  }

  return (
    <ChartContainer config={config} className="h-full w-full">
      <BarChart
        data={data as HorizontalBarDatum[]}
        layout="vertical"
        accessibilityLayer
        margin={{ left: 4, right: 16, top: 8, bottom: 8 }}
      >
        <CartesianGrid horizontal={false} />
        <XAxis type="number" tickLine={false} axisLine={false} allowDecimals={false} />
        <YAxis
          type="category"
          dataKey="category"
          tickLine={false}
          axisLine={false}
          width={96}
          tickMargin={8}
        />
        <ChartTooltip
          content={
            <ChartTooltipContent
              // A `null` is a gap: it is reported as "no data", never as the previous bar's
              // value.
              formatter={(value) => (value === null ? 'no data' : String(value))}
            />
          }
        />
        <Bar dataKey="count" radius={4}>
          {data.map((datum, index) => (
            <Cell key={`${index}-${datum.category}`} fill={datum.fill} />
          ))}
        </Bar>
      </BarChart>
    </ChartContainer>
  )
}
