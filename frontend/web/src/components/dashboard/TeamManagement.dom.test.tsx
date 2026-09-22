import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const fetchMembers = vi.fn()
const listPendingInvitations = vi.fn()

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn(), info: vi.fn() },
}))

vi.mock('@/lib/team-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/team-api')>('@/lib/team-api')
  return {
    ...actual,
    fetchMembers: (...a: unknown[]) => fetchMembers(...a),
    changeMemberRole: vi.fn(),
    suspendMember: vi.fn(),
    activateMember: vi.fn(),
    removeMember: vi.fn(),
  }
})

vi.mock('@/lib/invitations', () => ({
  listPendingInvitations: (...a: unknown[]) => listPendingInvitations(...a),
  createInvitation: vi.fn(),
  createBulkInvitations: vi.fn(),
  revokeInvitation: vi.fn(),
}))

import type { OrganizationProfileDto } from '@/types/organization'

import { TeamManagement } from './TeamManagement'

const ORGANIZATION = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Aveline Colombo 07',
  slug: 'aveline-colombo-07',
  clerkOrgId: null,
  ownerUserId: 'u-1',
  address: null,
  phoneNumber: null,
  description: null,
  logoUrl: null,
  planTier: 'Bloom',
} as never as OrganizationProfileDto

const ME = '22222222-2222-2222-2222-222222222222'

function member(overrides: Record<string, unknown> = {}) {
  return {
    userId: '33333333-3333-3333-3333-333333333333',
    email: 'staff@aveline.lk',
    firstName: 'Staff',
    lastName: 'Member',
    displayName: null,
    profileImageUrl: null,
    boutiqueRole: 'org:boutique_staff',
    status: 'Active',
    joinedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

function renderPage() {
  return render(
    <TeamManagement organization={ORGANIZATION} role="org:boutique_owner" currentUserId={ME} />,
  )
}

describe('TeamManagement', () => {
  beforeEach(() => {
    fetchMembers
      .mockReset()
      .mockResolvedValue({ items: [member()], page: 1, pageSize: 20, total: 1 })
    listPendingInvitations.mockReset().mockResolvedValue([])
  })

  it('is a page about the team, with the roster visible and no generator on it', async () => {
    // The reported defect: the section read as "Team & Code Generation" and the generator, its batch
    // controls and three code metrics filled the fold, so the member list - the thing the section is
    // for - was below it.
    renderPage()

    expect(await screen.findByText('Staff Member')).toBeInTheDocument()
    expect(screen.queryByText(/code generator studio/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/generate \d+ codes/i)).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: /^generate code$/i }),
    ).not.toBeInTheDocument()
  })

  it('opens code generation in a side drawer, not by navigating away', async () => {
    renderPage()
    await screen.findByText('Staff Member')

    await userEvent.click(screen.getByRole('button', { name: /invite staff/i }))

    const drawer = await screen.findByRole('dialog')
    expect(
      within(drawer).getByRole('button', { name: /^generate code$/i }),
    ).toBeInTheDocument()
  })

  it('opens the drawer straight on the pending codes when that button is used', async () => {
    renderPage()
    await screen.findByText('Staff Member')

    await userEvent.click(screen.getByRole('button', { name: /pending codes/i }))

    const drawer = await screen.findByRole('dialog')
    expect(await within(drawer).findByText(/no active codes/i)).toBeInTheDocument()
  })

  it('does not fetch pending codes until the drawer is opened', async () => {
    renderPage()
    await screen.findByText('Staff Member')

    expect(listPendingInvitations).not.toHaveBeenCalled()
  })

  it('shows the pending count on the button once the drawer has read it', async () => {
    listPendingInvitations.mockResolvedValue([
      {
        invitationId: 'inv-1',
        code: 'AAAAAAAAAAAA',
        link: 'l',
        boutiqueRole: 'org:boutique_staff',
        recipientEmail: null,
        createdAt: '2026-01-01T00:00:00Z',
        expiresAt: '2026-01-02T00:00:00Z',
      },
    ])
    renderPage()
    await screen.findByText('Staff Member')

    await userEvent.click(screen.getByRole('button', { name: /pending codes/i }))

    await new Promise((r) => setTimeout(r, 200))
    console.log('BUTTONS', screen.getAllByRole('button', { hidden: true }).map((b) => b.textContent))
  })
})
