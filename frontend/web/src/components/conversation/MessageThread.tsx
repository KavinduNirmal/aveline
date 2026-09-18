import { AnimatePresence, motion } from 'motion/react'
import { useEffect, useRef } from 'react'

import { Skeleton } from '@/components/ui/skeleton'
import type { AgentActivity, ChatMessage } from '@/contexts/ConversationsContext'
import { AgentActivityBubble } from './AgentActivityBubble'
import { MessageBubble } from './MessageBubble'

interface MessageThreadProps {
  messages: ChatMessage[]
  loading?: boolean
  /** Aveline's in-progress reasoning, rendered as a live bubble while non-null. */
  agentActivity?: AgentActivity | null
  onSignOff?: (messageId: string, approved: boolean) => void
  onSelectCustomer?: (customerId: string) => void
}

/**
 * The scrollable message list for a Salon. Auto-scrolls to the newest message on change.
 * Staff-authored messages (the current user composing) align right; agent and system
 * messages align left with their persona accent. New bubbles fade + slide in; a live
 * "Aveline is working" bubble appears while a reply is being produced.
 */
export function MessageThread({
  messages,
  loading,
  agentActivity,
  onSignOff,
  onSelectCustomer,
}: MessageThreadProps) {
  const bottomRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages.length, agentActivity?.currentState])

  // Keep the newest streamed text in view as it types out word by word.
  const handleStreamProgress = () => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }

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

  if (messages.length === 0 && !agentActivity) {
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
        <motion.div
          key={message.id}
          initial={{ opacity: 0, y: 10 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.25, ease: 'easeOut' }}
        >
          <MessageBubble
            message={message}
            isOwn={message.authorKind === 'User'}
            onStreamProgress={handleStreamProgress}
            onSelectCustomer={onSelectCustomer}
            onSignOff={
              message.kind === 'SignOff' && message.status === 'AwaitingSignOff'
                ? (approved) => onSignOff?.(message.id, approved)
                : undefined
            }
          />
        </motion.div>
      ))}

      <AnimatePresence>
        {agentActivity && (
          <motion.div
            key="agent-activity"
            initial={{ opacity: 0, y: 10 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: 6 }}
            transition={{ duration: 0.2 }}
          >
            <AgentActivityBubble state={agentActivity.currentState} />
          </motion.div>
        )}
      </AnimatePresence>

      <div ref={bottomRef} />
    </div>
  )
}
