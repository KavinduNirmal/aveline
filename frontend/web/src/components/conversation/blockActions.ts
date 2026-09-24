/**
 * The action vocabulary for an AI message's content blocks.
 *
 * A block is what the agent actually published — a draft (`suggestion`), a catalogue piece
 * (`piece`), a styled look (`look`) — and each one affords a different set of things the
 * associate can do with it. Which actions a block offers is a property of the block, not of the
 * bubble it happens to sit in, so the mapping lives here as plain data with no React in sight:
 * every rule below is asserted by `blockActions.test.ts` without rendering anything.
 *
 * The vocabulary is deliberately narrow. The brief's "item block" is the wire's `piece`, and its
 * "lookbook block" is the wire's `look`; both real names and both brief names are accepted, so a
 * payload written either way resolves to the same rail.
 *
 * Sending and forwarding both mean **delivery to the customer over their own channel**, never a
 * note written back into the Salon: a note is Aveline's context, reaches nobody, and on a
 * client-bound thread it wakes the agent. The two are kept apart in this module because they have
 * different destinations — the open thread's client versus a client the associate picks — but they
 * call the same endpoint, and neither is a room post.
 */

import type { ConversationDto } from '@/types/conversation'
import { salonLabel } from './salonLabel'

/** One thing an associate can do with a content block. */
export type BlockActionId = 'copy' | 'forward' | 'send_to_customer' | 'regenerate'

/** The word on the button, the tooltip and the accessible name. */
export const BLOCK_ACTION_LABELS: Record<BlockActionId, string> = {
  copy: 'Copy',
  forward: 'Forward',
  send_to_customer: 'Send to customer',
  regenerate: 'Regenerate',
}

/**
 * Short labels for the narrow case.
 *
 * The rail shares its width between its segments, and `piece` tiles are ~11rem wide. The
 * accessible name always stays the full label; only the printed word shortens.
 */
export const BLOCK_ACTION_SHORT_LABELS: Record<BlockActionId, string> = {
  copy: 'Copy',
  forward: 'Forward',
  send_to_customer: 'Send',
  regenerate: 'Redo',
}

/** What the segment says while it is waiting on the server. */
export const BLOCK_ACTION_BUSY_LABELS: Record<BlockActionId, string> = {
  copy: 'Copying…',
  forward: 'Sending…',
  send_to_customer: 'Sending…',
  regenerate: 'Redoing…',
}

/**
 * Which actions each block type offers. Absence is the answer: a type that is not listed gets no
 * rail at all, and a listed type gets exactly these — never a superset.
 *
 * `piece` carries Forward alone by design (the brief names one action for an item block), so the
 * tile grid does not grow a Copy on every card.
 */
const BLOCK_ACTIONS: Record<string, readonly BlockActionId[]> = {
  suggestion: ['copy', 'send_to_customer', 'regenerate'],
  piece: ['forward'],
  item: ['forward'],
  look: ['copy', 'forward', 'regenerate'],
  lookbook: ['copy', 'forward', 'regenerate'],
}

/**
 * The actions a block type offers, in the order they are drawn. An unknown type offers none,
 * which is what keeps an agent block this build has not been taught from growing a rail whose
 * buttons could not do anything sensible.
 */
export function actionsForBlockType(type: string): readonly BlockActionId[] {
  return BLOCK_ACTIONS[type] ?? []
}

/** The subset of a content block this module reads. Kept structural so it needs no import cycle. */
export interface ActionableBlock {
  type: string
  text?: string
  name?: string
  price?: number
  size?: string
  stock?: number
  imageUrl?: string
  url?: string
  itemId?: string
  [key: string]: unknown
}

/** A customer a block can be delivered to, as the forward picker needs to draw it. */
export interface DeliveryTarget {
  id: string
  label: string
  /** When the thread last saw a word, so the list reads like the inbox it is drawn from. */
  lastMessageAt: string | null
}

