import { cn } from '@/lib/utils'
import type { ChatMessage } from '@/contexts/ConversationsContext'
import { AvelineAvatar } from './AvelineAvatar'
import { BlockList } from './blocks'
import { personaForAuthor } from './persona'
import { TypewriterText } from './TypewriterText'

interface MessageBubbleProps {
  message: ChatMessage
  /** Whether this message is from the current staff user (right-aligned). */
  isOwn: boolean
  onSignOff?: (approved: boolean) => void
  /** Called as a streamed message types out, so the thread can keep the tail in view. */
  onStreamProgress?: () => void
}

function timeLabel(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''
  return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

/** Extracts the first `text` block's content, or null when the message has none. */
function primaryText(message: ChatMessage): string | null {
  for (const block of message.contentBlocks ?? []) {
    if (
      block &&
      typeof block === 'object' &&
      (block as { type?: string }).type === 'text' &&
      typeof (block as { text?: unknown }).text === 'string'
    ) {
      return (block as { text: string }).text
    }
  }
  return null
}

/** The avatar shown for an agent message. Aveline uses the blossom; others use an initial. */
function AgentAvatar({ persona }: { persona: { name: string; bg: string; ring: string } }) {
  if (persona.name === 'Aveline') {
    return <AvelineAvatar className="size-8" blossomClassName="size-5" />
  }
  return (
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
  )
}

/** Formats a "Thought for Xs" caption from the elapsed seconds. */
function thoughtLabel(seconds: number): string {
  return `Thought for ${seconds.toFixed(2)}s`
}

/**
 * A single message bubble in the Salon. Agent messages are attributed to their persona
 * (Aveline/Ava/Elle/Lina) with the persona accent; staff messages align right. Optimistic
 * staff messages show a `Sending…`/`Failed` status until confirmed; agent messages may carry
 * a `thoughtSeconds` caption.
 */
export function MessageBubble({
  message,
  isOwn,
  onSignOff,
  onStreamProgress,
}: MessageBubbleProps) {
  const persona = personaForAuthor(message.authorKind, message.agentKey)
  const isAgent = message.authorKind === 'Agent'
  const isSending = message.pending === 'sending'
  const isFailed = message.pending === 'failed'
  const streamText = message.streamIn ? primaryText(message) : null

  return (
    <div className={cn('flex w-full gap-2.5', isOwn && 'flex-row-reverse')}>
      {persona && <AgentAvatar persona={persona} />}

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
            isFailed && 'border-destructive/40 bg-destructive/5',
          )}
        >
          {streamText !== null ? (
            <TypewriterText text={streamText} onProgress={onStreamProgress} />
          ) : (
            <BlockList blocks={message.contentBlocks} onSignOff={onSignOff} />
          )}
        </div>

        {/* Footer: status / timestamp / thought caption. */}
        <div
          className={cn(
            'flex items-center gap-1.5 px-1 text-[10px] text-muted-foreground',
            isOwn && 'flex-row-reverse',
          )}
        >
          {isSending && (
            <span className="inline-flex items-center gap-1 text-muted-foreground">
              <span className="size-1 animate-pulse rounded-full bg-current" />
              Sending…
            </span>
          )}
          {isFailed && <span className="text-destructive">Failed to send</span>}
          {!isSending && !isFailed && timeLabel(message.createdAt)}
          {isAgent && typeof message.thoughtSeconds === 'number' && (
            <span className="italic opacity-70">{thoughtLabel(message.thoughtSeconds)}</span>
          )}
        </div>
      </div>
    </div>
  )
}
