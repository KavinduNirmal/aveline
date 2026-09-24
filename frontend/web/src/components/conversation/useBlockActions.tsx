import { useCallback, useMemo, useState, type ReactNode } from 'react'
import { toast } from 'sonner'

import type { ConversationDto } from '@/types/conversation'
import { ForwardPickerDialog, SendToCustomerDialog } from './BlockActionDialogs'
import {
  blockTitle,
  blockToText,
  deliveryTargetsFrom,
  type ActionableBlock,
  type BlockActionBridge,
  type BlockActionId,
} from './blockActions'

/** A text payload the rail produced and is about to act on. */
interface BlockPayload {
  text: string
  title: string
}

/** The state the forward picker and the send confirmation are driven by. */
interface BlockActionRequest extends BlockPayload {
  messageId: string
}

export interface UseBlockActionsOptions {
  /** The thread on screen; null when none is open. */
  conversationId: string | null
  /** The client the open thread belongs to, for the send confirmation's sentence. */
  customerName?: string | null
  /** True when the open thread's client can actually be reached on a channel. */
  customerReachable?: boolean
  /** Every Salon, so the forward picker knows which clients a card may go to. */
  conversations: ConversationDto[]
  /** Delivers a block's words to a client's own channel. Rejects when the server refuses. */
  deliver: (targetConversationId: string, text: string) => Promise<void>
  /** Re-runs the agent for the turn behind a message. Absent leaves Regenerate unavailable. */
  regenerate?: (messageId: string) => Promise<void>
  /** True while the agent is producing a reply, which is when a second regenerate is wrong. */
  agentBusy: boolean
}

export interface BlockActionsController {
  /** The thread's rail config, handed to `BlockList`/`MessageBubble`. */
  bridge: BlockActionBridge
  /** Mount once, wherever the thread is drawn. */
  dialogs: ReactNode
}

/**
 * Clipboard write with a fallback.
 *
 * `navigator.clipboard` is unavailable on a non-secure origin, which is exactly how the dashboard
 * is reached on a boutique's own LAN host, so the copy falls back to a selection the browser will
 * honour there rather than reporting a failure that is not the associate's fault.
 */
async function writeClipboard(text: string): Promise<void> {
  if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(text)
    return
  }
  const area = document.createElement('textarea')
  area.value = text
  area.setAttribute('readonly', '')
  area.style.position = 'fixed'
  area.style.top = '-1000px'
  area.style.opacity = '0'
  document.body.appendChild(area)
  area.select()
  try {
    if (!document.execCommand?.('copy')) throw new Error('copy refused')
  } finally {
    document.body.removeChild(area)
  }
}

/**
 * The action rail's behaviour for one thread.
 *
 * Each surface that draws a thread — the Salon section and the Aveline drawer — instantiates this
 * with its own conversation id, delivery call and regenerate, so the rail's copy, forward, send
 * and regenerate all act on the thread the associate is actually looking at, and neither surface's
 * in-flight action can disable the other's.
 *
 * **Send and Forward both leave the boutique.** They call the delivery endpoint, which sends the
 * words to the client on their own channel; neither writes a note into the Salon. The only
 * difference between them is the destination: Send is the client on screen, Forward is a client
 * the associate picks, and both are confirmed before anything goes out because a message to a
 * client's phone cannot be recalled.
 *
 * Per-block state is a map keyed by message id, so one block can be mid-delivery while another is
 * mid-regenerate, and the map is what disables the rest of a block's segments while one runs: two
 * actions on the same content would race each other.
 */
