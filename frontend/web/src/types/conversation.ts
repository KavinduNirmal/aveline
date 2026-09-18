/**
 * Types for the conversation ("The Salon") API, mirroring the backend DTOs in
 * `Aveline.Api/Modules/Conversations/DTOs`. Quiet-luxury vocabulary: a Conversation is a
 * Salon; messages carry a kind (Note, Look, Piece, AtAGlance, ClientMessage, SignOff,
 * Payment, Courier, Suggestion) and an author persona.
 */

/** The row's actionable states, in the order the server sends them. */
export type ConversationMarker = 'approval' | 'choice' | 'draft'

/** A conversation (Salon) as returned by the API. */
export interface ConversationDto {
  id: string
  /** The thread's nature and audience; customer context rides `customerId`. */
  kind: string
  customerId: string | null
  customerName: string | null
  /**
   * The channel reference (e.g. a WhatsApp phone) on a thread created by the inbound
   * path, so a thread whose customer is not yet identified is rendered rather than
   * mistaken for the general concierge.
   */
  externalRef: string | null
  threadId: string
  status: string
  lastMessageAt: string | null
  /** A block-aware summary of the newest message, truncated to 120 characters. */
  lastMessagePreview: string | null
  /** The message kind as sent (`Note` for every agent persona). */
  lastMessageKind: string | null
  /** The content-block type the preview came from; the row's category. */
  lastMessageBlock: string | null
  /** One of `Staff`, `Agent`, `Customer`. */
  lastMessageAuthor: string | null
  lastMessageAgentKey: string | null
  markers: ConversationMarker[]
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
