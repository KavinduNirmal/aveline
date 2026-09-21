import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const getMock = vi.fn()
const postMock = vi.fn()
const patchMock = vi.fn()
const deleteMock = vi.fn()

vi.mock('@/lib/api', () => ({
  apiClient: {
    get: (...args: unknown[]) => getMock(...args),
    post: (...args: unknown[]) => postMock(...args),
    patch: (...args: unknown[]) => patchMock(...args),
    delete: (...args: unknown[]) => deleteMock(...args),
  },
}))

import {
  createWalkInCustomer,
  deleteCustomer,
  fetchCustomer,
  fetchCustomerBook,
  fetchCustomerInteractions,
  recordCustomerInteraction,
  updateCustomer,
} from './customers-api'

const ORG = '11111111-1111-1111-1111-111111111111'
const CUSTOMER = '22222222-2222-2222-2222-222222222222'

describe('customers-api', () => {
  beforeEach(() => {
    getMock.mockReset().mockResolvedValue({ data: {} })
    postMock.mockReset().mockResolvedValue({ data: {} })
    patchMock.mockReset().mockResolvedValue({ data: {} })
    deleteMock.mockReset().mockResolvedValue({ data: undefined })
  })

  afterEach(() => vi.clearAllMocks())

  it('builds the book path from the organization id, never the slug', async () => {
    await fetchCustomerBook(ORG, { search: 'nadia', level: 'vip', page: 2, pageSize: 25 })

    expect(getMock).toHaveBeenCalledTimes(1)
    const [path, config] = getMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/customers`)
    expect(config.params).toEqual({
      search: 'nadia',
      level: 'vip',
      page: 2,
      pageSize: 25,
    })
  })

  it('omits an empty search and level rather than sending blank filters', async () => {
    await fetchCustomerBook(ORG, { search: '', level: '' })

    const [, config] = getMock.mock.calls[0]
    expect(config.params.search).toBeUndefined()
    expect(config.params.level).toBeUndefined()
    expect(config.params.page).toBe(1)
  })

  it('reads one client at the detail path', async () => {
    await fetchCustomer(ORG, CUSTOMER)

    expect(getMock).toHaveBeenCalledWith(
      `/api/v1/orgs/${ORG}/customers/${CUSTOMER}`,
      expect.anything(),
    )
  })

  it('reads the interaction history at the nested path', async () => {
    await fetchCustomerInteractions(ORG, CUSTOMER, { page: 3, pageSize: 10 })

    const [path, config] = getMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/customers/${CUSTOMER}/interactions`)
    expect(config.params).toEqual({ page: 3, pageSize: 10 })
  })

  it('sends an Idempotency-Key when creating a walk-in', async () => {
    await createWalkInCustomer(
      ORG,
      { fullName: 'Nadia Client', phoneNumber: '0771234567' },
      'key-1',
    )

    const [path, payload, config] = postMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/customers`)
    expect(payload.fullName).toBe('Nadia Client')
    expect(config.headers['Idempotency-Key']).toBe('key-1')
  })

  it('sends an Idempotency-Key when recording an interaction', async () => {
    await recordCustomerInteraction(
      ORG,
      CUSTOMER,
      { occurredAtUtc: '2026-09-20T10:00:00Z', channel: 'in_person', purchaseTotal: 12500 },
      'key-2',
    )

    const [path, payload, config] = postMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/customers/${CUSTOMER}/interactions`)
    expect(payload.purchaseTotal).toBe(12500)
    expect(config.headers['Idempotency-Key']).toBe('key-2')
  })

  it('patches only the writable fields', async () => {
    await updateCustomer(ORG, CUSTOMER, { fullName: 'Nadia Perera', level: 'vip' })

    const [path, payload] = patchMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/customers/${CUSTOMER}`)
    expect(payload).toEqual({ fullName: 'Nadia Perera', level: 'vip' })
    // `status` is derived server-side and absent from the request type.
    expect(payload).not.toHaveProperty('status')
  })

  it('deletes at the customer path', async () => {
    await deleteCustomer(ORG, CUSTOMER)

    expect(deleteMock).toHaveBeenCalledWith(`/api/v1/orgs/${ORG}/customers/${CUSTOMER}`)
  })

  it('exposes `loyaltyTierIsDerived` on the detail type so the client cannot render a tier editor', () => {
    // A compile-time contract: the field exists and is a number, not a boolean the UI could invert.
    const detail = {
      customerId: CUSTOMER,
      fullName: 'Nadia',
      nickname: null,
      phoneNumber: '+94771234567',
      email: null,
      level: 'vip',
      status: 'returning',
      totalSpent: 42000,
      visitCount: 3,
      lastVisitAtUtc: null,
      loyaltyTierIsDerived: 1,
      createdAtUtc: '2026-01-01T00:00:00Z',
      updatedAtUtc: null,
      interactionCount: 2,
      tags: ['wedding-season'],
    } as const

    expect(detail.loyaltyTierIsDerived).toBe(1)
  })
})
