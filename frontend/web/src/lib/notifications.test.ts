import { afterEach, describe, expect, it, vi } from 'vitest'

import { apiBaseUrl } from './env'
import {
  createNotificationsConnection,
  startNotifications,
  type NotificationPayload,
} from './notifications'

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

describe('notifications connection factory', () => {
  afterEach(() => {
    vi.clearAllMocks()
  })

  it('builds a connection to the notifications hub with the API base URL', () => {
    const fakeConnection = { state: 'Disconnected' }
    buildMock.mockReturnValue(fakeConnection)
    withUrlMock.mockReturnThis()
    withAutomaticReconnectMock.mockReturnThis()

    const connection = createNotificationsConnection(() => Promise.resolve('tok'))

    expect(withUrlMock).toHaveBeenCalledWith(`${apiBaseUrl}/hubs/notifications`, {
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

    createNotificationsConnection(getToken)

    const factory = withUrlMock.mock.calls[0][1].accessTokenFactory
    await expect(factory()).resolves.toBe('clerk-token')
    expect(getToken).toHaveBeenCalled()
  })

  it('accessTokenFactory falls back to empty string when no token', async () => {
    withUrlMock.mockReturnThis()
    withAutomaticReconnectMock.mockReturnThis()
    buildMock.mockReturnValue({})

    createNotificationsConnection(() => Promise.resolve(null))

    const factory = withUrlMock.mock.calls[0][1].accessTokenFactory
    await expect(factory()).resolves.toBe('')
  })
})

describe('startNotifications', () => {
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

  it('registers the ReceiveNotification handler and starts the connection', async () => {
    const connection = makeConnection()
    const onNotification = vi.fn()
    const onStateChange = vi.fn()

    const cleanup = startNotifications(connection as never, { onNotification, onStateChange })

    expect(connection.on).toHaveBeenCalledWith('ReceiveNotification', expect.any(Function))
    expect(connection.start).toHaveBeenCalled()
    await vi.waitFor(() => expect(onStateChange).toHaveBeenCalled())

    cleanup()
    expect(connection.off).toHaveBeenCalledWith('ReceiveNotification')
    expect(connection.stop).toHaveBeenCalled()
  })

  it('invokes onNotification when a ReceiveNotification message arrives', () => {
    const connection = makeConnection()
    const onNotification = vi.fn()
    const onStateChange = vi.fn()

    startNotifications(connection as never, { onNotification, onStateChange })

    const payload: NotificationPayload = { type: 'PaymentConfirmed', title: 'Paid', body: 'Order paid' }
    connection.handlers['ReceiveNotification']?.(payload)

    expect(onNotification).toHaveBeenCalledWith(payload)
  })

  it('reports Disconnected when the connection closes', () => {
    const connection = makeConnection()
    const onNotification = vi.fn()
    const onStateChange = vi.fn()

    startNotifications(connection as never, { onNotification, onStateChange })

    connection.stateHandlers.close?.()

    expect(onStateChange).toHaveBeenCalledWith('Disconnected')
  })
})
