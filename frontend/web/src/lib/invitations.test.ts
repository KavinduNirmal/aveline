import { describe, expect, it } from 'vitest'
import type { CreateInvitationRequest, PendingInvitationDto } from '../types/invitation'
import { INVITABLE_ROLES } from '../types/invitation'

describe('Invitations Contracts', () => {
  it('exposes only invitable boutique staff roles', () => {
    const roles = INVITABLE_ROLES.map((r) => r.value)
    expect(roles).toEqual([
      'org:boutique_supervisor',
      'org:boutique_manager',
      'org:boutique_staff',
    ])
    expect(roles).not.toContain('org:boutique_owner')
  })

  it('validates CreateInvitationRequest payload shape', () => {
    const req: CreateInvitationRequest = {
      boutiqueRole: 'org:boutique_manager',
      recipientEmail: 'staff@aveline.lk',
    }
    expect(req.boutiqueRole).toBe('org:boutique_manager')
  })

  it('can be created without an email (manual code share)', () => {
    const req: CreateInvitationRequest = { boutiqueRole: 'org:boutique_staff' }
    expect(req.recipientEmail).toBeUndefined()
  })

  it('shapes the pending invitation DTO', () => {
    const pending: PendingInvitationDto = {
      invitationId: 'inv-1',
      boutiqueRole: 'org:boutique_staff',
      recipientEmail: null,
      createdAt: '2026-09-06T00:00:00Z',
      expiresAt: '2026-09-13T00:00:00Z',
    }
    expect(pending.recipientEmail).toBeNull()
    expect(pending.boutiqueRole).toBe('org:boutique_staff')
  })
})
