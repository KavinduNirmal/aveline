/**
 * Types for the conversation ("The Salon") API, mirroring the backend DTOs in
 * `Aveline.Api/Modules/Conversations/DTOs`. Quiet-luxury vocabulary: a Conversation is a
 * Salon; messages carry a kind (Note, Look, Piece, AtAGlance, ClientMessage, SignOff,
 * Payment, Courier, Suggestion) and an author persona.
 */

/** A conversation (Salon) as returned by the API. */
export interface ConversationDto {
  id: string
  kind: string
  customerId: string | null
  threadId: string
  status: string
  lastMessageAt: string | null
}

/** A paginated page of conversations. */
export interface ConversationPage {
  items: ConversationDto[]
  total: number
  page: number
  pageSize: number
}

/** A single message in a Salon. */
export interface MessageDto {
  id: string
  conversationId: string
  authorKind: string
  agentKey: string | null
  authorUserId: string | null
  kind: string
  contentBlocks: unknown[]
  /** Canonical hash of the content blocks; binds a SignOff decision to the exact payload. */
  contentHash: string | null
  replyToMessageId: string | null
  status: string
  createdAt: string
}

/** A paginated page of messages. */
export interface MessagePage {
  items: MessageDto[]
  total: number
  page: number
  pageSize: number
}

/** Canonical agent persona keys (mirrors `AgentKeys` in the backend). */
export const AGENT_KEYS = {
  aveline: 'aveline',
  ava: 'ava',
  elle: 'elle',
  lina: 'lina',
} as const

export type AgentKey = (typeof AGENT_KEYS)[keyof typeof AGENT_KEYS]
