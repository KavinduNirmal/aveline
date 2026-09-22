import { X } from 'lucide-react'
import { useEffect } from 'react'

import { AvelineAvatar } from '@/components/conversation/AvelineAvatar'
import { Composer } from '@/components/conversation/Composer'
import { MessageThread } from '@/components/conversation/MessageThread'
import { Button } from '@/components/ui/button'
import { useConversations } from '@/contexts/ConversationsContext'
import { cn } from '@/lib/utils'
import { avelineStateConfig } from './avelineStates'

interface AvelineChatDrawerProps {
  open: boolean
  onClose: () => void
}

/**
 * The slide-in Aveline chat panel. Rendered at the shell root (NOT inside the header,
 * whose backdrop-blur would otherwise become the containing block for `fixed`
 * positioning) so it spans the full viewport height.
 *
 * This is **Aveline's own thread and only that**. It shares the conversation list and the SignalR
 * connection with the Salon section, but not the open thread: while both surfaces read one
 * `activeConversationId`, opening a client's Salon in the section turned this panel into that
 * client's chat, and pinning this panel to Aveline moved the section off whatever the operator had
 * selected. The two now hold separate thread slots, so neither can move the other.
 */
export function AvelineChatDrawer({ open, onClose }: AvelineChatDrawerProps) {
  const {
    avelineConversationId,
    avelineMessages,
    avelineLoading,
    avelineSending,
    avelineAgentState,
    avelineAgentActivity,
    openAveline,
    sendToAveline,
    decideAveline,
  } = useConversations()

  // Opening the drawer opens Aveline's thread, once. Guarding on the id rather than only on `open`
  // keeps this from re-opening (and re-fetching) when `openAveline` is re-created by a conversation
  // list update.
  useEffect(() => {
    if (open && !avelineConversationId) {
      void openAveline()
    }
  }, [avelineConversationId, open, openAveline])

  const stateConfig = avelineStateConfig(avelineAgentState)

  return (
    <div
      className={cn(
        'fixed inset-y-0 right-0 z-50 flex w-[380px] max-w-[90vw] flex-col border-l bg-background shadow-2xl transition-transform duration-300',
        open ? 'translate-x-0' : 'translate-x-full',
      )}
      role="dialog"
      aria-label="Aveline chat"
      aria-hidden={!open}
    >
      <header className="flex items-center justify-between border-b px-4 py-3">
        <div className="flex items-center gap-2.5">
          <AvelineAvatar
            state={avelineAgentState}
            className="size-8"
            blossomClassName="size-5"
          />
          <div>
            <p className="font-serif text-sm font-medium leading-tight">Aveline</p>
            <p className="text-[11px] text-muted-foreground">{stateConfig.label}</p>
          </div>
        </div>
        <Button size="icon" variant="ghost" onClick={onClose} aria-label="Close chat">
          <X className="size-4" aria-hidden />
        </Button>
      </header>

      <div className="flex-1 overflow-y-auto">
        <MessageThread
          messages={avelineMessages}
          loading={avelineLoading && !avelineConversationId}
          agentActivity={avelineAgentActivity}
          onSignOff={(messageId, approved) => void decideAveline(messageId, approved)}
        />
      </div>

      <Composer
        onSend={(text) => void sendToAveline(text)}
        disabled={!avelineConversationId}
        sending={avelineSending}
        placeholder="Ask Aveline…"
      />
    </div>
  )
}
