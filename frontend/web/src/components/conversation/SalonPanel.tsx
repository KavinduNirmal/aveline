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
import { isGeneralSalon, salonLabel, sortSalons } from './salonLabel'

/**
 * The identity mark for a Salon. Aveline's own thread uses the blossom; a client's thread uses the
 * client's initial, because the blossom in a client's header reads as "you are talking to Aveline"
 * when the thread is that client's.
 */
export function SalonAvatar({
  conversation,
  className = 'size-10',
}: {
  conversation: ConversationDto
  className?: string
}) {
  if (isGeneralSalon(conversation)) {
    return <AvelineAvatar className={className} blossomClassName="size-8" />
  }
  return (
    <div
      className={cn(
        'flex shrink-0 items-center justify-center rounded-full bg-primary/10 font-semibold text-primary',
        className,
      )}
      aria-label={`${salonLabel(conversation)} avatar`}
      role="img"
    >
      {salonLabel(conversation).charAt(0).toUpperCase()}
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
    selectCustomer,
    pendingAttachments,
    attach,
    retryAttachment,
    removeAttachment,
  } = useConversations()

  const activeConversation = conversations.find((c) => c.id === activeConversationId)
  // The tray is keyed by conversation in the context, so a section switch does not orphan it.
  const pendingForActive = activeConversationId
    ? (pendingAttachments[activeConversationId] ?? [])
    : []
  const headerTitle = activeConversation ? salonLabel(activeConversation) : 'Salon'
  const headerSubtitle = activeConversation
    ? isGeneralSalon(activeConversation)
      ? 'Shared concierge thread'
      : avelineStateConfig(agentState).label
    : avelineStateConfig(agentState).label
  // The general thread is pinned first; the rest keep the server's newest-first order.
  const listedConversations = sortSalons(conversations)

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
            listedConversations.map((conversation) => {
              const general = isGeneralSalon(conversation)
              return (
                <button
                  key={conversation.id}
                  type="button"
                  // Named for the row's own subject, so a screen reader says whose thread it opens.
                  aria-label={`Open salon ${salonLabel(conversation)}`}
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
                    <div className="flex items-center gap-1.5">
                      <p className="truncate text-sm font-medium">
                        {salonLabel(conversation)}
                      </p>
                      {/* The general thread is marked, so it is never mistaken for a client's. */}
                      {general ? (
                        <span className="shrink-0 rounded-full bg-primary/10 px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-primary">
                          Concierge
                        </span>
                      ) : null}
                    </div>
                    <p className="truncate text-xs text-muted-foreground">
                      {conversation.lastMessageAt
                        ? new Date(conversation.lastMessageAt).toLocaleString()
                        : 'No messages yet'}
                    </p>
                  </div>
                </button>
              )
            })
          )}
        </div>
      </aside>

      {/* Thread */}
      <section className="flex min-h-0 min-w-0 flex-col">
        <Card className="flex h-full flex-col overflow-hidden rounded-none border-0 py-0 shadow-none">
          {/* Chatroom header */}
          <header className="flex h-14 items-center gap-2.5 border-b px-4">
            {/* The thread's own identity: a client's thread shows the client, not Aveline. */}
            {activeConversation ? (
              <SalonAvatar conversation={activeConversation} />
            ) : (
              <AvelineAvatar state={agentState} className="size-10" blossomClassName="size-8" />
            )}
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
              onSelectCustomer={(customerId) => void selectCustomer(customerId)}
            />
          </div>
          <Composer
            onSend={(text, attachmentIds) => send(text, attachmentIds)}
            onAttach={(files) =>
              activeConversationId ? attach(activeConversationId, files) : Promise.resolve([])
            }
            pendingAttachments={pendingForActive}
            onRetryAttachment={(attachmentId) => {
              if (activeConversationId) void retryAttachment(activeConversationId, attachmentId)
            }}
            onRemoveAttachment={(attachmentId) => {
              if (activeConversationId) removeAttachment(activeConversationId, attachmentId)
            }}
            disabled={!activeConversationId}
            sending={sending}
          />
        </Card>
      </section>
    </div>
  )
}
