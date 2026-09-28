import type { ReactNode } from 'react'

import { cn } from '@/lib/utils'
import { BlockActionBar } from './BlockActionBar'
import {
  handlerForAction,
  resolveBlockActions,
  type ActionableBlock as ActionableBlockModel,
  type BlockActionBridge,
} from './blockActions'
import { isTileBlock } from './tileBlocks'

/** The bubble a block sits in; the rail's ink flips with it. */
type ActionTone = 'own' | 'other'

interface ActionableBlockProps {
  /** The block the rail acts on. */
  block: ActionableBlockModel
  /** The message the block belongs to, which is what a regenerate or a share link names. */
  messageId?: string
  /** The thread behind the rail. Absent — a preview, a test, the docs gallery — draws no rail. */
  bridge?: BlockActionBridge
  /** The card this rail closes; the toolbar's accessible name is built from it. */
  title: string
  tone?: ActionTone
  /**
   * The card's own surface — border tint, fill and radius. The rail inherits the bottom corners
   * through `overflow-hidden`, so a block keeps the rounding it already had and the rail takes
   * the shape of the card's foot rather than a radius of its own.
   */
  className?: string
  /** Padding for the content above the rail. */
  contentClassName?: string
  children: ReactNode
}

/**
 * An AI content block with its action rail fused to the bottom edge.
 *
 * The rail is part of the card, not a sibling floating beneath it: both live inside one
 * `overflow-hidden` box so the card's radius clips the rail's bottom corners, and the rail's own
 * `border-t` is the only seam between them. A block with no rail — an unknown type, or a surface
 * that wired no handlers — renders its content alone, exactly as it did before this existed.
 */
export function ActionableBlock({
  block,
  messageId,
  bridge,
  title,
  tone = 'other',
  className,
  contentClassName,
  children,
}: ActionableBlockProps) {
  const actions =
    bridge && messageId
      ? resolveBlockActions(block, {
          ...bridge.environment,
          pending: bridge.pending(messageId),
        })
          // A segment whose surface wired no handler is dropped rather than drawn dead: the rail
          // shows what it can actually do here.
          .filter((action) => handlerForAction(bridge.handlers, action.id) !== undefined)
      : []

  const hasRail = actions.length > 0

  return (
    <div
      data-slot="actionable-block"
      data-block-type={block.type}
      className={cn(
        // A column so the rail is the card's last row and the content takes the room left over;
        // `overflow-hidden` is what makes the card's own radius clip the rail's bottom corners.
        'flex min-w-0 flex-col overflow-hidden',
        className,
        hasRail && 'group/actionable',
      )}
    >
      <div className={cn('flex flex-1 flex-col', contentClassName)}>{children}</div>
      {hasRail && bridge && messageId && (
        <BlockActionBar
          actions={actions}
          blockTitle={title}
          tone={tone}
          // A tile in a curated row is ~11rem wide and shares it between its segments, so the
          // glyph alone carries the segment there.
          compact={isTileBlock(block)}
          onAction={(id) => {
            handlerForAction(bridge.handlers, id)?.(block, messageId)
          }}
        />
      )}
    </div>
  )
}
