import { describe, expect, it } from 'vitest'

import { ALL_PERMISSIONS } from './permissions'
import {
  ADMIN_DOMAINS,
  ADMIN_ROUTES,
  declaredPermissions,
  findRouteById,
  navigableRoutes,
  routesForDomain,
} from './routes'

/**
 * Registry invariants. The navigation and the router read the same list, so these are the
 * properties that keep the panel honest: nothing links to a page the caller cannot use, and
 * nothing declares a permission the server does not define.
 */
describe('ADMIN_ROUTES invariants', () => {
  it('has unique ids and unique subPaths', () => {
    const ids = ADMIN_ROUTES.map((route) => route.id)
    const subPaths = ADMIN_ROUTES.map((route) => route.subPath)
    expect(new Set(ids).size).toBe(ids.length)
    expect(new Set(subPaths).size).toBe(subPaths.length)
  })

  it('declares a gate on every non-Overview entry', () => {
    for (const route of ADMIN_ROUTES) {
      if (route.domain === 'overview') continue
      expect(route.gate, `${route.id} must declare a gate`).not.toBeNull()
    }
  })

  it('declares only permissions that exist in the catalogue', () => {
    const catalogue = new Set<string>(ALL_PERMISSIONS)
    for (const permission of declaredPermissions()) {
      expect(catalogue.has(permission), `${permission} is not a real permission`).toBe(true)
    }
  })

  it('gives every domain at least two entries', () => {
    for (const domain of ADMIN_DOMAINS) {
      expect(routesForDomain(domain).length, `domain ${domain}`).toBeGreaterThanOrEqual(2)
    }
  })

  it('uses every declared domain, and declares every used domain', () => {
    const used = new Set(ADMIN_ROUTES.map((route) => route.domain))
    expect([...used].sort()).toEqual([...ADMIN_DOMAINS].sort())
  })

  it('never links a route that is not enabled', () => {
    for (const route of navigableRoutes()) {
      expect(route.enabled, `${route.id} is linked but not enabled`).toBe(true)
    }
  })

  it('resolves its own entries by id', () => {
    expect(findRouteById('dashboard')?.subPath).toBe('dashboard')
    expect(findRouteById('does-not-exist')).toBeUndefined()
  })

  it('lands the two business entries in the new domain, each with a gate', () => {
    const business = routesForDomain('business')

    expect(business.map((route) => route.id).sort()).toEqual(['business-growth', 'business-usage'])
    for (const route of business) {
      expect(route.enabled, route.id).toBe(true)
      expect(route.gate, route.id).toEqual({
        kind: 'permission',
        permission: 'analytics:business:read',
      })
    }
  })

  it('declares analytics:business:read, and the catalogue defines it', () => {
    expect(declaredPermissions()).toContain('analytics:business:read')
    expect(ALL_PERMISSIONS).toContain('analytics:business:read')
  })

  it('routes the business entries under the business sub-path prefix', () => {
    expect(findRouteById('business-growth')?.subPath).toBe('business')
    expect(findRouteById('business-usage')?.subPath).toBe('business/usage')
  })

  /**
   * R0. The money domain. Its two gates are deliberately different, and the split is the
   * point: `MoneyRead` admits a moderator to what a boutique was billed, `MoneyOperations`
   * (which owns the Blossom ledger) does not. Collapsing them would hand a moderator the
   * Blossom adjustment surface.
   */
  it('lands the four money entries in the new domain', () => {
    const money = routesForDomain('money')

    expect(money.map((route) => route.id).sort()).toEqual([
      'blossoms',
      'revenue',
      'revenue-ledger',
      'revenue-stats',
    ])
    for (const route of money) {
      expect(route.gate, route.id).not.toBeNull()
    }
  })

  it('gates the three revenue entries on MoneyRead', () => {
    for (const id of ['revenue', 'revenue-ledger', 'revenue-stats']) {
      expect(findRouteById(id)?.gate, id).toEqual({
        kind: 'role',
        anyOf: ['owner', 'admin', 'moderator'],
      })
    }
  })

  it('gates the Blossom ledger on MoneyOperations rather than MoneyRead', () => {
    expect(findRouteById('blossoms')?.gate).toEqual({
      kind: 'role',
      anyOf: ['owner', 'admin'],
    })
    expect(findRouteById('blossoms')?.domain).toBe('money')
    expect(findRouteById('blossoms')?.subPath).toBe('blossoms')
    // The page exists (slice A6), so this entry is the one enabled money entry.
    expect(findRouteById('blossoms')?.enabled).toBe(true)
  })

  it('routes the revenue entries under the revenue sub-path prefix', () => {
    expect(findRouteById('revenue')?.subPath).toBe('revenue')
    expect(findRouteById('revenue-ledger')?.subPath).toBe('revenue/ledger')
    expect(findRouteById('revenue-stats')?.subPath).toBe('revenue/statistics')
  })

  /**
   * The three revenue pages shipped `enabled: false` from R0 to R5 so the navigation's shape and
   * these invariants were settled before the pages landed — the precedent slice A3 set. R6 flipped
   * them in the commit that mounted their `<Route>`s, which is the only moment that is safe:
   * `routes.router.test.ts` asserts the two stay in step in both directions.
   */
  it('registers and enables the three revenue pages now that they are built', () => {
    const navigable = navigableRoutes().map((route) => route.id)

    for (const id of ['revenue', 'revenue-ledger', 'revenue-stats']) {
      expect(findRouteById(id)?.enabled, id).toBe(true)
      // Enabled and linked: an operator can reach them from the panel.
      expect(navigable, id).toContain(id)
    }
  })

  it('declares the money domain, and the operations domain still holds two entries', () => {
    expect(ADMIN_DOMAINS).toContain('money')
    // `blossoms` moved out of `operations`; the domain invariant must still hold without it.
    expect(routesForDomain('operations').length).toBeGreaterThanOrEqual(2)
  })
})
