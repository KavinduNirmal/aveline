import { describe, expect, it } from 'vitest'

import type { Permission } from '@/lib/admin/permissions'
import {
  ROLE_POLICIES,
  canOpenGate,
  policyNames,
  resolveRolePolicy,
  type Gate,
} from './role-policies'

const context = (roles: string[], granted: Permission[] = []) => ({
  roles,
  can: (permission: Permission) => granted.includes(permission),
})

describe('ROLE_POLICIES mirrors AuthorizationConfiguration.cs by name', () => {
  it('registers exactly the four role policies, with the roles the C# grants', () => {
    expect(policyNames()).toEqual([
      'AdminReview',
      'StatsSystem',
      'AuditView',
      'PricingAdminRead',
    ])
    expect(ROLE_POLICIES.AdminReview.anyOf).toEqual(['moderator', 'admin', 'owner'])
    expect(ROLE_POLICIES.StatsSystem.anyOf).toEqual(['owner', 'admin'])
    expect(ROLE_POLICIES.AuditView.anyOf).toEqual(['owner', 'admin'])
    expect(ROLE_POLICIES.PricingAdminRead.anyOf).toEqual(['owner', 'admin'])
  })

  it('keeps the policy name and the registry key identical', () => {
    for (const [key, value] of Object.entries(ROLE_POLICIES)) {
      expect(value.policy, key).toBe(key)
    }
  })
})

describe('canOpenGate', () => {
  it('admits a role gate only when the caller holds one of its roles', () => {
    const audit: Gate = { kind: 'role', anyOf: ROLE_POLICIES.AuditView.anyOf }
    expect(canOpenGate(audit, context(['owner']))).toBe(true)
    expect(canOpenGate(audit, context(['admin']))).toBe(true)
    // The audit claimed a moderator holds audit:view and is nevertheless 403. It holds
    // neither, and this is the gate that says so.
    expect(canOpenGate(audit, context(['moderator'], ['audit:view']))).toBe(false)
  })

  it('admits a permission gate only when the permission is granted', () => {
    const backdate: Gate = { kind: 'permission', permission: 'pricing:backdate' }
    expect(canOpenGate(backdate, context(['admin'], ['pricing:manage']))).toBe(false)
    expect(canOpenGate(backdate, context(['owner'], ['pricing:backdate']))).toBe(true)
  })
})

describe('resolveRolePolicy', () => {
  it('refuses an unknown role in the gate', () => {
    expect(
      resolveRolePolicy(
        { policy: 'AuditView', anyOf: ['owner', 'admin'] } as (typeof ROLE_POLICIES)['AuditView'],
        context(['staff']),
      ),
    ).toBe(false)
  })
})
