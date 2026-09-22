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

/** An uploaded attachment, mirroring the API's `AttachmentDto`. */
export interface ConversationAttachmentDto {
  attachmentId: string
  url: string
  contentType: string
  fileName: string
  sizeBytes: number
  width: number | null
  height: number | null
  messageId: string | null
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
 * Uploads one file as an unbound attachment for a conversation, returning the stored row.
 * See POST /api/v1/orgs/{orgId}/conversations/{id}/attachments. The server reads a single
 * multipart field named `file`; the row stays unbound until a send names its `attachmentId`.
 */
export async function uploadConversationAttachment(
  organizationId: string,
  conversationId: string,
  file: File | Blob,
  fileName?: string,
): Promise<ConversationAttachmentDto> {
  const formData = new FormData()
  formData.append('file', file, fileName || (file instanceof File ? file.name : 'attachment'))

  // The shared client defaults to `application/json`, so the multipart header is set per request.
  const response = await apiClient.post<ConversationAttachmentDto>(
    `${conversationsBase(organizationId)}/${conversationId}/attachments`,
    formData,
    {
      headers: {
        'Content-Type': 'multipart/form-data',
      },
    },
  )
  return response.data
}

/**
 * Sends a staff note and triggers the agent. See POST /api/v1/orgs/{orgId}/conversations/{id}/messages.
 * `attachmentIds` binds already-uploaded unbound attachments to this message, and
 * `clientMessageId` is the stable idempotency key for a composed message across send retries;
 * both keys are omitted from the body when not supplied.
 */
export async function sendMessage(
  organizationId: string,
  conversationId: string,
  text: string,
  attachmentIds?: string[],
  clientMessageId?: string,
): Promise<MessageDto> {
  const body: {
    text: string
    attachmentIds?: string[]
    clientMessageId?: string
  } = { text }
  if (attachmentIds && attachmentIds.length > 0) {
    body.attachmentIds = attachmentIds
  }
  if (clientMessageId) {
    body.clientMessageId = clientMessageId
  }

  const response = await apiClient.post<MessageDto>(
    `${conversationsBase(organizationId)}/${conversationId}/messages`,
    body,
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
