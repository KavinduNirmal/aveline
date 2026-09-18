import { apiClient } from '@/lib/api'
import type {
  ConversationDto,
  ConversationPage,
  MessageDto,
  MessagePage,
} from '@/types/conversation'

const conversationsBase = (organizationId: string) =>
  `/api/v1/orgs/${organizationId}/conversations`

export interface ListConversationsParams {
  page?: number
  pageSize?: number
}

export interface ListMessagesParams {
  page?: number
  pageSize?: number
  around?: string
}

/**
 * Lists an organization's conversations (Salons). See GET /api/v1/orgs/{orgId}/conversations.
 */
export async function fetchConversations(
  organizationId: string,
  params: ListConversationsParams = {},
): Promise<ConversationPage> {
  const response = await apiClient.get<ConversationPage>(conversationsBase(organizationId), {
    params: {
      page: params.page ?? 1,
      pageSize: params.pageSize ?? 50,
    },
  })
  return response.data
}

/**
 * Gets-or-creates the Salon for an optional customer. See POST /api/v1/orgs/{orgId}/conversations.
 */
export async function getOrCreateConversation(
  organizationId: string,
  customerId?: string | null,
): Promise<ConversationDto> {
  const response = await apiClient.post<ConversationDto>(conversationsBase(organizationId), {
    customerId: customerId ?? null,
  })
  return response.data
}

/**
 * Fetches a single conversation. See GET /api/v1/orgs/{orgId}/conversations/{id}.
 */
export async function fetchConversation(
  organizationId: string,
  conversationId: string,
): Promise<ConversationDto> {
  const response = await apiClient.get<ConversationDto>(
    `${conversationsBase(organizationId)}/${conversationId}`,
  )
  return response.data
}

/**
 * Lists a conversation's messages. See GET /api/v1/orgs/{orgId}/conversations/{id}/messages.
 */
export async function fetchMessages(
  organizationId: string,
  conversationId: string,
  params: ListMessagesParams = {},
): Promise<MessagePage> {
  const response = await apiClient.get<MessagePage>(
    `${conversationsBase(organizationId)}/${conversationId}/messages`,
    {
      params: {
        page: params.page ?? 1,
        pageSize: params.pageSize ?? 50,
        around: params.around,
      },
    },
  )
  return response.data
}

/**
 * Sends a staff note and triggers the agent. See POST /api/v1/orgs/{orgId}/conversations/{id}/messages.
 */
export async function sendMessage(
  organizationId: string,
  conversationId: string,
  text: string,
): Promise<MessageDto> {
  const response = await apiClient.post<MessageDto>(
    `${conversationsBase(organizationId)}/${conversationId}/messages`,
    { text },
  )
  return response.data
}

/**
 * Approves or rejects a SignOff message. See POST /api/v1/orgs/{orgId}/conversations/{id}/messages/{messageId}/sign-off.
 * The `contentHash` binds the decision to the exact payload the human saw; the server rejects
 * the decision if the message content changed after display.
 */
export async function decideSignOff(
  organizationId: string,
  conversationId: string,
  messageId: string,
  approved: boolean,
  contentHash: string,
): Promise<MessageDto> {
  const response = await apiClient.post<MessageDto>(
    `${conversationsBase(organizationId)}/${conversationId}/messages/${messageId}/sign-off`,
    { approved, contentHash },
  )
  return response.data
}

/**
 * Binds a Salon to a customer chosen from a resolution `choice` block and re-triggers the
 * agent with that customer in context. See
 * POST /api/v1/orgs/{orgId}/conversations/{id}/select-customer.
 */
export async function selectConversationCustomer(
  organizationId: string,
  conversationId: string,
  customerId: string,
  query?: string,
): Promise<ConversationDto> {
  const response = await apiClient.post<ConversationDto>(
    `${conversationsBase(organizationId)}/${conversationId}/select-customer`,
    { customerId, query },
  )
  return response.data
}
