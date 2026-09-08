import { describe, expect, it, vi } from 'vitest'
import { renderToString } from 'react-dom/server'

import { NotificationsProvider, useNotifications } from './NotificationsContext'

const { useAuthMock, toastMock } = vi.hoisted(() => ({
  useAuthMock: vi.fn(),
  toastMock: vi.fn(),
}))

vi.mock('@clerk/react', () => ({
  useAuth: () => useAuthMock(),
}))

vi.mock('sonner', () => ({
  toast: toastMock,
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

function Probe() {
  const { connectionState, lastNotification } = useNotifications()
  return (
    <span>
      {connectionState}|{lastNotification?.title ?? 'none'}
    </span>
  )
}

describe('NotificationsProvider', () => {
  it('renders children and exposes a disconnected default state', () => {
    useAuthMock.mockReturnValue({ isLoaded: true, isSignedIn: false, getToken: vi.fn() })

    const html = renderToString(
      <NotificationsProvider>
        <Probe />
      </NotificationsProvider>,
    )

    expect(html).toContain('Disconnected')
    expect(html).toContain('none')
  })

  it('throws when useNotifications is used outside the provider', () => {
    useAuthMock.mockReturnValue({ isLoaded: true, isSignedIn: false, getToken: vi.fn() })

    expect(() => renderToString(<Probe />)).toThrow(
      'useNotifications must be used within a NotificationsProvider',
    )
  })
})