/** True when a thread's customer can actually be reached on a channel. */
export function isDeliverable(conversation: ConversationDto): boolean {
  // A client-bound thread keeps the number on the customer; a thread that arrived on a channel
  // keeps the handle it came from. The concierge has neither, so it is not a destination.
  return Boolean(conversation.customerId || conversation.externalRef)
}

/**
 * The clients a block may be forwarded to.
 *
 * Only threads that can actually receive a delivery are offered, and the open thread is excluded
 * — forwarding a card to the client who is already on screen is what "Send to customer" is for.
 * The remaining rows keep the order the list was served in, so the picker reads like the inbox
 * behind it.
 */
export function deliveryTargetsFrom(
  conversations: ConversationDto[],
  currentConversationId: string | null,
): DeliveryTarget[] {
  return conversations
    .filter((conversation) => conversation.id !== currentConversationId)
    .filter(isDeliverable)
    .map((conversation) => ({
      id: conversation.id,
      label: salonLabel(conversation),
      lastMessageAt: conversation.lastMessageAt,
    }))
}

/** The block's own name, or a noun for it, used in a toast and a confirmation. */
export function blockTitle(block: ActionableBlock): string {
  const name = typeof block.name === 'string' ? block.name.trim() : ''
  if (name) return name
  switch (block.type) {
    case 'suggestion':
      return 'Draft reply'
    case 'piece':
    case 'item':
      return 'Piece'
    case 'look':
    case 'lookbook':
      return 'Look'
    default:
      return 'Aveline'
  }
}

/**
 * The block as words a person can read.
 *
 * Copy and delivery both hand over text, and the text has to be the block rather than the message:
 * the whole point of the rail is acting on one card. A `piece` becomes a line an associate would
 * actually send ("Silk Wrap Blouse · Size M · LKR 18,500"), a `look` becomes its name and the
 * styling note, and a `suggestion` is already the sentence to send, so it is passed through
 * untouched — trimming only the trailing space a model sometimes leaves.
 */
export function blockToText(block: ActionableBlock): string {
  switch (block.type) {
    case 'piece':
    case 'item':
      return pieceLine(block)
    case 'look':
    case 'lookbook':
      return lookLine(block)
    case 'suggestion':
      return (block.text ?? '').trim()
    default: {
      const text = typeof block.text === 'string' ? block.text.trim() : ''
      return text || blockTitle(block)
    }
  }
}

/** A piece as one line: its name, then whatever the server actually sent about it. */
function pieceLine(block: ActionableBlock): string {
  const parts = [blockTitle(block)]
  if (typeof block.size === 'string' && block.size.trim()) {
    parts.push(`Size ${block.size.trim()}`)
  }
  if (typeof block.price === 'number' && Number.isFinite(block.price)) {
    parts.push(`LKR ${block.price.toLocaleString()}`)
  }
  return parts.join(' · ')
}

/** A look as its name and its rationale, with neither half invented when it is missing. */
function lookLine(block: ActionableBlock): string {
  const name = typeof block.name === 'string' ? block.name.trim() : ''
  const text = typeof block.text === 'string' ? block.text.trim() : ''
  // Only a name the server actually sent is worth prefixing, so an unnamed look does not print
  // the fallback noun in front of its own note.
  if (name && text) return `${name} — ${text}`
  return text || name || 'Look'
}

/** What the surrounding thread can currently do, as the rail needs to know it. */
export interface BlockActionEnvironment {
  /** True when the open thread's customer can be reached, so "send to customer" has a destination. */
  hasCustomerDestination: boolean
  /** True when there is at least one other client a forward could be delivered to. */
  hasForwardDestination: boolean
  /** True while the agent is already producing a reply for this thread. */
  agentBusy: boolean
  /** The action on this block currently waiting on the server, if any. */
  pending: BlockActionId | null
}

/** One segment of the rail, fully resolved. */
export interface ResolvedBlockAction {
  id: BlockActionId
  /** The full word, for the accessible name and the tooltip. */
  label: string
  /** The word printed on the segment, which shortens for the narrow rail. */
  shortLabel: string
  enabled: boolean
  /** Why the action is unavailable. Present exactly when `enabled` is false. */
  reason?: string
  /** True for the action that is waiting on the server right now. */
  busy: boolean
}

