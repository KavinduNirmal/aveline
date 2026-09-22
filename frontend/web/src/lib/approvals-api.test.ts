import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const getMock = vi.fn()
const postMock = vi.fn()

vi.mock('@/lib/api', () => ({
  apiClient: {
    get: (...a: unknown[]) => getMock(...a),
    post: (...a: unknown[]) => postMock(...a),
  },
}))

import {
  approveApproval,
  availableDecisions,
  fetchApprovals,
  rejectApproval,
  reviseApproval,
} from './approvals-api'

const ORG = '11111111-1111-1111-1111-111111111111'
const APPROVAL = '33333333-3333-3333-3333-333333333333'

describe('approvals-api', () => {
  beforeEach(() => {
    getMock.mockReset().mockResolvedValue({ data: { items: [], page: 1, pageSize: 20, total: 0 } })
    postMock.mockReset().mockResolvedValue({ data: {} })
  })

  afterEach(() => vi.clearAllMocks())

  it('reads the approval queue with its filter and paging', async () => {
    await fetchApprovals(ORG, { status: 'pending', page: 2, pageSize: 20 })

    const [path, config] = getMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/approvals`)
    expect(config.params).toEqual({ status: 'pending', page: 2, pageSize: 20 })
  })

  it('omits a blank status rather than sending an empty filter', async () => {
    await fetchApprovals(ORG, { status: '' })

    expect(getMock.mock.calls[0][1].params.status).toBeUndefined()
  })

  it('posts each verb to its own route', async () => {
    await approveApproval(ORG, APPROVAL, { reason: 'Within policy' })
    await rejectApproval(ORG, APPROVAL, { reason: 'Out of policy' })
    await reviseApproval(ORG, APPROVAL, { reason: 'Trimmed', revisedDiscount: 500 })

    expect(postMock.mock.calls[0][0]).toBe(`/api/v1/orgs/${ORG}/approvals/${APPROVAL}/approve`)
    expect(postMock.mock.calls[0][1]).toEqual({ reason: 'Within policy' })
    expect(postMock.mock.calls[1][0]).toBe(`/api/v1/orgs/${ORG}/approvals/${APPROVAL}/reject`)
    expect(postMock.mock.calls[2][0]).toBe(`/api/v1/orgs/${ORG}/approvals/${APPROVAL}/revise`)
    expect(postMock.mock.calls[2][1]).toEqual({ reason: 'Trimmed', revisedDiscount: 500 })
  })
})

/**
 * Q14. `reject` cancels the order and `revise` rewrites its money fields, so a staff approver must
 * not be offered either. The panel asks this before it renders a button: a hidden verb is honest, a
 * button that answers 403 is not.
 */
describe('availableDecisions', () => {
  it('gives an owner or manager all three verbs', () => {
    expect(availableDecisions(true, true)).toEqual(['approve', 'reject', 'revise'])
  })

  it('gives a staff approver only approve', () => {
    expect(availableDecisions(true, false)).toEqual(['approve'])
  })

  it('gives a caller with neither permission no decision verbs', () => {
    expect(availableDecisions(false, false)).toEqual([])
  })

  it('does not grant approve from orders:manage alone', () => {
    expect(availableDecisions(false, true)).toEqual([])
  })
})
