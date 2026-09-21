/**
 * The tenant drift guard (T0a). `src/lib/permissions.ts` is a hand-maintained mirror of
 * `Aveline.Api/Authorization/Permissions.cs` used only for navigation gating; authoritative
 * enforcement is always server-side. This test is the mechanism that keeps it honest: it fails
 * the moment `Permissions.cs` changes and the mirror does not.
 *
 * It reuses the same parser as the admin mirror's guard (`src/test/permissions-catalog.ts`),
 * which reads the C# source directly, so there is no regeneration step to forget.
 */
import { describe, expect, it } from 'vitest'

import { parsePermissionsCatalog } from '@/test/permissions-catalog'
import { ALL_PERMISSIONS, BOUTIQUE_ROLES, ROLE_PERMISSIONS } from './permissions'

const backend = parsePermissionsCatalog()

function sorted(values: readonly string[]): string[] {
  return [...values].sort()
}

describe('tenant permissions mirror ⇄ Aveline.Api Permissions.cs', () => {
  it('parses a sane server catalog first, so a parser bug cannot masquerade as parity', () => {
    expect(backend.all).toHaveLength(31)
    expect(backend.all).toContain('customers:manage')
    expect(backend.all).toContain('team:manage')
    expect(backend.all).toContain('orders:manage')
    // The two permissions this slice finally enforces.
    expect(backend.all).toContain('catalog:manage')
    expect(backend.all).toContain('reports:view')
  })

  it('mirrors Permissions.All exactly', () => {
    expect(sorted(ALL_PERMISSIONS)).toEqual(sorted(backend.all))
  })

  it('mirrors the four boutique grant sets exactly', () => {
    // The tenant mirror deliberately holds only the four org-scoped roles (C-13), so this is
    // an exact comparison against the backend's rows for those roles, not against all nine.
    expect(sorted(Object.keys(ROLE_PERMISSIONS))).toEqual(sorted(BOUTIQUE_ROLES))
    for (const role of BOUTIQUE_ROLES) {
      expect(
        sorted(ROLE_PERMISSIONS[role] ?? []),
        `role ${role} drifted from Permissions.cs`,
      ).toEqual(sorted(backend.roles[role]))
    }
  })

  /**
   * C-13. Every membership carries one of the four `org:boutique_*` roles — the Clerk webhook
   * defaults anything unrecognised to `org:boutique_staff` — and `org:principal` exists in no
   * backend grant set. The mirror must not carry either, because the org-scoped policies can
   * never resolve them, so the extra branches were a live client/server disagreement.
   */
  it('carries exactly the four boutique roles and no phantom org:principal', () => {
    expect(sorted(Object.keys(ROLE_PERMISSIONS))).toEqual([
      'org:boutique_manager',
      'org:boutique_owner',
      'org:boutique_staff',
      'org:boutique_supervisor',
    ])
    expect(ROLE_PERMISSIONS).not.toHaveProperty('org:principal')
  })

  it('matches the backend grant sets for the four boutique roles', () => {
    for (const role of [
      'org:boutique_staff',
      'org:boutique_manager',
      'org:boutique_supervisor',
      'org:boutique_owner',
    ]) {
      expect(sorted(ROLE_PERMISSIONS[role]), `${role} drifted`).toEqual(
        sorted(backend.roles[role]),
      )
    }
  })

  it('keeps the new write permissions off org:boutique_staff', () => {
    const staff = ROLE_PERMISSIONS['org:boutique_staff']
    expect(staff).not.toContain('customers:manage')
    expect(staff).not.toContain('team:manage')
    expect(staff).not.toContain('orders:manage')
    // Q8: they may approve a customer order, and the controller's verb split is what stops
    // that grant from also cancelling it.
    expect(staff).toContain('approvals:approve')
  })

  it('keeps team:manage separate from settings:manage for a manager', () => {
    const manager = ROLE_PERMISSIONS['org:boutique_manager']
    expect(manager).toContain('team:manage')
    expect(manager).not.toContain('settings:manage')
  })
})
