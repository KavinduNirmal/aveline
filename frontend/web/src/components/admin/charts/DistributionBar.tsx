import { Bar, BarChart, Cell, XAxis, YAxis } from "recharts"

import { ChartContainer, type ChartConfig } from "@/components/ui/chart"

/**
 * A one-row distribution: a single stacked bar whose segments are the categories, longest first in
 * the legend. This is how "free versus premium" is readable at a glance on the landing page.
 *
 * It takes **counts**, not percentages, and derives the proportions itself:
 *
 * - a tier with no measurable value is **named**, not silently dropped from the total;
 * - when every count is zero it says *"no organizations"* rather than dividing by zero and
 *   rendering `NaN%`;
 * - an empty list renders an empty state rather than a zero-width bar.
 *
 * `layout="vertical"` with a single category row, so the bar is horizontal and the category axis
 * carries the tier names.
 */
export interface DistributionSegment {
  key: string
  label: string
  value: number
  free: boolean
}

export function DistributionBar({
  segments,
  config,
  height: _height = 56,
}: {
  segments: readonly DistributionSegment[]
  config: ChartConfig
  height?: number
}) {
  const total = segments.reduce((sum, segment) => sum + Math.max(0, segment.value), 0)

  if (segments.length === 0) {
    return (
      <div className="flex h-full items-center justify-center text-xs text-muted-foreground">
        no data
      </div>
    )
  }

  if (total === 0) {
    return (
      <div className="flex h-full items-center justify-center text-xs text-muted-foreground">
        no organizations
      </div>
    )
  }

  const freeCount = segments
    .filter((segment) => segment.free)
    .reduce((sum, segment) => sum + segment.value, 0)
  const premiumCount = total - freeCount
  const share = (value: number) => Math.round((value / total) * 100)
  const freeShare = share(freeCount)
  const premiumShare = 100 - freeShare

  // One row, one key per segment, so Recharts stacks them into a single horizontal bar.
  const row: Record<string, string | number> = { category: "Plan mix" }
  for (const segment of segments) {
    row[segment.key] = Math.max(0, segment.value)
  }

  return (
    <div className="flex flex-col gap-2">
      <div style={{ height: 40 }}>
        <ChartContainer config={config} className="h-full w-full">
          <BarChart
            data={[row]}
            layout="vertical"
            accessibilityLayer
            margin={{ left: 0, right: 0, top: 4, bottom: 4 }}
          >
            <XAxis type="number" hide domain={[0, total]} />
            <YAxis type="category" dataKey="category" hide />
            {segments.map((segment, index) => (
              <Bar
                key={segment.key}
                dataKey={segment.key}
                stackId="mix"
                fill={`var(--color-${segment.key})`}
                radius={index === segments.length - 1 ? [0, 4, 4, 0] : 0}
                isAnimationActive={false}
              >
                {[row].map((_, cellIndex) => (
                  <Cell key={`${segment.key}-${cellIndex}`} fill={`var(--color-${segment.key})`} />
                ))}
              </Bar>
            ))}
          </BarChart>
        </ChartContainer>
      </div>

      <p className="text-[11px] text-muted-foreground">
        <span className="font-medium text-foreground">{total}</span> organizations ·{" "}
        <span className="font-medium text-foreground">{freeShare}%</span> free (
        {freeCount}) · <span className="font-medium text-foreground">{premiumShare}%</span> premium
        ({premiumCount})
      </p>

      <ul className="flex flex-wrap items-center gap-x-3 gap-y-1 text-[11px]">
        {segments.map((segment) => (
          <li key={segment.key} className="inline-flex items-center gap-1.5">
            <span
              className="size-2 rounded-full"
              style={{ background: `var(--color-${segment.key})` }}
              aria-hidden="true"
            />
            <span className="text-muted-foreground">
              {segment.label}{" "}
              <span className="font-mono text-foreground">{segment.value}</span>
            </span>
          </li>
        ))}
      </ul>
    </div>
  )
}
