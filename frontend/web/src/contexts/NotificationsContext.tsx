import { useAuth } from '@clerk/react'
import { HubConnectionState } from '@microsoft/signalr'
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { toast } from 'sonner'

import {
  createNotificationsConnection,
  startNotifications,
  type NotificationPayload,
} from '@/lib/notifications'

/** Clerk JWT template that mints the Aveline role claims (matches AuthApiBridge). */
const JWT_TEMPLATE = 'jwt-aveline-v1'

interface NotificationsContextValue {
  connectionState: HubConnectionState
  lastNotification: NotificationPayload | null
}

const NotificationsContext = createContext<NotificationsContextValue | undefined>(undefined)

/**
 * Establishes the SignalR notification connection while signed in and surfaces incoming
 * notifications as toasts. Exposes the connection state and the most recent notification
 * for any consumer (e.g. a future notification bell).
 */
export function NotificationsProvider({ children }: { children: ReactNode }) {
  const { isLoaded, isSignedIn, getToken } = useAuth()
  const [connectionState, setConnectionState] = useState<HubConnectionState>(
    HubConnectionState.Disconnected,
  )
  const [lastNotification, setLastNotification] = useState<NotificationPayload | null>(null)
  const cleanupRef = useRef<(() => void) | null>(null)

  const handleNotification = useCallback((payload: NotificationPayload) => {
    setLastNotification(payload)
    toast(payload.title, { description: payload.body })
  }, [])

  const handleStateChange = useCallback((state: HubConnectionState) => {
    setConnectionState(state)
  }, [])

  useEffect(() => {
    if (!isLoaded || !isSignedIn) {
      return
    }

    const connection = createNotificationsConnection(() =>
      getToken({ template: JWT_TEMPLATE }),
    )
    cleanupRef.current = startNotifications(connection, {
      onNotification: handleNotification,
      onStateChange: handleStateChange,
    })

    return () => {
      cleanupRef.current?.()
      cleanupRef.current = null
    }
  }, [getToken, handleNotification, handleStateChange, isLoaded, isSignedIn])

  return (
    <NotificationsContext.Provider value={{ connectionState, lastNotification }}>
      {children}
    </NotificationsContext.Provider>
  )
}

export function useNotifications(): NotificationsContextValue {
  const context = useContext(NotificationsContext)
  if (!context) {
    throw new Error('useNotifications must be used within a NotificationsProvider')
  }
  return context
}
