import type { PricingRule } from "@/types/admin"

export interface TimelineBar {
  id: string
  label: string
  leftPct: number
  widthPct: number
  startsAt: string
  endsAt: string
}

/**
 * Lays each rule's effective window onto one shared axis, so overlapping or adjacent windows are
 * visible at a glance. Pure, so the geometry is unit-testable without a DOM.
 *
 * A null `effectiveTo` means the rule is **still in force** — it is drawn to `now`, not to zero
 * width, because a zero-width bar would read as "expired".
 */
export function computeTimelineLayout(
  rules: readonly PricingRule[],
  now: number = Date.now(),
): TimelineBar[] {
  if (rules.length === 0) return []

  const starts = rules.map((rule) => Date.parse(rule.effectiveFrom))
  const ends = rules.map((rule) =>
    rule.effectiveTo === null ? now : Date.parse(rule.effectiveTo),
  )
  const min = Math.min(...starts)
  const maxRaw = Math.max(...ends)
  const max = maxRaw > min ? maxRaw : min + 1
  const span = max - min

  return rules.map((rule) => {
    const start = Date.parse(rule.effectiveFrom)
    const end = rule.effectiveTo === null ? now : Date.parse(rule.effectiveTo)
    return {
      id: rule.id,
      label: rule.model ?? rule.provider ?? rule.scopeKind,
      leftPct: ((start - min) / span) * 100,
      widthPct: Math.max(1, ((end - start) / span) * 100),
      startsAt: rule.effectiveFrom,
      endsAt: rule.effectiveTo ?? "in force",
    }
  })
}

/** Renders the timeline described by {@link computeTimelineLayout}. */
export function RuleTimeline({ rules }: { rules: readonly PricingRule[] }) {
  const bars = computeTimelineLayout(rules)

  if (bars.length === 0) {
    return <p className="text-xs text-muted-foreground">No effective windows to draw.</p>
  }

  return (
    <div className="flex flex-col gap-2">
      {bars.map((bar) => (
        <div key={bar.id} className="flex items-center gap-2">
          <span className="w-40 shrink-0 truncate text-[11px] font-mono text-muted-foreground">
            {bar.label}
          </span>
          <div className="relative h-4 flex-1 rounded bg-muted/40">
            <div
              data-testid="rule-timeline-bar"
              className="absolute inset-y-0 rounded bg-primary/70"
              style={{ left: `${bar.leftPct}%`, width: `${bar.widthPct}%` }}
              title={`${bar.startsAt} → ${bar.endsAt}`}
            />
          </div>
        </div>
      ))}
    </div>
  )
}
