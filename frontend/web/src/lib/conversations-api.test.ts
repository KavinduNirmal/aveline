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
  fetchConversation,
  fetchConversations,
  fetchMessages,
  getOrCreateConversation,
  sendMessage,
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

  it('decideSignOff posts the approval decision with the content hash', async () => {
    postMock.mockResolvedValue({ data: { id: 'msg-1' } })

    await decideSignOff(ORG, CONV, 'msg-1', true, 'abc123')

    expect(postMock).toHaveBeenCalledWith(
      `/api/v1/orgs/${ORG}/conversations/${CONV}/messages/msg-1/sign-off`,
      { approved: true, contentHash: 'abc123' },
    )
  })
})
