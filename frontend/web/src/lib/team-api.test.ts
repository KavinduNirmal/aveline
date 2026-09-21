import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const getMock = vi.fn()
const postMock = vi.fn()
const patchMock = vi.fn()
const deleteMock = vi.fn()

vi.mock('@/lib/api', () => ({
  apiClient: {
    get: (...a: unknown[]) => getMock(...a),
    post: (...a: unknown[]) => postMock(...a),
    patch: (...a: unknown[]) => patchMock(...a),
    delete: (...a: unknown[]) => deleteMock(...a),
  },
}))

import {
  activateMember,
  changeMemberRole,
  fetchMembers,
  memberActionBlockedReason,
  removeMember,
  suspendMember,
  type OrganizationMember,
} from './team-api'

const ORG = '11111111-1111-1111-1111-111111111111'
const USER = '22222222-2222-2222-2222-222222222222'

describe('team-api', () => {
  beforeEach(() => {
    getMock.mockReset().mockResolvedValue({ data: { items: [], page: 1, pageSize: 20, total: 0 } })
    postMock.mockReset().mockResolvedValue({ data: {} })
    patchMock.mockReset().mockResolvedValue({ data: {} })
    deleteMock.mockReset().mockResolvedValue({ data: undefined })
  })

  afterEach(() => vi.clearAllMocks())

  it('reads the member page from the organization path with its filters', async () => {
    await fetchMembers(ORG, { status: 'Active', role: 'org:boutique_staff', q: 'nadia', page: 2 })

    const [path, config] = getMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/members`)
    expect(config.params).toEqual({
      status: 'Active',
      role: 'org:boutique_staff',
      q: 'nadia',
      page: 2,
      pageSize: 20,
    })
  })

  it('omits blank filters rather than sending empty strings', async () => {
    await fetchMembers(ORG, { status: '', role: '', q: '' })

    const [, config] = getMock.mock.calls[0]
    expect(config.params.status).toBeUndefined()
    expect(config.params.role).toBeUndefined()
    expect(config.params.q).toBeUndefined()
  })

  it('changes a role with PATCH and the canonical role string', async () => {
    await changeMemberRole(ORG, USER, 'org:boutique_manager')

    const [path, body] = patchMock.mock.calls[0]
    expect(path).toBe(`/api/v1/orgs/${ORG}/members/${USER}`)
    expect(body).toEqual({ boutiqueRole: 'org:boutique_manager' })
  })

  it('suspends and activates through the two verb routes', async () => {
    await suspendMember(ORG, USER)
    await activateMember(ORG, USER)

    expect(postMock.mock.calls[0][0]).toBe(`/api/v1/orgs/${ORG}/members/${USER}/suspend`)
    expect(postMock.mock.calls[1][0]).toBe(`/api/v1/orgs/${ORG}/members/${USER}/activate`)
  })

  it('removes a membership with DELETE', async () => {
    await removeMember(ORG, USER)

    expect(deleteMock.mock.calls[0][0]).toBe(`/api/v1/orgs/${ORG}/members/${USER}`)
  })
})

/**
 * T6. The server refuses a self role change with 409 and an owner suspension/removal with 400. The
 * UI must not offer an action it knows will fail, so the rule is one pure function with a stated
 * reason rather than a disabled button with no explanation.
 */
describe('memberActionBlockedReason', () => {
  const member = (overrides: Partial<OrganizationMember> = {}): OrganizationMember => ({
    userId: USER,
    email: 'staff@aveline.lk',
    firstName: 'Nadia',
    lastName: 'Perera',
    displayName: null,
    profileImageUrl: null,
    boutiqueRole: 'org:boutique_staff',
    status: 'Active',
    joinedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  })

  it('blocks the caller from acting on their own row, with a reason', () => {
    expect(memberActionBlockedReason(member(), USER)).toMatch(/your own/i)
  })

  it('blocks any action on an owner membership', () => {
    const reason = memberActionBlockedReason(
      member({ userId: 'other', boutiqueRole: 'org:boutique_owner' }),
      USER,
    )
    expect(reason).toMatch(/owner/i)
  })

  it('allows an action on another member who is not an owner', () => {
    expect(memberActionBlockedReason(member({ userId: 'other' }), USER)).toBeNull()
  })
})
