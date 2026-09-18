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

/** The `ReceiveAgentState` payload contract shared with the conversation hub. */
export interface AgentStatePayload {
  conversationId: string
  state: string
  agentKey: string | null
  traceId: string | null
}

export interface ConversationHandlers {
  onMessage: (payload: ConversationMessagePayload) => void
  onAgentState?: (payload: AgentStatePayload) => void
  onStateChange: (state: HubConnectionState) => void
  /** Invoked once the connection has started, so the caller can join the Salon group. */
  onConnected?: (connection: HubConnection) => void
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
  const { onMessage, onAgentState, onStateChange, onConnected } = handlers

  connection.on('ReceiveMessage', (payload: ConversationMessagePayload) => {
    onMessage(payload)
  })
  connection.on('ReceiveAgentState', (payload: AgentStatePayload) => {
    onAgentState?.(payload)
  })
  connection.onreconnecting(() => onStateChange(connection.state))
  connection.onreconnected(() => onStateChange(connection.state))
  connection.onclose(() => onStateChange(HubConnectionState.Disconnected))

  onStateChange(connection.state)
  void connection.start().then(
    () => {
      onStateChange(connection.state)
      onConnected?.(connection)
    },
    () => onStateChange(HubConnectionState.Disconnected),
  )

  return () => {
    connection.off('ReceiveMessage')
    connection.off('ReceiveAgentState')
    void connection.stop()
  }
}