export function useBlockActions(options: UseBlockActionsOptions): BlockActionsController {
  const {
    conversationId,
    customerName,
    customerReachable,
    conversations,
    deliver,
    regenerate,
    agentBusy,
  } = options

  /** The action in flight, keyed by message id. */
  const [pending, setPending] = useState<Record<string, BlockActionId>>({})
  const [forwardRequest, setForwardRequest] = useState<BlockActionRequest | null>(null)
  const [sendRequest, setSendRequest] = useState<BlockActionRequest | null>(null)
  const [sending, setSending] = useState(false)

  const targets = useMemo(
    () => deliveryTargetsFrom(conversations, conversationId),
    [conversations, conversationId],
  )

  const markPending = useCallback((messageId: string, action: BlockActionId | null) => {
    setPending((prev) => {
      const next = { ...prev }
      if (action === null) delete next[messageId]
      else next[messageId] = action
      return next
    })
  }, [])

  const handleCopy = useCallback(
    async (block: ActionableBlock, messageId: string) => {
      markPending(messageId, 'copy')
      try {
        await writeClipboard(blockToText(block))
        toast.success(`${blockTitle(block)} copied`)
      } catch {
        toast.error('That could not be copied to the clipboard.')
      } finally {
        markPending(messageId, null)
      }
    },
    [markPending],
  )

  const handleForward = useCallback((block: ActionableBlock, messageId: string) => {
    setForwardRequest({ messageId, text: blockToText(block), title: blockTitle(block) })
  }, [])

  const handleSendToCustomer = useCallback((block: ActionableBlock, messageId: string) => {
    setSendRequest({ messageId, text: blockToText(block), title: blockTitle(block) })
  }, [])

  const handleRegenerate = useCallback(
    async (block: ActionableBlock, messageId: string) => {
      if (!regenerate) return
      markPending(messageId, 'regenerate')
      try {
        await regenerate(messageId)
        toast.success('Asking Aveline for a fresh take')
      } catch {
        toast.error(`Could not regenerate that ${blockTitle(block).toLowerCase()}.`)
      } finally {
        markPending(messageId, null)
      }
    },
    [markPending, regenerate],
  )

  /**
   * Delivers to one client and reports what the server said.
   *
   * The server's own sentence is preferred over anything the client could invent: "you have not
   * connected WhatsApp" and "that client has no number on file" are different things for the
   * associate to do next, and only the server knows which one happened.
   */
  const deliverTo = useCallback(
    async (messageId: string, targetConversationId: string, text: string, title: string) => {
      markPending(messageId, targetConversationId === conversationId ? 'send_to_customer' : 'forward')
      try {
        await deliver(targetConversationId, text)
        toast.success(`${title} sent`)
        return true
      } catch (error) {
        toast.error(
          error instanceof Error && error.message
            ? error.message
            : 'That could not be sent. Nothing was sent.',
        )
        return false
      } finally {
        markPending(messageId, null)
      }
    },
    [conversationId, deliver, markPending],
  )

  const confirmForward = useCallback(
    async (targetConversationId: string) => {
      if (!forwardRequest) return
      const { messageId, text, title } = forwardRequest
      const sent = await deliverTo(messageId, targetConversationId, text, title)
      if (sent) setForwardRequest(null)
    },
    [deliverTo, forwardRequest],
  )

  const confirmSend = useCallback(async () => {
    if (!sendRequest || !conversationId) return
    const { messageId, text, title } = sendRequest
    setSending(true)
    try {
      const sent = await deliverTo(messageId, conversationId, text, title)
      if (sent) setSendRequest(null)
    } finally {
      setSending(false)
    }
  }, [conversationId, deliverTo, sendRequest])

  const bridge = useMemo<BlockActionBridge>(
    () => ({
      handlers: {
        onCopy: (block, messageId) => void handleCopy(block, messageId),
        onForward: handleForward,
        // Always wired: whether the open thread has a reachable client is a property of the
        // thread, not of the surface, and the rail says why the segment is unavailable rather
        // than hiding the action the block offers.
        onSendToCustomer: handleSendToCustomer,
        onRegenerate: regenerate
          ? (block, messageId) => void handleRegenerate(block, messageId)
          : undefined,
      },
      environment: {
        hasCustomerDestination: Boolean(conversationId && customerReachable),
        hasForwardDestination: targets.length > 0,
        agentBusy,
      },
      pending: (messageId) => pending[messageId] ?? null,
    }),
    [
      agentBusy,
      conversationId,
      customerReachable,
      handleCopy,
      handleForward,
      handleRegenerate,
      handleSendToCustomer,
      pending,
      regenerate,
      targets.length,
    ],
  )

  const dialogs = (
    <>
      <ForwardPickerDialog
        open={forwardRequest !== null}
        onOpenChange={(open) => {
          if (!open) setForwardRequest(null)
        }}
        targets={targets}
        preview={forwardRequest?.text ?? ''}
        onPick={confirmForward}
      />
      <SendToCustomerDialog
        open={sendRequest !== null}
        onOpenChange={(open) => {
          if (!open) setSendRequest(null)
        }}
        customerName={customerName ?? null}
        preview={sendRequest?.text ?? ''}
        sending={sending}
        onConfirm={() => void confirmSend()}
      />
    </>
  )

  return { bridge, dialogs }
}
