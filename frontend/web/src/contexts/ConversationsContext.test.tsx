import { describe, expect, it, vi } from 'vitest'
import { renderToString } from 'react-dom/server'

import { ConversationsProvider, useConversations } from './ConversationsContext'

const { useAuthMock } = vi.hoisted(() => ({
  useAuthMock: vi.fn(),
}))

vi.mock('@clerk/react', () => ({
  useAuth: () => useAuthMock(),
}))

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: vi.fn(),
  HubConnectionState: {
    Disconnected: 'Disconnected',
    Connecting: 'Connecting',
    Connected: 'Connected',
    Reconnecting: 'Reconnecting',
  },
}))

vi.mock('@/lib/conversations', () => ({
  createConversationsConnection: vi.fn(),
  startConversations: vi.fn(() => () => undefined),
}))

vi.mock('@/lib/conversations-api', () => ({
  fetchConversations: vi.fn().mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 50 }),
  fetchMessages: vi.fn().mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 100 }),
  getOrCreateConversation: vi.fn(),
  sendMessage: vi.fn(),
  decideSignOff: vi.fn(),
}))

function Probe() {
  const { conversations, loading, connectionState } = useConversations()
  return (
    <span>
      {connectionState}|{loading ? 'loading' : 'loaded'}|{conversations.length}
    </span>
  )
}

describe('ConversationsProvider', () => {
  it('renders children and exposes a disconnected default state', () => {
    useAuthMock.mockReturnValue({ isLoaded: true, isSignedIn: false, getToken: vi.fn() })

    const html = renderToString(
      <ConversationsProvider organizationId="org-1">
        <Probe />
      </ConversationsProvider>,
    )

    expect(html).toContain('Disconnected')
  })

  it('throws when useConversations is used outside the provider', () => {
    useAuthMock.mockReturnValue({ isLoaded: true, isSignedIn: false, getToken: vi.fn() })

    expect(() => renderToString(<Probe />)).toThrow(
      'useConversations must be used within a ConversationsProvider',
    )
  })
})