/** Why an unavailable action is unavailable, in the app's own voice. */
const REASONS = {
  pending: 'Another action is already running on this block.',
  noCustomer: "This thread isn't linked to a client yet.",
  noForwardTarget: 'No other client to forward to yet.',
  agentBusy: 'Aveline is still working on a reply.',
} as const

/**
 * The rail's final state: which segments exist, which are live, and what to say when one is not.
 *
 * Only the actions the block type offers are ever returned, so "Regenerate" cannot appear on a
 * piece no matter what the thread can do. The rules that follow are about availability within
 * that set, and every one of them is a sentence the associate can act on rather than a greyed
 * button with no explanation.
 */
export function resolveBlockActions(
  block: ActionableBlock,
  environment: BlockActionEnvironment,
): ResolvedBlockAction[] {
  return actionsForBlockType(block.type).map((id) => {
    const busy = environment.pending === id
    const reason = unavailableReason(id, environment)
    return {
      id,
      label: BLOCK_ACTION_LABELS[id],
      shortLabel: BLOCK_ACTION_SHORT_LABELS[id],
      enabled: reason === undefined && !busy,
      // The busy segment states itself through its busy label; only a blocked one needs a reason.
      reason: reason ?? (busy ? REASONS.pending : undefined),
      busy,
    }
  })
}

/** The first reason an action cannot run, or `undefined` when it can. */
function unavailableReason(
  id: BlockActionId,
  environment: BlockActionEnvironment,
): string | undefined {
  // One action at a time on a block. Anything else waiting would race the same content.
  if (environment.pending !== null && environment.pending !== id) return REASONS.pending
  switch (id) {
    case 'send_to_customer':
      return environment.hasCustomerDestination ? undefined : REASONS.noCustomer
    case 'forward':
      return environment.hasForwardDestination ? undefined : REASONS.noForwardTarget
    case 'regenerate':
      return environment.agentBusy ? REASONS.agentBusy : undefined
    default:
      return undefined
  }
}

/** True when a block type draws a rail at all — the cheap check the renderer makes first. */
export function hasBlockActions(block: ActionableBlock | null | undefined): boolean {
  return Boolean(block) && actionsForBlockType(block!.type).length > 0
}

/**
 * What the rail calls when a segment is pressed.
 *
 * Every handler is optional, and a handler that is absent disables its action: a renderer with no
 * thread behind it — a preview, a test, the docs gallery — draws the rail's real shape with the
 * real reason rather than inventing a destination.
 */
export interface BlockActionHandlers {
  onCopy?: (block: ActionableBlock, messageId: string) => void
  onForward?: (block: ActionableBlock, messageId: string) => void
  onSendToCustomer?: (block: ActionableBlock, messageId: string) => void
  onRegenerate?: (block: ActionableBlock, messageId: string) => void
}

/**
 * Everything the rail needs about the thread it is drawn in, threaded from the surface that owns
 * the conversation rather than read from a context, so `BlockList` stays renderable on its own.
 */
export interface BlockActionBridge {
  handlers: BlockActionHandlers
  /** Whether the open client is reachable, another client exists to forward to, the agent is busy. */
  environment: Pick<
    BlockActionEnvironment,
    'hasCustomerDestination' | 'hasForwardDestination' | 'agentBusy'
  >
  /** The action in flight for one message, or null. */
  pending: (messageId: string) => BlockActionId | null
}

/** The handler that serves one action id, or `undefined` when the surface did not wire it. */
export function handlerForAction(
  handlers: BlockActionHandlers,
  id: BlockActionId,
): ((block: ActionableBlock, messageId: string) => void) | undefined {
  switch (id) {
    case 'copy':
      return handlers.onCopy
    case 'forward':
      return handlers.onForward
    case 'send_to_customer':
      return handlers.onSendToCustomer
    case 'regenerate':
      return handlers.onRegenerate
  }
}
