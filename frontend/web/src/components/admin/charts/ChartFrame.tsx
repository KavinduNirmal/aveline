import type { ReactNode } from "react"

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { ChartContainer, type ChartConfig } from "@/components/ui/chart"

/**
 * The frame every chart sits in.
 *
 * Trap 1 of the chart layer: `ResponsiveContainer` needs a **sized parent** — a chart inside an
 * auto-height flex parent collapses to zero. The explicit height token below is the fix, and it
 * is here rather than at each call site so it cannot be forgotten.
 *
 * Trap 2, found the hard way: **a non-chart state must not be rendered inside the container.**
 * `ResponsiveContainer` measures its parent and renders what it measures (a plain div), which is
 * `0×0` whenever the measurement has not resolved — so an error message placed there collapses and
 * wraps one character per line. `state` exists so the frame owns "failed" and "empty" itself, at
 * full width, and only a real chart ever enters `ChartContainer`.
 */
export function ChartFrame({
  title,
  description,
  config,
  height = 260,
  action,
  state = "ready",
  stateMessage,
  children,
}: {
  title: string
  description?: string
  config: ChartConfig
  height?: number
  action?: ReactNode
  /** `error` and `empty` render a full-width message instead of a chart. */
  state?: "ready" | "error" | "empty"
  /** Required for `error`; ignored otherwise. */
  stateMessage?: string
  children: ReactNode
}) {
  return (
    <Card className="border-border shadow-xs">
      <CardHeader className="flex flex-row items-start justify-between gap-2">
        <div className="min-w-0">
          <CardTitle className="font-serif text-base">{title}</CardTitle>
          {description !== undefined && (
            <CardDescription className="text-xs">{description}</CardDescription>
          )}
        </div>
        {action !== undefined && <div className="shrink-0">{action}</div>}
      </CardHeader>
      <CardContent>
        {state === "ready" ? (
          <div className="w-full" style={{ height }}>
            <ChartContainer config={config} className="h-full w-full">
              {children}
            </ChartContainer>
          </div>
        ) : (
          <div
            className="flex w-full flex-col items-start justify-center gap-1 text-xs text-muted-foreground"
            style={{ height }}
            role={state === "error" ? "alert" : undefined}
          >
            <span className="font-medium text-foreground">
              {state === "error" ? "This could not be loaded" : "Nothing to show"}
            </span>
            <span className="max-w-prose">
              {state === "error"
                ? `${trimTrailingPeriod(stateMessage ?? "The read failed")}. Nothing is shown here rather than a substitute.`
                : `${trimTrailingPeriod(stateMessage ?? "No data in this window")}. A blank is not a zero.`}
            </span>
          </div>
        )}
      </CardContent>
    </Card>
  )
}

/** Keeps the sentence readable when the server's message already ends in a full stop. */
function trimTrailingPeriod(message: string): string {
  return message.trim().replace(/\.+$/, "")
}
