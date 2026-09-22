import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const listPendingInvitations = vi.fn()
const createInvitation = vi.fn()
const createBulkInvitations = vi.fn()
const revokeInvitation = vi.fn()

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn(), info: vi.fn() },
}))

vi.mock('@/lib/invitations', () => ({
  listPendingInvitations: (...a: unknown[]) => listPendingInvitations(...a),
  createInvitation: (...a: unknown[]) => createInvitation(...a),
  createBulkInvitations: (...a: unknown[]) => createBulkInvitations(...a),
  revokeInvitation: (...a: unknown[]) => revokeInvitation(...a),
}))

import type { OrganizationProfileDto } from '@/types/organization'

import { InvitationDrawer } from './InvitationDrawer'

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

function renderDrawer(overrides: Partial<Parameters<typeof InvitationDrawer>[0]> = {}) {
  const onOpenChange = vi.fn()
  const onPendingCountChange = vi.fn()
  const onViewChange = vi.fn()
  render(
    <InvitationDrawer
      open
      onOpenChange={onOpenChange}
      organization={ORGANIZATION}
      view="generate"
      onViewChange={onViewChange}
      onPendingCountChange={onPendingCountChange}
      {...overrides}
    />,
  )
  return { onOpenChange, onPendingCountChange, onViewChange }
}

describe('InvitationDrawer', () => {
  beforeEach(() => {
    listPendingInvitations.mockReset().mockResolvedValue([])
    createInvitation.mockReset()
    createBulkInvitations.mockReset()
    revokeInvitation.mockReset()
  })

  it('is a dialog the operator can close, not a page section', async () => {
    const { onOpenChange } = renderDrawer()

    expect(screen.getByRole('dialog')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: /close/i }))

    expect(onOpenChange).toHaveBeenCalledWith(false)
  })

  it('generates a single code and shows it once', async () => {
    createInvitation.mockResolvedValue({
      invitationId: 'inv-1',
      code: 'ABCD1234EFGH',
      link: 'https://aveline.lk/invite/ABCD1234EFGH',
      boutiqueRole: 'org:boutique_staff',
      recipientEmail: null,
      expiresAt: '2026-01-02T00:00:00Z',
      summaryEmailRequested: false,
      summaryEmailStatus: 'NotRequested',
      summaryEmailNote: null,
    })

    renderDrawer()

    await userEvent.click(screen.getByRole('button', { name: /^generate code$/i }))

    expect(await screen.findByText('ABCD1234EFGH')).toBeInTheDocument()
    expect(createInvitation).toHaveBeenCalledWith(
      ORGANIZATION.id,
      expect.objectContaining({ boutiqueRole: 'org:boutique_staff', validityHours: 24 }),
    )
  })

  it('opens on the generator when asked for the generator', () => {
    renderDrawer({ view: 'generate' })

    expect(screen.getByRole('heading', { name: /invite staff/i })).toBeInTheDocument()
    // The primary action is the only control named "Generate code"; the view switch is "New code".
    expect(screen.getByRole('button', { name: /^generate code$/i })).toBeInTheDocument()
  })

  it('opens on the pending codes when asked, with a revoke per code', async () => {
    listPendingInvitations.mockResolvedValue([
      {
        invitationId: 'inv-9',
        boutiqueRole: 'org:boutique_staff',
        recipientEmail: null,
        createdAt: '2026-01-01T00:00:00Z',
        expiresAt: '2026-01-02T00:00:00Z',
      },
    ])
    revokeInvitation.mockResolvedValue(undefined)

    renderDrawer({ view: 'pending' })

    // A pending code is a non-secret view: the server returns no code to list, so the row is
    // identified by its role and lifetime rather than by a value the operator could copy.
    expect(await screen.findByText(/not addressed to anyone/i)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: /revoke code/i }))

    await waitFor(() => expect(revokeInvitation).toHaveBeenCalledWith(ORGANIZATION.id, 'inv-9'))
    expect(screen.queryByText(/not addressed to anyone/i)).not.toBeInTheDocument()
  })

  it('reports the pending count to the page so its button can show it', async () => {
    listPendingInvitations.mockResolvedValue([
      {
        invitationId: 'inv-1',
        boutiqueRole: 'org:boutique_staff',
        recipientEmail: null,
        createdAt: '2026-01-01T00:00:00Z',
        expiresAt: '2026-01-02T00:00:00Z',
      },
      {
        invitationId: 'inv-2',
        boutiqueRole: 'org:boutique_staff',
        recipientEmail: null,
        createdAt: '2026-01-01T00:00:00Z',
        expiresAt: '2026-01-02T00:00:00Z',
      },
    ])

    const { onPendingCountChange } = renderDrawer({ view: 'pending' })

    await waitFor(() => expect(onPendingCountChange).toHaveBeenCalledWith(2))
  })

  it('does not fetch the pending list while it is closed', () => {
    renderDrawer({ open: false })

    expect(listPendingInvitations).not.toHaveBeenCalled()
  })

  it('says how long each pending code has left, and flags the ones expiring soon', async () => {
    const inThreeHours = new Date(Date.now() + 3 * 60 * 60 * 1000).toISOString()
    const inSixDays = new Date(Date.now() + 6 * 24 * 60 * 60 * 1000).toISOString()
    listPendingInvitations.mockResolvedValue([
      {
        invitationId: 'inv-soon',
        boutiqueRole: 'org:boutique_staff',
        recipientEmail: null,
        createdAt: '2026-01-01T00:00:00Z',
        expiresAt: inThreeHours,
      },
      {
        invitationId: 'inv-later',
        boutiqueRole: 'org:boutique_manager',
        recipientEmail: null,
        createdAt: '2026-01-01T00:00:00Z',
        expiresAt: inSixDays,
      },
    ])

    renderDrawer({ view: 'pending' })

    expect(await screen.findByText(/expires in 3 hours/i)).toBeInTheDocument()
    expect(screen.getByText(/expires in 6 days/i)).toBeInTheDocument()
    // The imminent one is called out on its row; the comfortable one is not.
    expect(screen.getByText(/expires in 3 hours - expiring soon/i)).toBeInTheDocument()
    expect(screen.queryByText(/expires in 6 days - expiring soon/i)).not.toBeInTheDocument()
  })

  it('never offers to re-display a listed code, because the server does not return one', async () => {
    // The generation response is the only place a code exists in the clear. A copy button on the
    // pending list would imply the drawer holds a value it was never given.
    listPendingInvitations.mockResolvedValue([
      {
        invitationId: 'inv-1',
        boutiqueRole: 'org:boutique_staff',
        recipientEmail: 'staff@aveline.lk',
        createdAt: '2026-01-01T00:00:00Z',
        expiresAt: new Date(Date.now() + 6 * 24 * 60 * 60 * 1000).toISOString(),
      },
    ])

    renderDrawer({ view: 'pending' })
    await screen.findByText('staff@aveline.lk')

    expect(screen.queryByRole('button', { name: /copy/i })).not.toBeInTheDocument()
  })
  it('asks the opener to switch views rather than holding the view itself', async () => {
    // The opener owns the view: it sets it before opening, and this drawer is remounted on change.
    // Two sources of truth for "which half is showing" is what made the previous version contradict
    // its own opener.
    const { onViewChange } = renderDrawer({ view: 'generate' })

    // The view switch is a ToggleGroup, so its items expose `data-state` rather than the button role.
    await userEvent.click(screen.getByLabelText('Pending codes'))

    expect(onViewChange).toHaveBeenCalledWith('pending')
  })
})
