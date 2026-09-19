import { describe, expect, it } from 'vitest'

import { parsePermissionsCatalog } from '@/test/permissions-catalog'
import { ALL_PERMISSIONS, ROLE_PERMISSIONS } from './permissions'

const backend = parsePermissionsCatalog()

function sorted(values: readonly string[]): string[] {
  return [...values].sort()
}

/**
 * The drift guard. `permissions.ts` is a hand-maintained mirror of the server catalog used
 * only for presentation and navigation; this test is the mechanism that keeps it honest.
 * It fails the moment `Permissions.cs` changes and the mirror does not.
 */
describe('permissions mirror ⇄ Aveline.Api Permissions.cs', () => {
  it('parses a sane server catalog first, so a parser bug cannot masquerade as parity', () => {
    expect(backend.all).toHaveLength(25)
    expect(Object.keys(backend.roles)).toHaveLength(9)
    // The facts that have drifted before: the permission count and `billing:view:self`.
    expect(backend.all).toContain('billing:view:self')
    expect(backend.all).toContain('analytics:business:read')
  })

  it('mirrors Permissions.All exactly', () => {
    expect(sorted(ALL_PERMISSIONS)).toEqual(sorted(backend.all))
  })

  it('mirrors every canonical role grant set exactly', () => {
    expect(sorted(Object.keys(ROLE_PERMISSIONS))).toEqual(
      sorted(Object.keys(backend.roles)),
    )
    for (const [role, grants] of Object.entries(backend.roles)) {
      expect(
        sorted(ROLE_PERMISSIONS[role] ?? []),
        `role ${role} drifted from Permissions.cs`,
      ).toEqual(sorted(grants))
    }
  })

  it('keeps `admin` denied `pricing:backdate` and `owner` the full set', () => {
    expect(ROLE_PERMISSIONS.admin).not.toContain('pricing:backdate')
    expect(sorted(ROLE_PERMISSIONS.owner)).toEqual(sorted(backend.all))
    expect(backend.roles.admin).not.toContain('pricing:backdate')
  })

  it('records the three dead policies as dead, and refutes the audit’s moderator premise', () => {
    // `audit:view`, `stats:system` and `pricing:view` are registered by
    // AuthorizationConfiguration for every entry in Permissions.All
    // (`AuthorizationConfiguration.cs:265-268`) and referenced by no endpoint, while the
    // routes that own those names use `RequireRole(Owner, Admin)` (`:258,260,262`). That is
    // why the client needs the role overlay (strategy C5) rather than a permission-only gate.
    //
    // The audit claimed a `moderator` holds all three and is nevertheless 403. It holds
    // none of them (`Permissions.cs:104-106`); this assertion is the standing refutation.
    for (const dead of ['audit:view', 'stats:system', 'pricing:view']) {
      expect(backend.roles.moderator, dead).not.toContain(dead)
      expect(ROLE_PERMISSIONS.moderator, dead).not.toContain(dead)
    }

    // Only `owner` and `admin` hold the two that are exclusively role-guarded.
    for (const dead of ['audit:view', 'stats:system']) {
      const holders = Object.entries(backend.roles)
        .filter(([, grants]) => grants.includes(dead))
        .map(([role]) => role)
        .sort()
      expect(holders, dead).toEqual(['admin', 'owner'])
    }
  })
})
