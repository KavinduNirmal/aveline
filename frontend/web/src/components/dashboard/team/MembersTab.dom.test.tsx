import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const fetchMembers = vi.fn()
const changeMemberRole = vi.fn()
const suspendMember = vi.fn()
const activateMember = vi.fn()
const removeMember = vi.fn()

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn(), info: vi.fn() },
}))

vi.mock('@/lib/team-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/team-api')>('@/lib/team-api')
  return {
    ...actual,
    fetchMembers: (...a: unknown[]) => fetchMembers(...a),
    changeMemberRole: (...a: unknown[]) => changeMemberRole(...a),
    suspendMember: (...a: unknown[]) => suspendMember(...a),
    activateMember: (...a: unknown[]) => activateMember(...a),
    removeMember: (...a: unknown[]) => removeMember(...a),
  }
})

import { ApiError } from '@/lib/api-error'
import { MembersTab } from './MembersTab'

const ORG = '11111111-1111-1111-1111-111111111111'
const ME = '22222222-2222-2222-2222-222222222222'
const OTHER = '33333333-3333-3333-3333-333333333333'

function member(overrides: Record<string, unknown> = {}) {
  return {
    userId: OTHER,
    email: 'other@aveline.lk',
    firstName: 'Other',
    lastName: 'Member',
    displayName: null,
    profileImageUrl: null,
    boutiqueRole: 'org:boutique_staff',
    status: 'Active',
    joinedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

describe('MembersTab', () => {
  beforeEach(() => {
    fetchMembers.mockReset().mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0 })
    changeMemberRole.mockReset()
    suspendMember.mockReset().mockResolvedValue(undefined)
    activateMember.mockReset().mockResolvedValue(undefined)
    removeMember.mockReset()
  })

  it('disables the caller’s own row with a stated reason rather than letting the server 409', async () => {
    fetchMembers.mockResolvedValue({
      items: [member({ userId: ME, email: 'me@aveline.lk', firstName: 'Me' })],
      page: 1,
      pageSize: 20,
      total: 1,
    })

    render(<MembersTab organizationId={ORG} currentUserId={ME} />)

    await screen.findByText('me@aveline.lk')
    const changeRole = screen.getByRole('button', { name: /change role/i })
    expect(changeRole).toBeDisabled()
    expect(screen.getByText(/your own/i)).toBeTruthy()
  })

  it('renders the server’s 409 message when a role change is refused', async () => {
    fetchMembers.mockResolvedValue({ items: [member()], page: 1, pageSize: 20, total: 1 })
    changeMemberRole.mockRejectedValue(
      new ApiError(409, 'You cannot change your own role.'),
    )

    render(<MembersTab organizationId={ORG} currentUserId={ME} />)

    await screen.findByText('other@aveline.lk')
    await userEvent.click(screen.getByRole('button', { name: /change role/i }))
    await userEvent.click(await screen.findByRole('button', { name: /save role/i }))

    expect(await screen.findByText('You cannot change your own role.')).toBeTruthy()
  })

  it('renders a permission message on 403 rather than a generic failure', async () => {
    fetchMembers.mockResolvedValue({ items: [member()], page: 1, pageSize: 20, total: 1 })
    changeMemberRole.mockRejectedValue(
      new ApiError(403, "You don't have permission to perform this action."),
    )

    render(<MembersTab organizationId={ORG} currentUserId={ME} />)

    await screen.findByText('other@aveline.lk')
    await userEvent.click(screen.getByRole('button', { name: /change role/i }))
    await userEvent.click(await screen.findByRole('button', { name: /save role/i }))

    expect(
      await screen.findByText("You don't have permission to perform this action."),
    ).toBeTruthy()
  })

  it('suspends an active member through the lifecycle route', async () => {
    fetchMembers.mockResolvedValue({ items: [member()], page: 1, pageSize: 20, total: 1 })

    render(<MembersTab organizationId={ORG} currentUserId={ME} />)

    await screen.findByText('other@aveline.lk')
    await userEvent.click(screen.getByRole('button', { name: /^suspend$/i }))

    await waitFor(() => expect(suspendMember).toHaveBeenCalledWith(ORG, OTHER))
  })
})
