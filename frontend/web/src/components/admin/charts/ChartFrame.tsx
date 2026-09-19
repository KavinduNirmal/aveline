import type { ReactNode } from "react"

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { ChartContainer, type ChartConfig } from "@/components/ui/chart"

/**
 * The frame every chart sits in.
 *
 * Trap 1 of the chart layer: `ResponsiveContainer` needs a **sized parent** — a chart inside an
 * auto-height flex parent collapses to zero. The explicit height token below is the fix, and it
 * is here rather than at each call site so it cannot be forgotten.
 */
export function ChartFrame({
  title,
  description,
  config,
  height = 260,
  action,
  children,
}: {
  title: string
  description?: string
  config: ChartConfig
  height?: number
  action?: ReactNode
  children: ReactNode
}) {
  return (
    <Card className="border-border shadow-xs">
      <CardHeader className="flex flex-row items-start justify-between gap-2">
        <div>
          <CardTitle className="font-serif text-base">{title}</CardTitle>
          {description !== undefined && (
            <CardDescription className="text-xs">{description}</CardDescription>
          )}
        </div>
        {action}
      </CardHeader>
      <CardContent>
        <div className="w-full" style={{ height }}>
          <ChartContainer config={config} className="h-full w-full">
            {children}
          </ChartContainer>
        </div>
      </CardContent>
    </Card>
  )
}
