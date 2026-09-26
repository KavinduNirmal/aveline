import { Copy, Forward, Loader2, RefreshCw, Send } from 'lucide-react'

import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'
import {
  BLOCK_ACTION_BUSY_LABELS,
  type BlockActionId,
  type ResolvedBlockAction,
} from './blockActions'

/** The glyph each action wears. Imported per action so a tree-shaken build keeps only these. */
const ACTION_ICONS: Record<BlockActionId, typeof Copy> = {
  copy: Copy,
  forward: Forward,
  send_to_customer: Send,
  regenerate: RefreshCw,
}

/** The bubble a block sits in; the rail's ink and hairlines have to flip with it. */
type ActionTone = 'own' | 'other'

/**
 * The hairline, fill and ink for the rail, keyed to the bubble that holds the card.
 *
 * The staff bubble is filled with `primary`, so a rail painted for the neutral agent bubble reads
 * as a pale band punched across the message. Each surface states its own ink for the same reason
 * `ATTACHMENT_SURFACE` does in `blocks.tsx`.
 */
const RAIL_SURFACE: Record<ActionTone, string> = {
  // The dividers are the segment separators; `divide-x` on the rail draws them.
  other:
    'border-border/70 bg-muted/25 divide-border/70 text-muted-foreground group-hover/actionable:bg-muted/40',
  own:
    'border-primary-foreground/20 bg-primary-foreground/5 divide-primary-foreground/20 text-primary-foreground/80 group-hover/actionable:bg-primary-foreground/10',
}

/**
 * Where a segment's ink lands when it is live: recessed at rest, at full strength once the
 * associate is over the card or has tabbed into it.
 */
const SEGMENT_IDLE: Record<ActionTone, string> = {
  other: 'hover:bg-muted/70 hover:text-foreground',
  own: 'hover:bg-primary-foreground/15 hover:text-primary-foreground',
}

interface BlockActionBarProps {
  actions: ResolvedBlockAction[]
  onAction: (id: BlockActionId) => void
  /** The card this rail closes, for the toolbar's accessible name. */
  blockTitle: string
  tone?: ActionTone
  /**
   * True inside a tile row, where the card is ~11rem wide and four segments share it. The word is
   * dropped and the glyph carries the segment; the accessible name and the tooltip do not change,
   * which is the case the brief's "tooltips for icon-only buttons" is written for.
   */
  compact?: boolean
}

/**
 * The action rail that closes an AI content block.
 *
 * It is the card's **bottom edge**, not a row of buttons floating under it: the segments are
 * joined — a single hairline divider between neighbours, no rounding of their own — and the
 * parent card's `overflow-hidden` radius shapes the rail's bottom corners. Nothing here uses the
 * shadcn `Button`, whose pill radius is exactly the "floating chip" look this rail is not.
 *
 * A disabled segment stays **focusable** (`aria-disabled` rather than `disabled`): the reason it
 * is unavailable is the tooltip, and a `disabled` button is removed from the tab order, which
 * would leave a keyboard user with a greyed-out word and no explanation.
 */
export function BlockActionBar({
  actions,
  onAction,
  blockTitle,
  tone = 'other',
  compact = false,
}: BlockActionBarProps) {
  if (actions.length === 0) return null

  return (
    <div
      role="toolbar"
      aria-label={`${blockTitle} actions`}
      data-slot="block-action-bar"
      className={cn(
        // `divide-x` draws exactly one hairline between neighbours and none at either end, which
        // is what makes the segments read as one joined control.
        'flex items-stretch divide-x border-t',
        RAIL_SURFACE[tone],
      )}
    >
      {actions.map((action) => {
        const Icon = ACTION_ICONS[action.id]
        const word = action.busy ? BLOCK_ACTION_BUSY_LABELS[action.id] : action.label
        return (
          <Tooltip key={action.id}>
            <TooltipTrigger asChild>
              <button
                type="button"
                data-slot="block-action"
                data-action={action.id}
                data-state={action.busy ? 'busy' : action.enabled ? 'idle' : 'unavailable'}
                // The printed word shortens to fit a narrow rail; the name never does, so a screen
                // reader still hears the action the brief calls it by.
                aria-label={word}
                // Focusable on purpose: see the component note above.
                aria-disabled={action.enabled ? undefined : true}
                aria-busy={action.busy || undefined}
                onClick={() => {
                  if (!action.enabled) return
                  onAction(action.id)
                }}
                className={cn(
                  'flex min-w-0 flex-1 items-center justify-center gap-1.5 px-1.5 py-1.5',
                  'text-[11px] font-medium transition-colors',
                  // An inset ring, so a focused segment does not spill a halo outside the card.
                  'outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-inset',
                  action.enabled
                    ? SEGMENT_IDLE[tone]
                    : 'cursor-not-allowed opacity-45',
                  action.busy && 'opacity-80',
                )}
              >
                {action.busy ? (
                  <Loader2 className="size-3 shrink-0 animate-spin" aria-hidden />
                ) : (
                  <Icon className="size-3 shrink-0" aria-hidden />
                )}
                {!compact && (
                  <span className="min-w-0 truncate">
                    {action.busy ? word : action.shortLabel}
                  </span>
                )}
              </button>
            </TooltipTrigger>
            <TooltipContent>
              {action.enabled || action.busy
                ? word
                : action.reason ?? `${action.label} is not available here.`}
            </TooltipContent>
          </Tooltip>
        )
      })}
    </div>
  )
}
