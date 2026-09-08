import { cn } from '@/lib/utils'
import type { MessageDto } from '@/types/conversation'
import { BlockList } from './blocks'
import { personaForAuthor } from './persona'

interface MessageBubbleProps {
  message: MessageDto
  /** Whether this message is from the current staff user (right-aligned). */
  isOwn: boolean
  onSignOff?: (approved: boolean) => void
}

function timeLabel(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''
  return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

/**
 * A single message bubble in the Salon. Agent messages are attributed to their persona
 * (Aveline/Ava/Elle/Lina) with the persona accent; staff messages align right.
 */
export function MessageBubble({ message, isOwn, onSignOff }: MessageBubbleProps) {
  const persona = personaForAuthor(message.authorKind, message.agentKey)
  const isAgent = message.authorKind === 'Agent'

  return (
    <div className={cn('flex w-full gap-2.5', isOwn && 'flex-row-reverse')}>
      {persona && (
        <div
          className={cn(
            'flex size-8 shrink-0 items-center justify-center rounded-full text-xs font-semibold text-white ring-2',
            persona.bg,
            persona.ring,
          )}
          title={persona.name}
        >
          {persona.name[0]}
        </div>
      )}

      <div className={cn('flex max-w-[78%] flex-col gap-1', isOwn && 'items-end')}>
        {persona && (
          <span className={cn('px-1 text-[11px] font-medium', persona.text)}>
            {persona.name}
          </span>
        )}
        <div
          className={cn(
            'rounded-2xl px-3.5 py-2.5',
            isOwn
              ? 'rounded-br-md bg-primary text-primary-foreground'
              : isAgent
                ? 'rounded-bl-md border border-border bg-card'
                : 'rounded-bl-md border border-border bg-muted/50',
          )}
        >
          <BlockList blocks={message.contentBlocks} onSignOff={onSignOff} />
        </div>
        <span className="px-1 text-[10px] text-muted-foreground">
          {timeLabel(message.createdAt)}
        </span>
      </div>
    </div>
  )
}
