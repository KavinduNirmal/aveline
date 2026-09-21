import { render, screen, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { ConversationDto } from '@/types/conversation'

const useConversations = vi.fn()

vi.mock('@/contexts/ConversationsContext', () => ({
  useConversations: () => useConversations(),
}))

vi.mock('@/components/conversation/Composer', () => ({
  Composer: () => null,
}))

vi.mock('@/components/conversation/MessageThread', () => ({
  MessageThread: () => null,
}))

import { SalonPanel } from './SalonPanel'
import { isGeneralSalon, salonLabel, sortSalons } from './salonLabel'

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

function contextValue(
  conversations: ConversationDto[],
  overrides: Record<string, unknown> = {},
) {
  return {
    ...overrides,
    conversations,
    activeConversationId: null,
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
  }
}

describe('salonLabel', () => {
  it('names a customer Salon after the customer', () => {
    // The client's name is on the wire (`ConversationDto.customerName`); the panel used to print the
    // literal word "Customer" for every customer thread regardless.
    expect(
      salonLabel(conversation({ customerId: 'c-1', customerName: 'Samantha Arias' })),
    ).toBe('Samantha Arias')
  })

  it('says which number an unidentified inbound thread came from', () => {
    expect(
      salonLabel(conversation({ customerId: null, externalRef: '+94771234567' })),
    ).toBe('+94771234567')
  })

  it('falls back without asserting a name it does not have', () => {
    // No name and no phone: the row must not claim a customer called "Customer".
    expect(salonLabel(conversation({ customerId: 'c-1', customerName: null }))).toBe(
      'Unnamed client',
    )
  })

  it('calls the general, customer-less Salon "Aveline"', () => {
    expect(salonLabel(conversation())).toBe('Aveline')
  })

  it('ignores a blank name rather than rendering an empty row', () => {
    expect(salonLabel(conversation({ customerId: 'c-1', customerName: '   ' }))).toBe(
      'Unnamed client',
    )
  })
})

describe('isGeneralSalon', () => {
  it('is true only for the customer-less, channel-less concierge thread', () => {
    expect(isGeneralSalon(conversation())).toBe(true)
    expect(isGeneralSalon(conversation({ customerId: 'c-1' }))).toBe(false)
    expect(isGeneralSalon(conversation({ externalRef: '+94771234567' }))).toBe(false)
  })
})

describe('sortSalons', () => {
  it('pins the general Aveline Salon above the client Salons', () => {
    // "Pinned to the top" is the request: the concierge thread is the one every member has, and it
    // is the entry point for asking about a client, so it must not drift down as clients accumulate.
    const customer = conversation({
      id: 'conv-c',
      customerId: 'c-1',
      customerName: 'Samantha Arias',
      lastMessageAt: '2026-09-21T09:00:00Z',
    })
    const general = conversation({ id: 'conv-g', lastMessageAt: '2026-09-01T09:00:00Z' })

    const sorted = sortSalons([customer, general])

    expect(sorted.map((c) => c.id)).toEqual(['conv-g', 'conv-c'])
  })

  it('keeps the server order among client Salons', () => {
    const first = conversation({
      id: 'conv-1',
      customerId: 'c-1',
      customerName: 'A',
      lastMessageAt: '2026-09-21T09:00:00Z',
    })
    const second = conversation({
      id: 'conv-2',
      customerId: 'c-2',
      customerName: 'B',
      lastMessageAt: '2026-09-20T09:00:00Z',
    })

    // The list arrives newest-first; the pin must not reorder the rest.
    expect(sortSalons([first, second]).map((c) => c.id)).toEqual(['conv-1', 'conv-2'])
  })

  it('does not mutate the list it is given', () => {
    const list = [conversation({ id: 'conv-c', customerId: 'c-1' }), conversation({ id: 'conv-g' })]
    sortSalons(list)
    expect(list.map((c) => c.id)).toEqual(['conv-c', 'conv-g'])
  })
})

describe('SalonPanel', () => {
  beforeEach(() => {
    useConversations.mockReset()
  })

  it('lists the pinned Aveline Salon first, separately labelled from the client Salons', () => {
    useConversations.mockReturnValue(
      contextValue([
        conversation({
          id: 'conv-c',
          customerId: 'c-1',
          customerName: 'Samantha Arias',
          lastMessageAt: '2026-09-21T09:00:00Z',
        }),
        conversation({ id: 'conv-g', lastMessageAt: '2026-09-01T09:00:00Z' }),
      ]),
    )

    render(<SalonPanel />)

    const rows = screen.getAllByRole('button', { name: /open salon/i })
    expect(rows).toHaveLength(2)
    expect(within(rows[0]).getByText('Aveline')).toBeInTheDocument()
    expect(within(rows[1]).getByText('Samantha Arias')).toBeInTheDocument()
    // The general thread is marked as the shared concierge thread.
    expect(within(rows[0]).getByText(/concierge/i)).toBeInTheDocument()
  })

  it('does not label a client Salon with the literal word "Customer"', () => {
    useConversations.mockReturnValue(
      contextValue([
        conversation({ id: 'conv-c', customerId: 'c-1', customerName: 'Samantha Arias' }),
      ]),
    )

    render(<SalonPanel />)

    expect(screen.getByText('Samantha Arias')).toBeInTheDocument()
    expect(screen.queryByText('Customer')).not.toBeInTheDocument()
  })
  it('shows the client, not Aveline, in a client thread\'s header', () => {
    // A blossom in a client's header reads as "this thread is Aveline's", when it is the client's.
    useConversations.mockReturnValue(
      contextValue([
        conversation({ id: 'conv-g' }),
        conversation({ id: 'conv-c', customerId: 'c-1', customerName: 'Samantha Arias' }),
      ], { activeConversationId: 'conv-c' }),
    )

    render(<SalonPanel />)

    // The header names the client and marks them with their own initial.
    expect(screen.getAllByText('Samantha Arias').length).toBeGreaterThan(0)
    expect(screen.getByLabelText('Samantha Arias avatar')).toBeInTheDocument()
  })

  it('names the role of every Salon it lists', () => {
    // The avatar distinguishes the general thread from a client's; this is the accessible half, so a
    // screen reader is told which thread each row opens.
    useConversations.mockReturnValue(
      contextValue([
        conversation({ id: 'conv-g' }),
        conversation({ id: 'conv-c', customerId: 'c-1', customerName: 'Samantha Arias' }),
      ]),
    )

    render(<SalonPanel />)

    expect(screen.getByLabelText('Samantha Arias avatar')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Open salon Aveline' })).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Open salon Samantha Arias' }),
    ).toBeInTheDocument()
  })
})
