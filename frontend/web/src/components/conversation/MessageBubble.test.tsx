import { describe, expect, it } from 'vitest'

import { shouldStreamContent } from './MessageBubble'
import type { ChatMessage } from '@/contexts/ConversationsContext'

function agentMessage(contentBlocks: unknown[], streamIn = true): ChatMessage {
  return {
    id: 'm1',
    conversationId: 'c1',
    authorKind: 'Agent',
    agentKey: 'ava',
    authorUserId: null,
    kind: 'Note',
    contentBlocks: contentBlocks as ChatMessage['contentBlocks'],
    contentHash: null,
    replyToMessageId: null,
    status: 'Published',
    createdAt: new Date().toISOString(),
    streamIn,
  }
}

describe('shouldStreamContent', () => {
  it('streams a purely-text live message', () => {
    expect(shouldStreamContent(agentMessage([{ type: 'text', text: 'hello' }]))).toBe(true)
  })

  it('does not stream a rich multi-block message (text + suggestion)', () => {
    expect(
      shouldStreamContent(
        agentMessage([
          { type: 'text', text: 'Sarah Perera (new)' },
          { type: 'suggestion', text: 'Good afternoon!' },
        ]),
      ),
    ).toBe(false)
  })

  it('does not stream a table card (at_a_glance)', () => {
    expect(
      shouldStreamContent(
        agentMessage([{ type: 'at_a_glance', columns: ['Category'], rows: [['preference', 'silk']] }]),
      ),
    ).toBe(false)
  })

  it('never streams when not flagged live (e.g. reloaded history)', () => {
    expect(
      shouldStreamContent(agentMessage([{ type: 'text', text: 'hi' }], false)),
    ).toBe(false)
  })

  it('never streams an empty message', () => {
    expect(shouldStreamContent(agentMessage([]))).toBe(false)
  })
})
