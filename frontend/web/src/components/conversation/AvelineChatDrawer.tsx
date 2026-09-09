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
 * positioning) so it spans the full viewport height. Shares the same Salon thread as the
 * full Salon tab via the shared ConversationsContext.
 */
export function AvelineChatDrawer({ open, onClose }: AvelineChatDrawerProps) {
  const {
    activeConversationId,
    messages,
    loading,
    sending,
    agentState,
    agentActivity,
    openOrCreateSalon,
    send,
    decide,
    selectCustomer,
  } = useConversations()

  // Ensure a Salon is open so the drawer has somewhere to send.
  useEffect(() => {
    if (open && !activeConversationId) {
      void openOrCreateSalon(null)
    }
  }, [activeConversationId, open, openOrCreateSalon])

  const stateConfig = avelineStateConfig(agentState)

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
            state={agentState}
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
          messages={messages}
          loading={loading && !activeConversationId}
          agentActivity={agentActivity}
          onSignOff={(messageId, approved) => void decide(messageId, approved)}
          onSelectCustomer={(customerId) => void selectCustomer(customerId)}
        />
      </div>

      <Composer
        onSend={(text) => void send(text)}
        disabled={!activeConversationId}
        sending={sending}
        placeholder="Ask Aveline…"
      />
    </div>
  )
}

