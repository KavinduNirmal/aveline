import { describe, expect, it } from 'vitest'

import { parseRolePolicies } from '@/test/permissions-catalog'
import { ROLE_POLICIES } from './role-policies'

const backend = parseRolePolicies()

/**
 * The drift guard for the `Gate` overlay. `ROLE_POLICIES` is a hand-written mirror of the
 * `RequireRole(...)` registrations in `AuthorizationConfiguration.cs`; this test is what keeps
 * it true, and it fails the moment the C# changes.
 */
describe('ROLE_POLICIES ⇄ AuthorizationConfiguration.cs', () => {
  it('finds the six role policies in the C#, in registration order', () => {
    expect(backend.map((entry) => entry.policy)).toEqual([
      'AdminReview',
      'StatsSystem',
      'AuditView',
      'PricingAdminRead',
      'MoneyRead',
      'MoneyOperations',
    ])
  })

  it('mirrors each policy and its roles exactly', () => {
    for (const entry of backend) {
      const mirrored = ROLE_POLICIES[entry.policy as keyof typeof ROLE_POLICIES]
      expect(mirrored, `missing mirror for ${entry.policy}`).toBeDefined()
      expect([...mirrored.anyOf], entry.policy).toEqual([...entry.anyOf])
    }
  })

  /**
   * R0. The money overlay exists because the revenue family splits read from write across
   * roles: `MoneyRead` admits a moderator (who already reads organization data), and
   * `MoneyOperations` does not. If the two ever collapse into one policy, that split is gone.
   */
  it('keeps the read overlay and the operations overlay distinct', () => {
    expect([...ROLE_POLICIES.MoneyRead.anyOf]).toEqual(['owner', 'admin', 'moderator'])
    expect([...ROLE_POLICIES.MoneyOperations.anyOf]).toEqual(['owner', 'admin'])
    expect([...ROLE_POLICIES.MoneyOperations.anyOf]).not.toContain('moderator')
  })
})
