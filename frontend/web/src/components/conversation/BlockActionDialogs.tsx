import { useState } from 'react'

import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { cn } from '@/lib/utils'
import type { DeliveryTarget } from './blockActions'

interface ForwardPickerDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  targets: DeliveryTarget[]
  /** The block's words, quoted back so the associate forwards the right card. */
  preview: string
  /** Resolves once the delivery is stored; the dialog stays open until it does. */
  onPick: (targetConversationId: string) => Promise<void>
}

/**
 * The Forward picker: which client a block goes to.
 *
 * Every row here is a real delivery — the words leave the boutique over that client's own channel
 * — which is why only threads whose client can actually be reached are listed. The concierge
 * thread is not among them: it has no client, so a card forwarded there would reach nobody, and
 * offering it would be offering a send that cannot happen.
 */
export function ForwardPickerDialog({
  open,
  onOpenChange,
  targets,
  preview,
  onPick,
}: ForwardPickerDialogProps) {
  const [picked, setPicked] = useState<string | null>(null)

  const choose = async (targetId: string) => {
    setPicked(targetId)
    try {
      await onPick(targetId)
    } finally {
      setPicked(null)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle className="text-sm">Forward to a client</DialogTitle>
          <DialogDescription className="text-xs">
            The card is sent to that client on their own channel, and recorded in their Salon.
          </DialogDescription>
        </DialogHeader>

        {preview && (
          <p className="line-clamp-3 rounded-lg border bg-muted/40 p-2.5 text-xs text-muted-foreground">
            {preview}
          </p>
        )}

        <div className="max-h-72 space-y-1.5 overflow-y-auto">
          {targets.length === 0 ? (
            <p className="p-2 text-sm text-muted-foreground">
              No other client to forward to yet. A client Salon is created with the client.
            </p>
          ) : (
            targets.map((target) => (
              <button
                key={target.id}
                type="button"
                onClick={() => void choose(target.id)}
                // Every row is disabled while one delivery is in flight, which is what stops a
                // second tap from messaging a second client with the same card.
                disabled={picked !== null}
                aria-busy={picked === target.id || undefined}
                className={cn(
                  'flex w-full items-center justify-between gap-2 rounded-lg border px-3 py-2 text-left text-sm transition-colors',
                  'hover:bg-muted focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-inset focus-visible:outline-none',
                  'disabled:cursor-not-allowed disabled:opacity-60',
                )}
              >
                <span className="min-w-0 truncate font-medium">{target.label}</span>
                <span className="shrink-0 text-xs text-muted-foreground">
                  {picked === target.id
                    ? 'Sending…'
                    : target.lastMessageAt
                      ? new Date(target.lastMessageAt).toLocaleDateString()
                      : 'No messages yet'}
                </span>
              </button>
            ))
          )}
        </div>

        <DialogFooter>
          <Button variant="outline" size="sm" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

interface SendToCustomerDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** The client the open thread belongs to; the sentence says who will receive it. */
  customerName: string | null
  /** The block's words, quoted back before an irreversible send. */
  preview: string
  sending: boolean
  onConfirm: () => void
}

/**
 * The confirmation in front of "Send to customer".
 *
 * This puts the words on the client's own phone, on their own channel, and a message cannot be
 * recalled from this screen, so the last step names the recipient and shows the exact text rather
 * than relying on the associate remembering which Salon is open.
 */
export function SendToCustomerDialog({
  open,
  onOpenChange,
  customerName,
  preview,
  sending,
  onConfirm,
}: SendToCustomerDialogProps) {
  const recipient = customerName?.trim() || 'this client'

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle className="text-sm">Send to {recipient}?</DialogTitle>
          <DialogDescription className="text-xs">
            This is delivered to {recipient} on their own channel. It cannot be unsent.
          </DialogDescription>
        </DialogHeader>

        {preview && (
          <p className="line-clamp-5 whitespace-pre-wrap rounded-lg border bg-muted/40 p-2.5 text-xs">
            {preview}
          </p>
        )}

        <DialogFooter>
          <Button variant="outline" size="sm" onClick={() => onOpenChange(false)} disabled={sending}>
            Cancel
          </Button>
          <Button size="sm" onClick={onConfirm} disabled={sending}>
            {sending ? 'Sending…' : 'Send to customer'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
