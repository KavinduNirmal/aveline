import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
} from '@microsoft/signalr'

import { apiBaseUrl } from './env'
import type { MessageDto } from '@/types/conversation'

/** Returns the current Clerk JWT, or `null` when signed out. */
export type TokenGetter = () => Promise<string | null>

/** The `ReceiveMessage` payload contract shared with the conversation hub. */
export type ConversationMessagePayload = MessageDto

export interface ConversationHandlers {
  onMessage: (payload: ConversationMessagePayload) => void
  onStateChange: (state: HubConnectionState) => void
}

/**
 * Builds a SignalR connection to the conversation hub, authenticating with the Clerk
 * JWT supplied by {@link TokenGetter} (sent as the `access_token` query parameter).
 */
export function createConversationsConnection(getToken: TokenGetter): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(`${apiBaseUrl}/hubs/conversations`, {
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
export function startConversations(
  connection: HubConnection,
  handlers: ConversationHandlers,
): () => void {
  const { onMessage, onStateChange } = handlers

  connection.on('ReceiveMessage', (payload: ConversationMessagePayload) => {
    onMessage(payload)
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
    connection.off('ReceiveMessage')
    void connection.stop()
  }
}
