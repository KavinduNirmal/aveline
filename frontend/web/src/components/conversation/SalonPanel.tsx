import { MessageSquarePlus } from 'lucide-react'

import { AvelineAvatar } from '@/components/conversation/AvelineAvatar'
import { Composer } from '@/components/conversation/Composer'
import { MessageThread } from '@/components/conversation/MessageThread'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { useConversations } from '@/contexts/ConversationsContext'
import { cn } from '@/lib/utils'
import type { ConversationDto } from '@/types/conversation'
import { avelineStateConfig } from './avelineStates'

/** The display name for a salon: just the name, no "Salon" suffix. */
function salonName(conversation: ConversationDto): string {
  return conversation.customerId ? 'Customer' : 'Aveline'
}

/** The avatar shown for a salon row. Aveline uses the blossom; customers use an initial. */
function SalonAvatar({ conversation }: { conversation: ConversationDto }) {
  if (!conversation.customerId) {
    return <AvelineAvatar className="size-10" blossomClassName="size-8" />
  }
  return (
    <div className="flex size-10 shrink-0 items-center justify-center rounded-full bg-muted text-sm font-semibold text-muted-foreground">
      C
    </div>
  )
}

/**
 * The full Salon view shown in the dashboard's Salon tab. A conversation list on the left
 * and the selected thread on the right. Shares the same context as the floating Aveline
 * chat drawer, so both always reflect the same thread.
 */
export function SalonPanel() {
  const {
    conversations,
    activeConversationId,
    messages,
    loading,
    sending,
    agentState,
    agentActivity,
    openConversation,
    openOrCreateSalon,
    send,
    decide,
  } = useConversations()

  const activeConversation = conversations.find((c) => c.id === activeConversationId)
  const headerTitle = activeConversation ? salonName(activeConversation) : 'Salon'
  const headerSubtitle = avelineStateConfig(agentState).label

  return (
    <div className="grid h-[calc(100vh_-_8rem)] grid-cols-[280px_1fr] overflow-hidden rounded-xl border bg-background">
      {/* Conversation list */}
      <aside className="flex flex-col border-r">
        <div className="flex h-14 items-center justify-between border-b px-3">
          <h2 className="font-serif text-base font-medium">Salons</h2>
          <Button
            size="sm"
            variant="ghost"
            onClick={() => void openOrCreateSalon(null)}
            aria-label="New salon"
          >
            <MessageSquarePlus className="size-4" aria-hidden />
          </Button>
        </div>
        <div className="flex-1 overflow-y-auto p-2">
          {loading ? (
            <div className="space-y-2 p-2">
              {[0, 1, 2].map((i) => <Skeleton key={i} className="h-14 rounded-lg" />)}
            </div>
          ) : conversations.length === 0 ? (
            <p className="p-3 text-sm text-muted-foreground">
              No salons yet. Start a conversation with Aveline.
            </p>
          ) : (
            conversations.map((conversation) => (
              <button
                key={conversation.id}
                type="button"
                onClick={() => void openConversation(conversation.id)}
                className={cn(
                  'mb-1 flex w-full items-center gap-2.5 rounded-lg px-2.5 py-2 text-left transition-colors',
                  activeConversationId === conversation.id
                    ? 'bg-primary/10 text-primary'
                    : 'hover:bg-muted',
                )}
              >
                <SalonAvatar conversation={conversation} />
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium">{salonName(conversation)}</p>
                  <p className="truncate text-xs text-muted-foreground">
                    {conversation.lastMessageAt
                      ? new Date(conversation.lastMessageAt).toLocaleString()
                      : 'No messages yet'}
                  </p>
                </div>
              </button>
            ))
          )}
        </div>
      </aside>

      {/* Thread */}
      <section className="flex min-h-0 min-w-0 flex-col">
        <Card className="flex h-full flex-col overflow-hidden rounded-none border-0 shadow-none">
          {/* Chatroom header */}
          <header className="flex h-14 items-center gap-2.5 border-b px-4">
            <AvelineAvatar
              state={agentState}
              className="size-10"
              blossomClassName="size-8"
            />
            <div className="min-w-0">
              <p className="truncate font-serif text-sm font-medium leading-tight">
                {headerTitle}
              </p>
              <p className="truncate text-[11px] text-muted-foreground">{headerSubtitle}</p>
            </div>
          </header>

          <div className="flex-1 overflow-y-auto">
            <MessageThread
              messages={messages}
              loading={loading && !activeConversationId}
              agentActivity={agentActivity}
              onSignOff={(messageId, approved) => void decide(messageId, approved)}
            />
          </div>
          <Composer
            onSend={(text) => void send(text)}
            disabled={!activeConversationId}
            sending={sending}
          />
        </Card>
      </section>
    </div>
  )
}
