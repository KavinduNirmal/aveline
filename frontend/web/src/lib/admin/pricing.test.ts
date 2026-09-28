import { describe, expect, it } from 'vitest'

import {
  LEGACY_FORMULA_BANNER,
  RECOMPUTE_DISABLED_REASON,
  RECOMPUTE_PERMISSION,
  canRecompute,
} from './pricing'

/**
 * `recompute` returns `200` with a `PricingRecomputeResult` and is guarded by `pricing:backdate`
 * (`PricingEndpoints.cs:203-204,211`), which `admin` is deliberately denied
 * (`Permissions.cs:107`). The input plan's "returns `501`, disable it" is false; the real
 * constraint is a capability, and the console must gate on the capability rather than on the
 * page's `pricing:manage` permission.
 */
describe('the recompute capability', () => {
  it('is the pricing:backdate permission, not pricing:manage', () => {
    expect(RECOMPUTE_PERMISSION).toBe('pricing:backdate')
  })

  it('is enabled for a caller holding pricing:backdate', () => {
    expect(canRecompute((permission) => permission === 'pricing:backdate')).toBe(true)
  })

  it('is disabled for an admin who holds every pricing permission except backdate', () => {
    const adminGrants = new Set(['pricing:view', 'pricing:manage'])
    expect(canRecompute((permission) => adminGrants.has(permission))).toBe(false)
  })

  it('states the accurate reason when disabled', () => {
    expect(RECOMPUTE_DISABLED_REASON).toMatch(/pricing:backdate/)
    expect(RECOMPUTE_DISABLED_REASON).not.toMatch(/501|not implemented/i)
  })

  it('warns that pricing writes are inert while the legacy formula flag is on', () => {
    expect(LEGACY_FORMULA_BANNER).toMatch(/legacy formula/i)
    expect(LEGACY_FORMULA_BANNER).toMatch(/do not change/i)
  })
})
