import { describe, expect, it } from 'vitest'
import { renderToString } from 'react-dom/server'

import { MessageBubble } from './MessageBubble'
import type { ChatMessage } from '@/contexts/ConversationsContext'

function makeMessage(overrides: Partial<ChatMessage> = {}): ChatMessage {
  return {
    id: 'm1',
    conversationId: 'c1',
    authorKind: 'Agent',
    agentKey: 'aveline',
    authorUserId: null,
    kind: 'Note',
    contentBlocks: [{ type: 'text', text: 'I have asked Ava and Elle.' }],
    contentHash: null,
    replyToMessageId: null,
    status: 'Published',
    createdAt: '2026-09-09T10:00:00Z',
    ...overrides,
  }
}

describe('MessageBubble', () => {
  it('renders an agent message with the persona name', () => {
    const html = renderToString(<MessageBubble message={makeMessage()} isOwn={false} />)
    expect(html).toContain('Aveline')
    expect(html).toContain('I have asked Ava and Elle.')
  })

  it('renders the blossom avatar for Aveline (not a letter)', () => {
    const html = renderToString(<MessageBubble message={makeMessage()} isOwn={false} />)
    expect(html).toContain('aveline-waiting')
    // Aveline's avatar is the blossom, not an "A" initial.
    expect(html).not.toContain('>A</div>')
  })

  it('renders a blossom avatar for a non-Aveline agent (not a letter)', () => {
    const message = makeMessage({ agentKey: 'lina' })
    const html = renderToString(<MessageBubble message={message} isOwn={false} />)
    expect(html).toContain('Lina')
    // Lina's avatar is a blossom in her persona colour, not an "L" initial.
    expect(html).not.toContain('>L</div>')
    expect(html).toContain('text-commerce')
  })

  it('renders a staff message right-aligned', () => {
    const message = makeMessage({
      authorKind: 'User',
      agentKey: null,
      authorUserId: 'u1',
      contentBlocks: [{ type: 'text', text: 'Does anything match?' }],
    })
    const html = renderToString(<MessageBubble message={message} isOwn />)
    expect(html).toContain('Does anything match?')
    expect(html).toContain('flex-row-reverse')
  })

  it('renders a SignOff message with approve/reject when awaiting', () => {
    const message = makeMessage({
      kind: 'SignOff',
      agentKey: 'lina',
      status: 'AwaitingSignOff',
      contentBlocks: [{ type: 'sign_off', reason: 'above limit', amount: 48000 }],
    })
    const onSignOff = () => undefined
    const html = renderToString(
      <MessageBubble message={message} isOwn={false} onSignOff={onSignOff} />,
    )
    expect(html).toContain('Lina')
    expect(html).toContain('Approve')
  })

  it('renders a ClientMessage from the system', () => {
    const message = makeMessage({
      authorKind: 'System',
      agentKey: null,
      kind: 'ClientMessage',
      contentBlocks: [{ type: 'client_message', from: '+94771234567', text: 'Hi' }],
    })
    const html = renderToString(<MessageBubble message={message} isOwn={false} />)
    expect(html).toContain('Customer')
  })

  it('shows a Sending… status for an optimistic staff message', () => {
    const message = makeMessage({
      authorKind: 'User',
      agentKey: null,
      authorUserId: 'u1',
      contentBlocks: [{ type: 'text', text: 'Hello' }],
      pending: 'sending',
    })
    const html = renderToString(<MessageBubble message={message} isOwn />)
    expect(html).toContain('Sending…')
  })

  it('shows a failed status for a failed optimistic message', () => {
    const message = makeMessage({
      authorKind: 'User',
      agentKey: null,
      authorUserId: 'u1',
      contentBlocks: [{ type: 'text', text: 'Hello' }],
      pending: 'failed',
    })
    const html = renderToString(<MessageBubble message={message} isOwn />)
    expect(html).toContain('Failed to send')
  })

  it('shows a Thought for Xs caption on an agent message', () => {
    const message = makeMessage({ thoughtSeconds: 0.84 })
    const html = renderToString(<MessageBubble message={message} isOwn={false} />)
    expect(html).toContain('Thought for 0.84s')
  })
})
