import { formatMoney } from '@/lib/format-money'
import { FIGURE_ACCENT_CLASS } from '@/lib/dashboard-chart-rules'
import { cn } from '@/lib/utils'

interface MoneyProps {
  value: number | null | undefined
  currency: string
  /**
   * Size only. The figure set, the tabular digits and the accent are fixed here, so a money figure
   * cannot acquire a different treatment from the one beside it.
   */
  className?: string
}

/**
 * A money figure, in the one treatment the dashboard uses for numbers.
 *
 * **Three things are decided once here, and they are the whole reason this exists:**
 *
 * 1. **`font-mono tabular-nums`** — the digits are the same width, so a column of figures lines up
 *    and does not shift as it updates. This is the difference between a table of numbers and a wall
 *    of them.
 * 2. **The theme primary as the accent**, through `FIGURE_ACCENT_CLASS`. A near-black figure was
 *    readable and characterless; these are the customer's own numbers and the most important thing
 *    on the page, so they carry the brand.
 * 3. **`formatMoney` is the only formatter**, so `null` still renders "not measured" and never `0`,
 *    exactly as it does in a `KpiCard`.
 *
 * A caller that finds itself adding a colour or a `font-*` class is changing the treatment rather
 * than composing it, and should be changing this component instead.
 */
export function Money({ value, currency, className }: MoneyProps) {
  return (
    <span className={cn('font-mono tabular-nums', FIGURE_ACCENT_CLASS, className)}>
      {formatMoney(value, currency)}
    </span>
  )
}
