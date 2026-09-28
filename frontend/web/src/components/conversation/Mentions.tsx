import { Fragment } from 'react'

import { splitMentions } from '@/lib/mentions'
import { cn } from '@/lib/utils'

/** Which bubble a mention sits in. The pill has to flip with it, as the attachment plate does. */
type MentionTone = 'own' | 'other'

/**
 * The pill's surface, per bubble.
 *
 * A tint of the bubble's own ink would vanish into it: the staff bubble is filled with `primary`,
 * so the pill there is a light plate of that bubble's foreground while the neutral bubbles take the
 * app's accent. The same split, for the same reason, as the attachment plate in `blocks.tsx`.
 */
const MENTION_PILL: Record<MentionTone, string> = {
  own: 'bg-primary-foreground/15 text-primary-foreground ring-primary-foreground/25',
  other: 'bg-primary/10 text-primary ring-primary/20',
}

/** What a pill says on hover: the entity the resolver read from it. */
function mentionTitle(kind: 'customer' | 'phone', value: string): string {
  return kind === 'customer' ? `Customer mention: ${value}` : `Phone mention: ${value}`
}

/**
 * A message's words with its entity mentions lifted into pills (ADR-019).
 *
 * `@Samantha Arias` and `#0771234567` are how staff point the resolver at an exact customer instead
 * of letting it guess from prose. Written as plain text they read as stray punctuation; as a pill
 * they read as the entity the lookup used, which is what they are.
 *
 * The grammar is `@/lib/mentions`, a mirror of the resolver's own parser, so a pill covers exactly
 * the span the resolver read — the marker plus the captured name or number, and not the prose the
 * greedy capture dropped.
 */
export function MentionText({
  text,
  tone = 'other',
  className,
}: {
  text: string
  tone?: MentionTone
  className?: string
}) {
  return (
    <p className={className}>
      {splitMentions(text).map((segment, i) =>
        segment.kind === 'text' ? (
          <Fragment key={i}>{segment.text}</Fragment>
        ) : (
          <span
            key={i}
            data-slot="mention"
            data-mention-kind={segment.mention.kind}
            title={mentionTitle(segment.mention.kind, segment.mention.value)}
            className={cn(
              // A plain inline box, not `inline-flex`: a flex container cannot break across lines,
              // and the greedy name capture means a mention can be longer than the bubble is wide.
              // `box-decoration-clone` gives each line fragment its own rounded plate, and the
              // padding paints without moving the line box, so a mention never disturbs the rhythm
              // of the paragraph it sits in.
              'rounded-full box-decoration-clone px-1.5 py-0.5 font-medium ring-1 ring-inset',
              MENTION_PILL[tone],
              segment.mention.kind === 'phone' && 'tabular-nums',
            )}
          >
            {segment.mention.marker + segment.mention.value}
          </span>
        ),
      )}
    </p>
  )
}
