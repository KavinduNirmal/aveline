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
 */
export async function decideSignOff(
  organizationId: string,
  conversationId: string,
  messageId: string,
  approved: boolean,
): Promise<MessageDto> {
  const response = await apiClient.post<MessageDto>(
    `${conversationsBase(organizationId)}/${conversationId}/messages/${messageId}/sign-off`,
    { approved },
  )
  return response.data
}
