import { Bar, BarChart, CartesianGrid, Cell, XAxis, YAxis } from "recharts"

import { ChartTooltip, ChartTooltipContent } from "@/components/ui/chart"
import { usePrefersReducedMotion } from "@/hooks/usePrefersReducedMotion"
import { peakIndex, type ActivityPoint } from "@/lib/admin/activity-series"

/**
 * Business-action volume, as animated bars.
 *
 * The bars carry a diagonal hatch, and the peak bucket is filled solid so the shape is readable at
 * a glance. **Animation is gated on `prefers-reduced-motion`.**
 *
 * The data is Postgres-only (`GET /admin/audit`), which is what keeps this inside the Q11 boundary:
 * the console charts what it owns and links out for the platform's time series.
 */
export function ActivityChart({ points }: { points: ActivityPoint[] }) {
  const reducedMotion = usePrefersReducedMotion()
  const peak = peakIndex(points)
  const hatchId = "admin-activity-hatch"

  return (
    <BarChart data={points} accessibilityLayer margin={{ left: 4, right: 8, top: 8 }}>
      <defs>
        <pattern
          id={hatchId}
          patternUnits="userSpaceOnUse"
          width="6"
          height="6"
          patternTransform="rotate(45)"
        >
          <rect width="6" height="6" fill="var(--color-activity)" opacity="0.18" />
          <line
            x1="0"
            y1="0"
            x2="0"
            y2="6"
            stroke="var(--color-activity)"
            strokeWidth="2"
            opacity="0.5"
          />
        </pattern>
      </defs>
      <CartesianGrid vertical={false} />
      <XAxis dataKey="label" tickLine={false} axisLine={false} tickMargin={8} />
      <YAxis allowDecimals={false} tickLine={false} axisLine={false} width={32} />
      <ChartTooltip content={<ChartTooltipContent indicator="dot" />} />
      <Bar
        dataKey="value"
        radius={[6, 6, 0, 0]}
        isAnimationActive={!reducedMotion}
        animationDuration={700}
      >
        {points.map((point, index) => (
          <Cell
            key={point.key}
            fill={index === peak ? "var(--color-activity)" : `url(#${hatchId})`}
          />
        ))}
      </Bar>
    </BarChart>
  )
}
