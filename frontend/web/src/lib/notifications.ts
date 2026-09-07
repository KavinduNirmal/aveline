import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
} from '@microsoft/signalr'

import { apiBaseUrl } from './env'

/** Returns the current Clerk JWT, or `null` when signed out. */
export type TokenGetter = () => Promise<string | null>

/** The `ReceiveNotification` payload contract shared with the backend hub. */
export interface NotificationPayload {
  type: string
  title: string
  body: string
  data?: Record<string, string | null>
}

export interface NotificationsHandlers {
  onNotification: (payload: NotificationPayload) => void
  onStateChange: (state: HubConnectionState) => void
}

/**
 * Builds a SignalR connection to the notifications hub, authenticating with the Clerk
 * JWT supplied by {@link TokenGetter} (sent as the `access_token` query parameter).
 */
export function createNotificationsConnection(getToken: TokenGetter): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(`${apiBaseUrl}/hubs/notifications`, {
      accessTokenFactory: async () => (await getToken()) ?? '',
    })
    .withAutomaticReconnect()
    .build()
}

/**
 * Wires a connection's handlers and starts it. Returns a cleanup function that removes
 * the handler and stops the connection. Framework-agnostic so it can be unit tested
 * without a DOM.
 */
export function startNotifications(
  connection: HubConnection,
  handlers: NotificationsHandlers,
): () => void {
  const { onNotification, onStateChange } = handlers

  connection.on('ReceiveNotification', (payload: NotificationPayload) => {
    onNotification(payload)
  })
  connection.onreconnecting(() => onStateChange(connection.state))
  connection.onreconnected(() => onStateChange(connection.state))
  connection.onclose(() => onStateChange(HubConnectionState.Disconnected))

  onStateChange(connection.state)
  void connection.start().then(
    () => onStateChange(connection.state),
    () => onStateChange(HubConnectionState.Disconnected),
  )

  return () => {
    connection.off('ReceiveNotification')
    void connection.stop()
  }
}
