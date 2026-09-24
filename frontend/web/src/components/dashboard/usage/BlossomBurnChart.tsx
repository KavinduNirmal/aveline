import {
  Area,
  AreaChart,
  CartesianGrid,
  ChartContainer,
  ChartTooltip,
  ChartTooltipContent,
  ReferenceLine,
  XAxis,
  YAxis,
  type ChartConfig,
} from '@/components/ui/chart'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { usePrefersReducedMotion } from '@/hooks/usePrefersReducedMotion'
import { CONNECT_NULLS } from '@/lib/dashboard-chart-rules'
import { formatCount } from '@/lib/format-money'
import type { BlossomUsage } from '@/lib/billing-api'

interface BlossomBurnChartProps {
  usage: BlossomUsage
  /** The period allowance, drawn as a reference line. `null` when the server did not report one. */
  allowance: number | null
}

const CHART_CONFIG: ChartConfig = {
  blossoms: { label: 'Blossoms', color: 'var(--primary)' },
}

/**
 * Daily Blossom consumption.
 *
 * Three honesty rules, each visible in the code:
 *
 * 1. **No series is no chart.** An empty series renders an explicit "not measured" state rather
 *    than an empty axis that reads as "you used nothing".
 * 2. **`connectNulls` is `false`.** A gap in the series is a gap in the data, and a line drawn
 *    through it would report consumption the server never measured.
 * 3. **Animation respects the reader's preference** through `usePrefersReducedMotion`, matching the
 *    rest of the product.
 */
export function BlossomBurnChart({ usage, allowance }: BlossomBurnChartProps) {
  const reduceMotion = usePrefersReducedMotion()
  const data = usage.series.map((point) => ({ key: point.key, blossoms: point.blossoms }))

  return (
    <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
      <CardHeader>
        <CardTitle className="font-serif text-lg font-medium">Blossom consumption</CardTitle>
        <CardDescription>
          Daily consumption across the requested window. Total:{' '}
          {formatCount(usage.totalBlossoms)} Blossoms.
        </CardDescription>
      </CardHeader>
      <CardContent>
        {data.length === 0 ? (
          <p className="text-sm italic text-muted-foreground">
            not measured — no consumption points were recorded in this window.
          </p>
        ) : (
          <ChartContainer config={CHART_CONFIG} className="h-64 w-full">
            <AreaChart data={data} margin={{ left: 8, right: 8, top: 8, bottom: 8 }}>
              <CartesianGrid vertical={false} />
              <XAxis dataKey="key" tickLine={false} axisLine={false} tickMargin={8} />
              <YAxis tickLine={false} axisLine={false} width={48} />
              <ChartTooltip content={<ChartTooltipContent />} />
              {allowance !== null && allowance > 0 ? (
                <ReferenceLine
                  y={allowance}
                  stroke="var(--muted-foreground)"
                  strokeDasharray="4 4"
                />
              ) : null}
              <Area
                dataKey="blossoms"
                type="monotone"
                stroke="var(--color-blossoms)"
                fill="var(--color-blossoms)"
                fillOpacity={0.2}
                connectNulls={CONNECT_NULLS}
                isAnimationActive={!reduceMotion}
              />
            </AreaChart>
          </ChartContainer>
        )}
      </CardContent>
    </Card>
  )
}
