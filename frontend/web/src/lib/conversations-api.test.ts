import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const getMock = vi.fn()
const postMock = vi.fn()

vi.mock('@/lib/api', () => ({
  apiClient: {
    get: (...args: unknown[]) => getMock(...args),
    post: (...args: unknown[]) => postMock(...args),
  },
}))

import {
  decideSignOff,
  DeliveryRefusedError,
  deliverBlockToCustomer,
  fetchConversation,
  fetchConversations,
  fetchMessages,
  getOrCreateConversation,
  regenerateMessage,
  sendMessage,
  uploadConversationAttachment,
} from './conversations-api'

const ORG = 'org-1'
const CONV = 'conv-1'

describe('conversations API client', () => {
  beforeEach(() => {
    getMock.mockReset()
    postMock.mockReset()
  })

  afterEach(() => {
    vi.clearAllMocks()
  })

  it('fetchConversations requests the org-scoped list endpoint', async () => {
    getMock.mockResolvedValue({ data: { items: [], total: 0, page: 1, pageSize: 50 } })

    await fetchConversations(ORG)

    expect(getMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/conversations`, {
      params: { page: 1, pageSize: 50 },
    })
  })

  it('getOrCreateConversation posts customerId', async () => {
    postMock.mockResolvedValue({ data: { id: CONV } })

    await getOrCreateConversation(ORG, 'customer-1')

    expect(postMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/conversations`, {
      customerId: 'customer-1',
    })
  })

  it('getOrCreateConversation posts null customerId when omitted', async () => {
    postMock.mockResolvedValue({ data: { id: CONV } })

    await getOrCreateConversation(ORG)

    expect(postMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/conversations`, {
      customerId: null,
    })
  })

  it('fetchConversation requests a single conversation', async () => {
    getMock.mockResolvedValue({ data: { id: CONV } })

    await fetchConversation(ORG, CONV)

    expect(getMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/conversations/${CONV}`)
  })

  it('fetchMessages requests the messages endpoint with paging', async () => {
    getMock.mockResolvedValue({ data: { items: [], total: 0, page: 1, pageSize: 50 } })

    await fetchMessages(ORG, CONV, { page: 2, pageSize: 10 })

    expect(getMock).toHaveBeenCalledWith(
      `/api/v1/orgs/${ORG}/conversations/${CONV}/messages`,
      { params: { page: 2, pageSize: 10, around: undefined } },
    )
  })

  it('sendMessage posts text to the messages endpoint', async () => {
    postMock.mockResolvedValue({ data: { id: 'msg-1' } })

    await sendMessage(ORG, CONV, 'Does anything match Michael?')

    expect(postMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/conversations/${CONV}/messages`, {
      text: 'Does anything match Michael?',
    })
  })

  it('sendMessage keeps the text-only body exactly as before when neither optional field is supplied', async () => {
    postMock.mockResolvedValue({ data: { id: 'msg-1' } })

    await sendMessage(ORG, CONV, 'Hello')

    const body = postMock.mock.calls[0][1] as Record<string, unknown>
    expect(body).toEqual({ text: 'Hello' })
    expect(Object.keys(body)).toEqual(['text'])
    expect('attachmentIds' in body).toBe(false)
    expect('clientMessageId' in body).toBe(false)
  })

  it('sendMessage includes attachmentIds when the list is non-empty', async () => {
    postMock.mockResolvedValue({ data: { id: 'msg-1' } })

    await sendMessage(ORG, CONV, 'Here it is', ['att-1', 'att-2'])

    expect(postMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/conversations/${CONV}/messages`, {
      text: 'Here it is',
      attachmentIds: ['att-1', 'att-2'],
    })
  })

  it('sendMessage omits attachmentIds and clientMessageId when the list is empty', async () => {
    postMock.mockResolvedValue({ data: { id: 'msg-1' } })

    await sendMessage(ORG, CONV, 'Hello', [])

    const body = postMock.mock.calls[0][1] as Record<string, unknown>
    expect(body).toEqual({ text: 'Hello' })
    expect('attachmentIds' in body).toBe(false)
  })

  it('sendMessage includes clientMessageId when given', async () => {
    postMock.mockResolvedValue({ data: { id: 'msg-1' } })

    await sendMessage(ORG, CONV, 'Hello', undefined, 'client-1')

    expect(postMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/conversations/${CONV}/messages`, {
      text: 'Hello',
      clientMessageId: 'client-1',
    })
  })

  it('sendMessage includes both optional fields when both are given', async () => {
    postMock.mockResolvedValue({ data: { id: 'msg-1' } })

    await sendMessage(ORG, CONV, 'Hello', ['att-1'], 'client-1')

    expect(postMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/conversations/${CONV}/messages`, {
      text: 'Hello',
      attachmentIds: ['att-1'],
      clientMessageId: 'client-1',
    })
  })

  it('uploadConversationAttachment posts the file as multipart form data to the attachments endpoint', async () => {
    postMock.mockResolvedValue({ data: { attachmentId: 'att-1' } })

    const file = new File(['fake image bytes'], 'saree.jpg', { type: 'image/jpeg' })
    const result = await uploadConversationAttachment(ORG, CONV, file)

    expect(postMock).toHaveBeenCalledWith(
      `/api/v1/orgs/${ORG}/conversations/${CONV}/attachments`,
      expect.any(FormData),
      { headers: { 'Content-Type': 'multipart/form-data' } },
    )
    expect(result.attachmentId).toBe('att-1')
  })

  it('uploadConversationAttachment sends the file under the "file" field name and sets the multipart content type', async () => {
    postMock.mockResolvedValue({ data: { attachmentId: 'att-1' } })

    const file = new File(['fake image bytes'], 'saree.jpg', { type: 'image/jpeg' })
    await uploadConversationAttachment(ORG, CONV, file)

    const [url, body, config] = postMock.mock.calls[0] as [
      string,
      FormData,
      { headers: Record<string, string> },
    ]

    expect(url).toBe(`/api/v1/orgs/${ORG}/conversations/${CONV}/attachments`)
    expect(body).toBeInstanceOf(FormData)
    // The server reads `form.Files.GetFile("file")`; an unset override would leave the
    // shared client's default `application/json` header and no multipart boundary.
    expect(config.headers['Content-Type']).toBe('multipart/form-data')

    const uploaded = body.get('file')
    expect(uploaded).toBeInstanceOf(File)
    expect((uploaded as File).name).toBe('saree.jpg')
    expect(await (uploaded as File).text()).toBe('fake image bytes')
  })

  it('uploadConversationAttachment uses the supplied name for a Blob upload', async () => {
    postMock.mockResolvedValue({ data: { attachmentId: 'att-2' } })

    const blob = new Blob(['pdf bytes'], { type: 'application/pdf' })
    await uploadConversationAttachment(ORG, CONV, blob, 'invoice.pdf')

    const body = postMock.mock.calls[0][1] as FormData
    expect(body.has('file')).toBe(true)
    expect((body.get('file') as File).name).toBe('invoice.pdf')
  })

  it('decideSignOff posts the approval decision with the content hash', async () => {
    postMock.mockResolvedValue({ data: { id: 'msg-1' } })

    await decideSignOff(ORG, CONV, 'msg-1', true, 'abc123')

    expect(postMock).toHaveBeenCalledWith(
      `/api/v1/orgs/${ORG}/conversations/${CONV}/messages/msg-1/sign-off`,
      { approved: true, contentHash: 'abc123' },
    )
  })

  it('deliverBlockToCustomer posts to the deliver route, not the messages route', async () => {
    postMock.mockResolvedValue({
      data: {
        delivered: true,
        channel: 'WhatsApp',
        providerMessageId: 'wamid.1',
        message: { id: 'msg-9' },
      },
    })

    const receipt = await deliverBlockToCustomer(ORG, CONV, 'Silk Slip Dress · LKR 24,000')

    // A note on the messages route reaches nobody, so delivery must never be posted there.
    expect(postMock).toHaveBeenCalledWith(
      `/api/v1/orgs/${ORG}/conversations/${CONV}/deliver`,
      { text: 'Silk Slip Dress · LKR 24,000' },
    )
    expect(receipt.channel).toBe('WhatsApp')
    expect(receipt.message).toMatchObject({ id: 'msg-9' })
  })

  it('deliverBlockToCustomer carries the idempotency key only when one is given', async () => {
    postMock.mockResolvedValue({ data: { delivered: true } })

    await deliverBlockToCustomer(ORG, CONV, 'hello', 'key-1')
    expect(postMock.mock.calls[0][1]).toEqual({ text: 'hello', clientMessageId: 'key-1' })

    postMock.mockClear()
    await deliverBlockToCustomer(ORG, CONV, 'hello')
    expect(postMock.mock.calls[0][1]).toEqual({ text: 'hello' })
  })

  it('deliverBlockToCustomer throws the server\'s refusal sentence, not a generic failure', async () => {
    // "You have not connected WhatsApp" and "that client has no number" are different things for
    // the associate to do next, so the server's own sentence has to survive the trip.
    postMock.mockRejectedValue({
      response: {
        data: {
          refusal: 'channel_not_connected',
          detail: 'This boutique has not connected WhatsApp yet.',
        },
      },
    })

    await expect(deliverBlockToCustomer(ORG, CONV, 'hello')).rejects.toMatchObject({
      name: 'DeliveryRefusedError',
      refusal: 'channel_not_connected',
      message: 'This boutique has not connected WhatsApp yet.',
    })
  })

  it('deliverBlockToCustomer still refuses readably when the body carries no sentence', async () => {
    postMock.mockRejectedValue(new Error('network down'))

    const error = await deliverBlockToCustomer(ORG, CONV, 'hello').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(DeliveryRefusedError)
    expect((error as DeliveryRefusedError).message).toBe('That could not be delivered.')
    expect((error as DeliveryRefusedError).refusal).toBeNull()
  })

  it('regenerateMessage posts to the regenerate route with no body', async () => {
    postMock.mockResolvedValue({ status: 202 })

    await regenerateMessage(ORG, CONV, 'msg-7')

    expect(postMock).toHaveBeenCalledWith(
      `/api/v1/orgs/${ORG}/conversations/${CONV}/messages/msg-7/regenerate`,
    )
  })
})
