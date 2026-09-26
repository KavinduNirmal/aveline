import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { ConversationDto } from '@/types/conversation'
import type { BlockActionBridge } from './blockActions'

/** The rail config the Salon hands to the thread, captured through the mocked thread. */
const captured = vi.hoisted(() => ({ bridge: undefined as BlockActionBridge | undefined }))

const useConversations = vi.fn()

vi.mock('@/contexts/ConversationsContext', () => ({
  useConversations: () => useConversations(),
}))

vi.mock('@/components/conversation/Composer', () => ({
  Composer: () => null,
}))

// The thread itself is covered by its own tests; this file is about what the surface *hands* it.
vi.mock('@/components/conversation/MessageThread', () => ({
  MessageThread: (props: { blockActions?: BlockActionBridge }) => {
    captured.bridge = props.blockActions
    return null
  },
}))

import { SalonPanel } from './SalonPanel'

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

const CLIENT_THREAD = conversation({
  id: 'conv-1',
  customerId: 'u-1',
  customerName: 'Nadia Perera',
})
const CONCIERGE_THREAD = conversation({ id: 'conv-2' })

const deliverToClient = vi.fn().mockResolvedValue(undefined)

function contextValue(conversations: ConversationDto[], activeConversationId: string | null) {
  return {
    conversations,
    activeConversationId,
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
    deliverToClient,
    regenerate: vi.fn(),
    pendingAttachments: {},
    attach: vi.fn(),
    retryAttachment: vi.fn(),
    removeAttachment: vi.fn(),
  }
}

beforeEach(() => {
  captured.bridge = undefined
  deliverToClient.mockClear()
  useConversations.mockReset()
})

describe('what the Salon hands the rail', () => {
  it('offers Send to customer for a thread bound to a reachable client', () => {
    useConversations.mockReturnValue(contextValue([CLIENT_THREAD], 'conv-1'))
    render(<SalonPanel />)

    expect(captured.bridge?.environment.hasCustomerDestination).toBe(true)
    expect(captured.bridge?.handlers.onSendToCustomer).toBeTypeOf('function')
    expect(captured.bridge?.handlers.onForward).toBeTypeOf('function')
  })

  it('refuses Send to customer on the client-less concierge thread', () => {
    useConversations.mockReturnValue(contextValue([CONCIERGE_THREAD], 'conv-2'))
    render(<SalonPanel />)

    // No client on the thread means nowhere to deliver, and the rail says so rather than pointing
    // at whichever client the list happens to hold.
    expect(captured.bridge?.environment.hasCustomerDestination).toBe(false)
  })

  it('offers no forward destination when no other client can be reached', () => {
    useConversations.mockReturnValue(contextValue([CLIENT_THREAD, CONCIERGE_THREAD], 'conv-1'))
    render(<SalonPanel />)

    // The open thread is excluded and the concierge cannot receive a delivery, so there is none.
    expect(captured.bridge?.environment.hasForwardDestination).toBe(false)
  })
})

describe('the full flow: press the rail, confirm, and the client is delivered to', () => {
  it('delivers a suggestion to the open thread\'s client through the context service', async () => {
    useConversations.mockReturnValue(contextValue([CLIENT_THREAD], 'conv-1'))
    render(<SalonPanel />)

    // The rail reports the press, exactly as a segment would.
    await act(async () => {
      captured.bridge?.handlers.onSendToCustomer?.(
        { type: 'suggestion', text: 'We have it in size M.' },
        'msg-1',
      )
    })

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Send to Nadia Perera?')
    expect(deliverToClient).not.toHaveBeenCalled()

    await userEvent.click(within(dialog).getByRole('button', { name: 'Send to customer' }))

    await waitFor(() =>
      expect(deliverToClient).toHaveBeenCalledWith('conv-1', 'We have it in size M.'),
    )
  })

  it('delivers nothing when the associate dismisses the confirmation', async () => {
    useConversations.mockReturnValue(contextValue([CLIENT_THREAD], 'conv-1'))
    render(<SalonPanel />)

    await act(async () => {
      captured.bridge?.handlers.onSendToCustomer?.({ type: 'suggestion', text: 'Hi' }, 'msg-1')
    })
    const dialog = await screen.findByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(deliverToClient).not.toHaveBeenCalled()
  })
})
