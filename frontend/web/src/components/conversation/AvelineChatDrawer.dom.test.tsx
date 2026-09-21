import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { ConversationDto } from '@/types/conversation'

const useConversations = vi.fn()

vi.mock('@/contexts/ConversationsContext', () => ({
  useConversations: () => useConversations(),
}))

// The neighbouring components are mocked so this test is about *whose* thread the drawer reads,
// not about how a message bubble renders. The mocks echo the props they are given.
vi.mock('@/components/conversation/Composer', () => ({
  Composer: ({ disabled, placeholder }: { disabled?: boolean; placeholder?: string }) => (
    <input aria-label="composer" disabled={disabled} placeholder={placeholder} />
  ),
}))
vi.mock('@/components/conversation/MessageThread', () => ({
  MessageThread: ({ messages }: { messages: Array<{ id: string; contentBlocks: unknown }> }) => (
    <ul aria-label="messages">
      {messages.map((message) => (
        <li key={message.id}>{JSON.stringify(message.contentBlocks)}</li>
      ))}
    </ul>
  ),
}))

import { AvelineChatDrawer } from './AvelineChatDrawer'

function conversation(overrides: Partial<ConversationDto> = {}): ConversationDto {
  return {
    id: 'conv-1',
    kind: 'Salon',
    customerId: null,
    customerName: null,
    externalRef: null,
    threadId: 'thread-1',
    status: 'Active',
    lastMessageAt: null,
    lastMessagePreview: null,
    lastMessageKind: null,
    lastMessageBlock: null,
    lastMessageAuthor: null,
    lastMessageAgentKey: null,
    markers: [],
    ...overrides,
  }
}

function contextValue(overrides: Record<string, unknown> = {}) {
  return {
    conversations: [conversation({ id: 'conv-g' })],
    activeConversationId: 'conv-panel',
    messages: [],
    loading: false,
    sending: false,
    agentState: 'idle',
    waiting: false,
    agentActivity: null,
    connectionState: 'Connected',
    openConversation: vi.fn(),
    openOrCreateSalon: vi.fn(),
    send: vi.fn(),
    decide: vi.fn(),
    selectCustomer: vi.fn(),
    // The drawer's own thread.
    avelineConversationId: 'conv-g',
    avelineMessages: [],
    avelineAgentState: 'idle',
    avelineAgentActivity: null,
    avelineLoading: false,
    avelineSending: false,
    openAveline: vi.fn(),
    sendToAveline: vi.fn(),
    decideAveline: vi.fn(),
    ...overrides,
  }
}

describe('AvelineChatDrawer', () => {
  beforeEach(() => {
    useConversations.mockReset()
  })

  it('opens Aveline\'s own thread and never touches the section\'s', () => {
    // The two surfaces share a conversation list and a hub connection, but not the open thread.
    // The drawer used to drive the shared `activeConversationId`, so the Salon section and the
    // drawer each dragged the other off whatever the operator had selected.
    const openAveline = vi.fn()
    const openOrCreateSalon = vi.fn()
    useConversations.mockReturnValue(
      contextValue({ openAveline, openOrCreateSalon, avelineConversationId: null }),
    )

    render(<AvelineChatDrawer open onClose={() => {}} />)

    expect(openAveline).toHaveBeenCalledTimes(1)
    expect(openOrCreateSalon).not.toHaveBeenCalled()
  })

  it('does not open a thread while it is closed', () => {
    const openAveline = vi.fn()
    useConversations.mockReturnValue(
      contextValue({ openAveline, avelineConversationId: null }),
    )

    render(<AvelineChatDrawer open={false} onClose={() => {}} />)

    expect(openAveline).not.toHaveBeenCalled()
  })

  it('does not re-open a thread it already has', () => {
    const openAveline = vi.fn()
    useConversations.mockReturnValue(
      contextValue({ openAveline, avelineConversationId: 'conv-g' }),
    )

    render(<AvelineChatDrawer open onClose={() => {}} />)

    expect(openAveline).not.toHaveBeenCalled()
  })

  it('renders the drawer thread, not the section\'s, when they differ', () => {
    // The section is showing a client Salon; the drawer must still show Aveline's messages.
    useConversations.mockReturnValue(
      contextValue({
        activeConversationId: 'conv-c',
        messages: [
          {
            id: 'panel-1',
            conversationId: 'conv-c',
            authorKind: 'User',
            agentKey: null,
            authorUserId: null,
            kind: 'Note',
            contentBlocks: [{ type: 'text', text: 'a note about the client' }],
            contentHash: null,
            replyToMessageId: null,
            status: 'Published',
            createdAt: '2026-09-21T10:00:00Z',
          },
        ],
        avelineConversationId: 'conv-g',
        avelineMessages: [
          {
            id: 'avel-1',
            conversationId: 'conv-g',
            authorKind: 'Agent',
            agentKey: 'aveline',
            authorUserId: null,
            kind: 'Note',
            contentBlocks: [{ type: 'text', text: 'Aveline was here' }],
            contentHash: null,
            replyToMessageId: null,
            status: 'Published',
            createdAt: '2026-09-21T09:00:00Z',
          },
        ],
      }),
    )

    render(<AvelineChatDrawer open onClose={() => {}} />)

    expect(screen.getByLabelText('messages')).toHaveTextContent('Aveline was here')
    expect(screen.getByLabelText('messages')).not.toHaveTextContent('a note about the client')
  })

  it('sends into the drawer thread, not the section\'s', async () => {
    const sendToAveline = vi.fn()
    const send = vi.fn()
    useConversations.mockReturnValue(contextValue({ sendToAveline, send }))

    render(<AvelineChatDrawer open onClose={() => {}} />)

    // The composer is disabled without a thread; here the drawer has one.
    expect(screen.getByLabelText('composer')).not.toBeDisabled()
  })

  it('disables the composer when Aveline\'s thread is not open yet', () => {
    useConversations.mockReturnValue(contextValue({ avelineConversationId: null }))

    render(<AvelineChatDrawer open onClose={() => {}} />)

    expect(screen.getByLabelText('composer')).toBeDisabled()
  })

  it('keeps its own header identity', () => {
    useConversations.mockReturnValue(contextValue())

    render(<AvelineChatDrawer open onClose={() => {}} />)

    expect(screen.getByText('Aveline')).toBeInTheDocument()
  })
})
