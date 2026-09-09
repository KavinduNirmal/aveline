import { Button } from '@/components/ui/button'
import { useConversations } from '@/contexts/ConversationsContext'
import { cn } from '@/lib/utils'
import { AvelineAvatar } from './AvelineAvatar'

interface AvelineChatLauncherProps {
  open: boolean
  onOpen: () => void
}

/**
 * The Aveline chat launcher shown in the dashboard header. It is a primary call-to-action,
 * so the Blossom mark is always in motion (colour cycle + slow rotation). When Aveline is
 * processing a reply (`waiting`) the blossom visibly "thinks".
 */
export function AvelineChatLauncher({ open, onOpen }: AvelineChatLauncherProps) {
  const { agentState } = useConversations()

  return (
    <Button
      variant="ghost"
      size="icon"
      onClick={onOpen}
      aria-label={open ? 'Aveline chat is open' : 'Open Aveline chat'}
      title="Aveline"
      className={cn(
        'relative size-9 rounded-full',
        open && 'bg-primary/10',
      )}
    >
      <AvelineAvatar
        state={agentState}
        className="size-9"
        blossomClassName="size-5"
      />
    </Button>
  )
}
