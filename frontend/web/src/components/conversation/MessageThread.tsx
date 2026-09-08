import { useEffect, useRef } from 'react'

import { Skeleton } from '@/components/ui/skeleton'
import type { MessageDto } from '@/types/conversation'
import { MessageBubble } from './MessageBubble'

interface MessageThreadProps {
  messages: MessageDto[]
  loading?: boolean
  onSignOff?: (messageId: string, approved: boolean) => void
}

/**
 * The scrollable message list for a Salon. Auto-scrolls to the newest message on change.
 * Staff-authored messages (the current user composing) align right; agent and system
 * messages align left with their persona accent.
 */
export function MessageThread({
  messages,
  loading,
  onSignOff,
}: MessageThreadProps) {
  const bottomRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages.length])

  if (loading) {
    return (
      <div className="space-y-4 p-4">
        {[0, 1, 2].map((i) => (
          <div key={i} className="flex gap-2.5">
            <Skeleton className="size-8 rounded-full" />
            <Skeleton className="h-16 w-2/3 rounded-2xl" />
          </div>
        ))}
      </div>
    )
  }

  if (messages.length === 0) {
    return (
      <div className="flex h-full flex-col items-center justify-center gap-2 p-6 text-center">
        <p className="font-serif text-lg text-muted-foreground">The Salon is quiet</p>
        <p className="max-w-xs text-sm text-muted-foreground/70">
          Ask Aveline anything about a customer, a piece, or a price.
        </p>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-3 p-4">
      {messages.map((message) => (
        <MessageBubble
          key={message.id}
          message={message}
          isOwn={message.authorKind === 'User'}
          onSignOff={
            message.kind === 'SignOff' && message.status === 'AwaitingSignOff'
              ? (approved) => onSignOff?.(message.id, approved)
              : undefined
          }
        />
      ))}
      <div ref={bottomRef} />
    </div>
  )
}
