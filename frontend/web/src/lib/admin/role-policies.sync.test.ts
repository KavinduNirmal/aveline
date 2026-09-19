import { describe, expect, it } from 'vitest'

import { parseRolePolicies } from '@/test/permissions-catalog'
import { ROLE_POLICIES } from './role-policies'

const backend = parseRolePolicies()

/**
 * The drift guard for the `Gate` overlay. `ROLE_POLICIES` is a hand-written mirror of four
 * `RequireRole(...)` registrations in `AuthorizationConfiguration.cs`; this test is what keeps
 * it true, and it fails the moment the C# changes.
 */
describe('ROLE_POLICIES ⇄ AuthorizationConfiguration.cs', () => {
  it('finds the four role policies in the C#', () => {
    expect(backend.map((entry) => entry.policy)).toEqual([
      'AdminReview',
      'StatsSystem',
      'AuditView',
      'PricingAdminRead',
    ])
  })

  it('mirrors each policy and its roles exactly', () => {
    for (const entry of backend) {
      const mirrored = ROLE_POLICIES[entry.policy as keyof typeof ROLE_POLICIES]
      expect(mirrored, `missing mirror for ${entry.policy}`).toBeDefined()
      expect([...mirrored.anyOf], entry.policy).toEqual([...entry.anyOf])
    }
  })
})
