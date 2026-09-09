import { afterEach, describe, expect, it, vi } from 'vitest'

import { apiBaseUrl } from './env'
import {
  createConversationsConnection,
  startConversations,
  type ConversationMessagePayload,
} from './conversations'

const { withUrlMock, withAutomaticReconnectMock, buildMock } = vi.hoisted(() => ({
  withUrlMock: vi.fn(),
  withAutomaticReconnectMock: vi.fn(),
  buildMock: vi.fn(),
}))

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: vi.fn(function () {
    return {
      withUrl: withUrlMock,
      withAutomaticReconnect: withAutomaticReconnectMock,
      build: buildMock,
    }
  }),
  HubConnectionState: {
    Disconnected: 'Disconnected',
    Connecting: 'Connecting',
    Connected: 'Connected',
    Reconnecting: 'Reconnecting',
  },
}))

describe('conversations connection factory', () => {
  afterEach(() => {
    vi.clearAllMocks()
  })

  it('builds a connection to the conversations hub with the API base URL', () => {
    const fakeConnection = { state: 'Disconnected' }
    buildMock.mockReturnValue(fakeConnection)
    withUrlMock.mockReturnThis()
    withAutomaticReconnectMock.mockReturnThis()

    const connection = createConversationsConnection(() => Promise.resolve('tok'))

    expect(withUrlMock).toHaveBeenCalledWith(`${apiBaseUrl}/hubs/conversations`, {
      accessTokenFactory: expect.any(Function),
    })
    expect(withAutomaticReconnectMock).toHaveBeenCalled()
    expect(connection).toBe(fakeConnection)
  })

  it('accessTokenFactory resolves the token from the getter', async () => {
    const getToken = vi.fn().mockResolvedValue('clerk-token')
    withUrlMock.mockReturnThis()
    withAutomaticReconnectMock.mockReturnThis()
    buildMock.mockReturnValue({})

    createConversationsConnection(getToken)

    const factory = withUrlMock.mock.calls[0][1].accessTokenFactory
    await expect(factory()).resolves.toBe('clerk-token')
    expect(getToken).toHaveBeenCalled()
  })

  it('accessTokenFactory falls back to empty string when no token', async () => {
    withUrlMock.mockReturnThis()
    withAutomaticReconnectMock.mockReturnThis()
    buildMock.mockReturnValue({})

    createConversationsConnection(() => Promise.resolve(null))

    const factory = withUrlMock.mock.calls[0][1].accessTokenFactory
    await expect(factory()).resolves.toBe('')
  })
})

describe('startConversations', () => {
  const makeConnection = () => {
    const handlers: Record<string, (payload?: unknown) => void> = {}
    const stateHandlers: Record<string, () => void> = {}
    return {
      state: 'Disconnected',
      on: vi.fn((event: string, handler: (payload?: unknown) => void) => {
        handlers[event] = handler
      }),
      off: vi.fn(),
      onreconnecting: vi.fn((h: () => void) => {
        stateHandlers.reconnecting = h
      }),
      onreconnected: vi.fn((h: () => void) => {
        stateHandlers.reconnected = h
      }),
      onclose: vi.fn((h: () => void) => {
        stateHandlers.close = h
      }),
      start: vi.fn().mockResolvedValue(undefined),
      stop: vi.fn().mockResolvedValue(undefined),
      handlers,
      stateHandlers,
    }
  }

  it('registers the ReceiveMessage handler and starts the connection', async () => {
    const connection = makeConnection()
    const onMessage = vi.fn()
    const onStateChange = vi.fn()

    const cleanup = startConversations(connection as never, { onMessage, onStateChange })

    expect(connection.on).toHaveBeenCalledWith('ReceiveMessage', expect.any(Function))
    expect(connection.start).toHaveBeenCalled()
    await vi.waitFor(() => expect(onStateChange).toHaveBeenCalled())

    cleanup()
    expect(connection.off).toHaveBeenCalledWith('ReceiveMessage')
    expect(connection.stop).toHaveBeenCalled()
  })

  it('invokes onMessage when a ReceiveMessage arrives', () => {
    const connection = makeConnection()
    const onMessage = vi.fn()
    const onStateChange = vi.fn()

    startConversations(connection as never, { onMessage, onStateChange })

    const payload: ConversationMessagePayload = {
      id: 'm1',
      conversationId: 'c1',
      authorKind: 'Agent',
      agentKey: 'aveline',
      authorUserId: null,
      kind: 'Note',
      contentBlocks: [{ type: 'text', text: 'Hello' }],
      contentHash: null,
      replyToMessageId: null,
      status: 'Published',
      createdAt: new Date().toISOString(),
    }
    connection.handlers['ReceiveMessage']?.(payload)

    expect(onMessage).toHaveBeenCalledWith(payload)
  })

  it('reports Disconnected when the connection closes', () => {
    const connection = makeConnection()
    const onMessage = vi.fn()
    const onStateChange = vi.fn()

    startConversations(connection as never, { onMessage, onStateChange })

    connection.stateHandlers.close?.()

    expect(onStateChange).toHaveBeenCalledWith('Disconnected')
  })

  it('invokes onConnected with the connection once started', async () => {
    const connection = makeConnection()
    const onMessage = vi.fn()
    const onStateChange = vi.fn()
    const onConnected = vi.fn()

    startConversations(connection as never, { onMessage, onStateChange, onConnected })

    await vi.waitFor(() => expect(onConnected).toHaveBeenCalled())
    expect(onConnected).toHaveBeenCalledWith(connection)
  })
})
